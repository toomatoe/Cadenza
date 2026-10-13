using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Cadenza.Core.Exceptions;
using Cadenza.Core.Models;
using Cadenza.Core.Services.Contracts;

namespace Cadenza.Core.Services.Spotify;

/// <summary>
/// Spotify Web API client for library metadata. Requests are serialized; an expired token is refreshed
/// once, and short rate limits are retried after Spotify's Retry-After delay.
/// </summary>
public sealed class SpotifyApi(HttpClient http, SpotifyAuth auth) : IMusicCatalog, IDisposable
{
    /// <summary>Items requested per page.</summary>
    public const int PageSize = 10;

    private const string BaseUrl = "https://api.spotify.com/v1/";
    private const int MaxRateLimitRetries = 2;
    private const int PreferredArtworkWidth = 300;
    private const int SpotifyIdLength = 22;
    private static readonly TimeSpan s_maxRetryDelay = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan s_defaultRetryDelay = TimeSpan.FromSeconds(2);

    private readonly SemaphoreSlim _requestGate = new(1, 1);

    public Task<Page<Track>> SearchAsync(string query, int offset, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        var route = Route($"search?q={Uri.EscapeDataString(query.Trim())}&type=track&limit={PageSize}&offset={offset}");
        return TrackPageAsync(route, offset, container: "tracks", wrapped: false, ct);
    }

    public Task<Page<Track>> SavedTracksAsync(int offset, CancellationToken ct) =>
        TrackPageAsync(Route($"me/tracks?limit={PageSize}&offset={offset}"), offset, container: null, wrapped: true, ct);

    public Task<Page<Track>> PlaylistTracksAsync(string id, int offset, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(id);
        if (id.Length != SpotifyIdLength || id.Any(c => !char.IsAsciiLetterOrDigit(c)))
        {
            throw new ArgumentException("Invalid playlist ID.", nameof(id));
        }

        return TrackPageAsync(Route($"playlists/{id}/items?limit={PageSize}&offset={offset}"), offset, container: null, wrapped: true, ct);
    }

    public async Task<Page<Playlist>> PlaylistsAsync(int offset, CancellationToken ct)
    {
        using var document = await GetAsync(Route($"me/playlists?limit={PageSize}&offset={offset}"), ct);
        var root = document.RootElement;
        var result = new List<Playlist>();
        foreach (var item in root.GetProperty("items").EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            // Spotify renamed "tracks" to "items" in 2026; accept either.
            var count = 0;
            if ((item.TryGetProperty("items", out var items) || item.TryGetProperty("tracks", out items)) &&
                items.ValueKind == JsonValueKind.Object &&
                items.TryGetProperty("total", out var total))
            {
                count = total.GetInt32();
            }

            var owner = item.TryGetProperty("owner", out var ownerElement) ? Text(ownerElement, "display_name") : "";
            result.Add(new Playlist(Text(item, "id"), Text(item, "name"), count, Artwork(item), owner));
        }

        return new Page<Playlist>(result, offset, HasMore(root));
    }

    public void Dispose() => _requestGate.Dispose();

    private static string Route(FormattableString route) => FormattableString.Invariant(route);

    /// <param name="container">Property that holds the page (search wraps results in "tracks").</param>
    /// <param name="wrapped">Whether each item nests its track under "track" or "item".</param>
    private async Task<Page<Track>> TrackPageAsync(string route, int offset, string? container, bool wrapped, CancellationToken ct)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        using var document = await GetAsync(route, ct);
        var root = container is null ? document.RootElement : document.RootElement.GetProperty(container);
        var tracks = new List<Track>();
        foreach (var value in root.GetProperty("items").EnumerateArray())
        {
            var item = value;
            if (wrapped && !value.TryGetProperty("track", out item) && !value.TryGetProperty("item", out item))
            {
                continue;
            }

            // Skip episodes, local files and removed tracks.
            if (item.ValueKind != JsonValueKind.Object || Text(item, "type") != "track" || Text(item, "id").Length == 0)
            {
                continue;
            }

            tracks.Add(ParseTrack(item));
        }

        return new Page<Track>(tracks, offset, HasMore(root));
    }

    private static Track ParseTrack(JsonElement item)
    {
        var artists = item.GetProperty("artists").EnumerateArray().ToArray();
        var album = item.GetProperty("album");
        var id = Text(item, "id");
        return new Track(
            id,
            Text(item, "name"),
            string.Join(", ", artists.Select(artist => Text(artist, "name"))),
            artists.Length > 0 ? Text(artists[0], "id") : "",
            Text(album, "name"),
            item.GetProperty("duration_ms").GetInt32(),
            $"https://open.spotify.com/track/{id}",
            item.TryGetProperty("explicit", out var explicitContent) && explicitContent.ValueKind == JsonValueKind.True,
            Artwork(album));
    }

    private async Task<JsonDocument> GetAsync(string route, CancellationToken ct)
    {
        await _requestGate.WaitAsync(ct);
        try
        {
            var refreshed = false;
            for (var attempt = 0; ; attempt++)
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, BaseUrl + route);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await auth.GetAccessTokenAsync(ct));
                using var response = await http.SendAsync(request, ct);
                if (response.StatusCode == HttpStatusCode.Unauthorized && !refreshed)
                {
                    await auth.GetAccessTokenAsync(ct, forceRefresh: true);
                    refreshed = true;
                    continue;
                }

                if (response.StatusCode == HttpStatusCode.TooManyRequests && attempt < MaxRateLimitRetries)
                {
                    await DelayForRetryAsync(response, ct);
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    throw new SpotifyApiException(ErrorMessage(response.StatusCode), (int)response.StatusCode);
                }

                return JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            }
        }
        finally
        {
            _requestGate.Release();
        }
    }

    private static async Task DelayForRetryAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var retryAfter = response.Headers.RetryAfter;
        var delay = retryAfter?.Delta
            ?? (retryAfter?.Date is { } date ? date - DateTimeOffset.UtcNow : s_defaultRetryDelay);

        // Long rate limits are reported instead of holding the request gate.
        if (delay > s_maxRetryDelay)
        {
            throw new SpotifyApiException(ErrorMessage(HttpStatusCode.TooManyRequests), (int)HttpStatusCode.TooManyRequests);
        }

        await Task.Delay(delay < TimeSpan.Zero ? TimeSpan.Zero : delay, ct);
    }

    private static string ErrorMessage(HttpStatusCode status) => status switch
    {
        HttpStatusCode.Unauthorized => "Spotify sign-in expired. Reconnect in Settings.",
        HttpStatusCode.Forbidden => "Spotify denied this feature. Check your developer app's allowed users and endpoint access.",
        HttpStatusCode.TooManyRequests => "Spotify is rate limiting requests. Please try again later.",
        _ => "Spotify could not complete this request. Please retry."
    };

    /// <summary>Picks the smallest HTTPS image at least 300px wide, else the largest available.</summary>
    private static string Artwork(JsonElement item)
    {
        if (!item.TryGetProperty("images", out var images) || images.ValueKind != JsonValueKind.Array)
        {
            return "";
        }

        return images.EnumerateArray()
            .Where(image => image.ValueKind == JsonValueKind.Object)
            .Select(image => (Url: Text(image, "url"), Width: ImageWidth(image)))
            .Where(image => Uri.TryCreate(image.Url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps)
            .OrderBy(image => image.Width >= PreferredArtworkWidth ? 0 : 1)
            .ThenBy(image => image.Width >= PreferredArtworkWidth ? image.Width : -image.Width)
            .Select(image => image.Url)
            .FirstOrDefault() ?? "";
    }

    private static int ImageWidth(JsonElement image) =>
        image.TryGetProperty("width", out var width) && width.ValueKind == JsonValueKind.Number && width.TryGetInt32(out var size)
            ? size
            : 0;

    private static string Text(JsonElement item, string property) =>
        item.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : "";

    private static bool HasMore(JsonElement item) =>
        item.TryGetProperty("next", out var next) && next.ValueKind == JsonValueKind.String;
}
