using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Cadenza.Core;

public sealed class SpotifyApi(HttpClient http, SpotifyAuth auth)
{
    private readonly SemaphoreSlim requestGate = new(1, 1);

    public Task<Page<Track>> SearchAsync(string query, int offset, CancellationToken ct) =>
        TrackPageAsync($"search?q={Uri.EscapeDataString(query.Trim())}&type=track&limit=10&offset={offset}", offset, "tracks", false, ct);
    public Task<Page<Track>> SavedTracksAsync(int offset, CancellationToken ct) =>
        TrackPageAsync($"me/tracks?limit=10&offset={offset}", offset, null, true, ct);
    public Task<Page<Track>> PlaylistTracksAsync(string id, int offset, CancellationToken ct)
    {
        if (id.Length != 22 || id.Any(c => !char.IsAsciiLetterOrDigit(c))) throw new ArgumentException("Invalid playlist ID.");
        return TrackPageAsync($"playlists/{id}/items?limit=10&offset={offset}", offset, null, true, ct);
    }

    public async Task<Page<Playlist>> PlaylistsAsync(int offset, CancellationToken ct)
    {
        using var document = await GetAsync($"me/playlists?limit=10&offset={offset}", ct);
        var root = document.RootElement;
        var result = new List<Playlist>();
        foreach (var item in root.GetProperty("items").EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            var count = 0;
            if ((item.TryGetProperty("items", out var items) || item.TryGetProperty("tracks", out items)) &&
                items.ValueKind == JsonValueKind.Object && items.TryGetProperty("total", out var total)) count = total.GetInt32();
            result.Add(new Playlist(Text(item, "id"), Text(item, "name"), count, Artwork(item),
                item.TryGetProperty("owner", out var owner) ? Text(owner, "display_name") : ""));
        }
        return new Page<Playlist>(result, offset, HasMore(root));
    }

    private async Task<Page<Track>> TrackPageAsync(string route, int offset, string? container, bool wrapped, CancellationToken ct)
    {
        if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));
        using var document = await GetAsync(route, ct);
        var root = container is null ? document.RootElement : document.RootElement.GetProperty(container);
        var tracks = new List<Track>();
        foreach (var value in root.GetProperty("items").EnumerateArray())
        {
            var item = value;
            if (wrapped && (!value.TryGetProperty("track", out item) && !value.TryGetProperty("item", out item))) continue;
            if (item.ValueKind != JsonValueKind.Object || Text(item, "type") != "track" || Text(item, "id").Length == 0) continue;
            var artists = item.GetProperty("artists").EnumerateArray().ToArray();
            var name = string.Join(", ", artists.Select(a => Text(a, "name")));
            var id = Text(item, "id");
            tracks.Add(new Track(id, Text(item, "name"), name, artists.Length > 0 ? Text(artists[0], "id") : "",
                Text(item.GetProperty("album"), "name"), item.GetProperty("duration_ms").GetInt32(),
                $"https://open.spotify.com/track/{id}", item.TryGetProperty("explicit", out var e) && e.ValueKind == JsonValueKind.True, Artwork(item.GetProperty("album"))));
        }
        return new Page<Track>(tracks, offset, HasMore(root));
    }

    private async Task<JsonDocument> GetAsync(string route, CancellationToken ct)
    {
        await requestGate.WaitAsync(ct);
        try
        {
            var refreshed = false;
            for (var attempt = 0; ; attempt++)
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.spotify.com/v1/" + route);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await auth.GetAccessTokenAsync(ct));
                using var response = await http.SendAsync(request, ct);
                if (response.StatusCode == HttpStatusCode.Unauthorized && !refreshed)
                {
                    await auth.GetAccessTokenAsync(ct, forceRefresh: true);
                    refreshed = true;
                    continue;
                }
                if ((int)response.StatusCode == 429 && attempt < 2)
                {
                    var delay = response.Headers.RetryAfter?.Delta ??
                        (response.Headers.RetryAfter?.Date is { } date ? date - DateTimeOffset.UtcNow : TimeSpan.FromSeconds(2));
                    if (delay > TimeSpan.FromSeconds(30)) throw new SpotifyApiException("Spotify is rate limiting requests. Please try again later.", 429);
                    await Task.Delay(delay < TimeSpan.Zero ? TimeSpan.Zero : delay, ct);
                    continue;
                }
                if (!response.IsSuccessStatusCode)
                {
                    var message = (int)response.StatusCode switch
                    {
                        401 => "Spotify sign-in expired. Reconnect in Settings.",
                        403 => "Spotify denied this feature. Check your developer app's allowed users and endpoint access.",
                        429 => "Spotify is rate limiting requests. Please try again later.",
                        _ => "Spotify could not complete this request. Please retry."
                    };
                    throw new SpotifyApiException(message, (int)response.StatusCode);
                }
                return JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            }
        }
        finally { requestGate.Release(); }
    }

    private static string Artwork(JsonElement item)
    {
        if (!item.TryGetProperty("images", out var images) || images.ValueKind != JsonValueKind.Array) return "";
        // Prefer a bounded thumbnail over the full-resolution cover.
        var candidates = images.EnumerateArray().Where(image => image.ValueKind == JsonValueKind.Object)
            .Select(image => new { Url = Text(image, "url"), Width = image.TryGetProperty("width", out var width) && width.ValueKind == JsonValueKind.Number && width.TryGetInt32(out var size) ? size : 0 })
            .Where(image => Uri.TryCreate(image.Url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps)
            .OrderBy(image => image.Width >= 300 ? 0 : 1).ThenBy(image => image.Width >= 300 ? image.Width : -image.Width);
        return candidates.FirstOrDefault()?.Url ?? "";
    }

    private static string Text(JsonElement item, string property) =>
        item.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : "";
    private static bool HasMore(JsonElement item) => item.TryGetProperty("next", out var next) && next.ValueKind == JsonValueKind.String;
}
