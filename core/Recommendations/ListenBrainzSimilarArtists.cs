using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cadenza.Core.Recommendations;

/// <summary>
/// Similar artists from ListenBrainz's open, listening-session based similarity data. Artist names are
/// mapped to MusicBrainz IDs first. Only artist names and IDs are sent; never anything about the user.
/// Results are cached on disk for <see cref="CacheLifetime"/>, and MusicBrainz is called at most once
/// per second, as its usage policy requires.
/// </summary>
public sealed class ListenBrainzSimilarArtists : ISimilarArtistProvider, IDisposable
{
    public static readonly TimeSpan CacheLifetime = TimeSpan.FromDays(14);

    private const string UserAgent = "Cadenza/1.0 (https://github.com/toomatoe/Cadenza)";
    private const string MusicBrainzSearch = "https://musicbrainz.org/ws/2/artist?limit=5&fmt=json&query=";
    private const string SimilarArtistsUrl = "https://labs.api.listenbrainz.org/similar-artists/json?artist_mbids=";

    // ListenBrainz's session-based similarity: listening sessions over ~20 years, top 100 per artist.
    private const string Algorithm = "session_based_days_7500_session_300_contribution_5_threshold_10_limit_100_filter_True_skip_30";
    private const int MinimumMatchScore = 90;
    private static readonly TimeSpan s_musicBrainzInterval = TimeSpan.FromMilliseconds(1100);
    private static readonly JsonSerializerOptions s_json = new() { WriteIndented = false };

    private readonly HttpClient _http;
    private readonly string? _cachePath;
    private readonly SemaphoreSlim _musicBrainzGate = new(1, 1);
    private readonly Func<DateTimeOffset> _clock;
    private DateTimeOffset _lastMusicBrainzCall = DateTimeOffset.MinValue;
    private Cache _cache = new();

    /// <param name="cachePath">JSON cache file, or null to cache in memory only.</param>
    public ListenBrainzSimilarArtists(HttpClient http, string? cachePath, Func<DateTimeOffset>? clock = null)
    {
        _http = http;
        _cachePath = cachePath;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        LoadCache();
    }

    public async Task<IReadOnlyList<SimilarArtist>> GetSimilarAsync(string artistName, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(artistName);
        var mbid = await FindArtistIdAsync(artistName, ct);
        if (mbid is null)
        {
            return [];
        }

        if (_cache.Similar.TryGetValue(mbid, out var cached) && Fresh(cached.Fetched))
        {
            return cached.Artists;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(SimilarArtistsUrl + mbid + "&algorithm=" + Algorithm));
        request.Headers.UserAgent.ParseAdd(UserAgent);
        using var response = await _http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            return [];
        }

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var entries = document.RootElement.ValueKind == JsonValueKind.Array
            ? document.RootElement.EnumerateArray()
                .Where(item => item.TryGetProperty("name", out _) && item.TryGetProperty("score", out _))
                .Select(item => (
                    Name: item.GetProperty("name").GetString() ?? "",
                    Id: item.TryGetProperty("artist_mbid", out var id) ? id.GetString() ?? "" : "",
                    Score: item.GetProperty("score").GetDouble()))
                .Where(item => item.Name.Length > 0)
                .ToArray()
            : [];
        var max = entries.Select(entry => entry.Score).DefaultIfEmpty(0).Max();
        var artists = entries
            .OrderByDescending(entry => entry.Score)
            .Select(entry => new SimilarArtist(entry.Name, entry.Id, max > 0 ? entry.Score / max : 0))
            .ToArray();
        _cache.Similar[mbid] = new CachedSimilar(_clock(), artists);
        SaveCache();
        return artists;
    }

    public void Dispose() => _musicBrainzGate.Dispose();

    /// <summary>Finds the MusicBrainz artist ID for an exact (case-insensitive) name match with a high search score.</summary>
    private async Task<string?> FindArtistIdAsync(string artistName, CancellationToken ct)
    {
        var key = TasteProfile.Key(artistName);
        if (_cache.ArtistIds.TryGetValue(key, out var cached) && Fresh(cached.Fetched))
        {
            return cached.Id;
        }

        await _musicBrainzGate.WaitAsync(ct);
        try
        {
            var wait = _lastMusicBrainzCall + s_musicBrainzInterval - _clock();
            if (wait > TimeSpan.Zero)
            {
                await Task.Delay(wait, ct);
            }

            var query = Uri.EscapeDataString($"artist:\"{artistName.Replace("\"", "", StringComparison.Ordinal)}\"");
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(MusicBrainzSearch + query));
            request.Headers.UserAgent.ParseAdd(UserAgent);
            using var response = await _http.SendAsync(request, ct);
            _lastMusicBrainzCall = _clock();
            string? id = null;
            if (response.IsSuccessStatusCode)
            {
                using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
                if (document.RootElement.TryGetProperty("artists", out var artists))
                {
                    id = artists.EnumerateArray()
                        .Where(artist => artist.TryGetProperty("score", out var score) && ParseScore(score) >= MinimumMatchScore)
                        .Where(artist => artist.TryGetProperty("name", out var name) && TasteProfile.Key(name.GetString() ?? "") == key)
                        .Select(artist => artist.GetProperty("id").GetString())
                        .FirstOrDefault();
                }
            }

            _cache.ArtistIds[key] = new CachedId(_clock(), id);
            SaveCache();
            return id;
        }
        finally
        {
            _musicBrainzGate.Release();
        }
    }

    // MusicBrainz returns the score as a number; older responses used a string.
    private static int ParseScore(JsonElement score) =>
        score.ValueKind == JsonValueKind.Number ? score.GetInt32()
        : int.TryParse(score.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;

    private bool Fresh(DateTimeOffset fetched) => _clock() - fetched < CacheLifetime;

    private void LoadCache()
    {
        if (_cachePath is null || !File.Exists(_cachePath))
        {
            return;
        }

        try
        {
            _cache = JsonSerializer.Deserialize<Cache>(File.ReadAllText(_cachePath), s_json) ?? new Cache();
        }
        catch (Exception error) when (error is IOException or JsonException)
        {
            _cache = new Cache();
        }
    }

    private void SaveCache()
    {
        if (_cachePath is null)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_cachePath)!);
            File.WriteAllText(_cachePath, JsonSerializer.Serialize(_cache, s_json));
        }
        catch (IOException)
        {
            // The cache is an optimisation; failing to write it only costs a refetch later.
        }
    }

    private sealed class Cache
    {
        [JsonPropertyName("artistIds")]
        public Dictionary<string, CachedId> ArtistIds { get; init; } = [];

        [JsonPropertyName("similar")]
        public Dictionary<string, CachedSimilar> Similar { get; init; } = [];
    }

    private sealed record CachedId(DateTimeOffset Fetched, string? Id);

    private sealed record CachedSimilar(DateTimeOffset Fetched, SimilarArtist[] Artists);
}
