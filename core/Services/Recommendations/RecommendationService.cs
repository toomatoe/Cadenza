using Cadenza.Core.Interfaces;
using Cadenza.Core.Models;
using Cadenza.Core.Models.Recommendations;

namespace Cadenza.Core.Services.Recommendations;

/// <summary>
/// Wires the recommendation pieces together for the "For you" page. The engine, caches and similarity
/// lookups are created on the first refresh, so the feature costs nothing until it's used.
/// </summary>
public sealed class RecommendationService : IRecommendationService, IDisposable
{
    private const string HistoryFile = "listening-history.json";
    private const string SimilarityCacheFile = "recommendation-cache.json";
    private const string TrackCacheFile = "recommendation-tracks.json";
    private const string SnapshotFile = "recommendations-last.json";

    private readonly HttpClient _http;
    private readonly ISpotifyApi _spotify;
    private readonly string _dataFolder;
    private readonly TimeProvider _clock;
    private IListeningHistoryStore? _history;
    private ListenBrainzSimilarArtists? _similarArtists;
    private CachedTrackResolver? _artistTracks;
    private RecommendationEngine? _engine;

    /// <param name="http">The app's shared HTTP client (used for ListenBrainz and MusicBrainz).</param>
    /// <param name="spotify">The signed-in Spotify API.</param>
    /// <param name="dataFolder">Where history, caches and the last suggestions are kept.</param>
    /// <param name="clock">Time source; <see cref="TimeProvider.System"/> outside tests.</param>
    public RecommendationService(HttpClient http, ISpotifyApi spotify, string dataFolder, TimeProvider clock)
    {
        _http = http;
        _spotify = spotify;
        _dataFolder = dataFolder;
        _clock = clock;
    }

    private IListeningHistoryStore History => _history ??= new ListeningHistoryStore(DataPath(HistoryFile));

    /// <inheritdoc/>
    public (RecommendationResult Result, DateTimeOffset MadeAt)? LoadSaved() =>
        RecommendationSnapshot.Load(DataPath(SnapshotFile), _clock.GetUtcNow());

    /// <inheritdoc/>
    public async Task<(RecommendationResult Result, bool MissingScopes)> RefreshAsync(double discovery, CancellationToken cancellationToken)
    {
        _similarArtists ??= new ListenBrainzSimilarArtists(_http, DataPath(SimilarityCacheFile));
        _artistTracks ??= new CachedTrackResolver(new SpotifyTrackResolver(_spotify), DataPath(TrackCacheFile));
        _engine ??= new RecommendationEngine(_similarArtists, _artistTracks);

        var (inputs, missingScopes) = await new TasteInputsLoader(_spotify, History).LoadAsync(cancellationToken);
        var now = _clock.GetUtcNow();
        var profile = TasteProfile.Build(inputs, now);
        var result = await _engine.RecommendAsync(profile, discovery, History.RecentlySuggested, cancellationToken);
        History.RememberSuggestions(result.Items.Select(item => item.Track.Id));
        RecommendationSnapshot.Save(DataPath(SnapshotFile), result, now);

        return (result, missingScopes);
    }

    /// <inheritdoc/>
    public void RecordListening(ListeningEventKind kind, Track track) =>
        History.Record(new ListeningEvent(kind, track.Id, track.LeadArtist, _clock.GetUtcNow()));

    /// <inheritdoc/>
    public void Forget()
    {
        History.Clear();
        RecommendationSnapshot.Delete(DataPath(SnapshotFile));
        _artistTracks?.Clear();
    }

    public void Dispose() => _similarArtists?.Dispose();

    private string DataPath(string fileName) => Path.Combine(_dataFolder, fileName);
}
