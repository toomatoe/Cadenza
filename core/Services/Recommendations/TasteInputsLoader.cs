using System.Net;
using Cadenza.Core.Exceptions;
using Cadenza.Core.Models;
using Cadenza.Core.Models.Recommendations;
using Cadenza.Core.Services.Spotify;

namespace Cadenza.Core.Services.Recommendations;

/// <summary>Gathers <see cref="TasteInputs"/> from the listener's Spotify account and local history.</summary>
public sealed class TasteInputsLoader
{
    public const int SavedTrackLimit = 200;
    private readonly SpotifyApi _api;
    private readonly ListeningHistoryStore _history;

    public TasteInputsLoader(SpotifyApi api, ListeningHistoryStore history)
    {
        _api = api;
        _history = history;
    }

    /// <returns>The inputs, and whether the newer scopes (top items, recent plays) are missing.</returns>
    public async Task<(TasteInputs Inputs, bool MissingScopes)> LoadAsync(CancellationToken cancellationToken)
    {
        var saved = await _api.SavedTracksWithDatesAsync(SavedTrackLimit, cancellationToken);
        var topArtists = new Dictionary<TimeRange, IReadOnlyList<ArtistRef>>();
        var topTracks = new Dictionary<TimeRange, IReadOnlyList<Track>>();
        IReadOnlyList<DatedTrack> recent = [];
        var missingScopes = false;
        try
        {
            foreach (var range in Enum.GetValues<TimeRange>())
            {
                topArtists[range] = await _api.TopArtistsAsync(range, cancellationToken);
                topTracks[range] = await _api.TopTracksAsync(range, cancellationToken);
            }

            recent = await _api.RecentlyPlayedAsync(cancellationToken);
        }
        catch (SpotifyApiException error) when (error.StatusCode is (int)HttpStatusCode.Forbidden or (int)HttpStatusCode.Unauthorized)
        {
            // Tokens issued before the new scopes were added; saved tracks still work.
            missingScopes = true;
        }

        return (new TasteInputs(saved, topArtists, topTracks, recent, _history.Events), missingScopes);
    }
}
