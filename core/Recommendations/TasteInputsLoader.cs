using System.Net;
using Cadenza.Core.Exceptions;
using Cadenza.Core.Models;
using Cadenza.Core.Services.Spotify;

namespace Cadenza.Core.Recommendations;

/// <summary>Gathers <see cref="TasteInputs"/> from the listener's Spotify account and local history.</summary>
public sealed class TasteInputsLoader(SpotifyApi api, ListeningHistoryStore history)
{
    public const int SavedTrackLimit = 200;

    /// <returns>The inputs, and whether the newer scopes (top items, recent plays) are missing.</returns>
    public async Task<(TasteInputs Inputs, bool MissingScopes)> LoadAsync(CancellationToken ct)
    {
        var saved = await api.SavedTracksWithDatesAsync(SavedTrackLimit, ct);
        var topArtists = new Dictionary<TimeRange, IReadOnlyList<ArtistRef>>();
        var topTracks = new Dictionary<TimeRange, IReadOnlyList<Track>>();
        IReadOnlyList<DatedTrack> recent = [];
        var missingScopes = false;
        try
        {
            foreach (var range in Enum.GetValues<TimeRange>())
            {
                topArtists[range] = await api.TopArtistsAsync(range, ct);
                topTracks[range] = await api.TopTracksAsync(range, ct);
            }

            recent = await api.RecentlyPlayedAsync(ct);
        }
        catch (SpotifyApiException error) when (error.StatusCode is (int)HttpStatusCode.Forbidden or (int)HttpStatusCode.Unauthorized)
        {
            // Tokens issued before the new scopes were added; saved tracks still work.
            missingScopes = true;
        }

        return (new TasteInputs(saved, topArtists, topTracks, recent, history.Events), missingScopes);
    }
}
