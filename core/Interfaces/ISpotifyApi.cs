using Cadenza.Core.Models;
using Cadenza.Core.Models.Recommendations;

namespace Cadenza.Core.Interfaces;

/// <summary>The Spotify Web API calls Cadenza makes, beyond the catalog pages in <see cref="IMusicCatalog"/>.</summary>
public interface ISpotifyApi : IMusicCatalog, IDisposable
{
    /// <summary>The listener's top artists for a time range (needs the user-top-read scope).</summary>
    Task<IReadOnlyList<ArtistRef>> TopArtistsAsync(TimeRange range, CancellationToken cancellationToken);

    /// <summary>The listener's top tracks for a time range (needs the user-top-read scope).</summary>
    Task<IReadOnlyList<Track>> TopTracksAsync(TimeRange range, CancellationToken cancellationToken);

    /// <summary>Recently played tracks with when they were played (needs user-read-recently-played).</summary>
    Task<IReadOnlyList<DatedTrack>> RecentlyPlayedAsync(CancellationToken cancellationToken);

    /// <summary>Saved tracks with when they were saved, newest first, up to <paramref name="max"/>.</summary>
    Task<IReadOnlyList<DatedTrack>> SavedTracksWithDatesAsync(int max, CancellationToken cancellationToken);

    /// <summary>Tracks credited to an artist, found through search.</summary>
    Task<IReadOnlyList<Track>> SearchArtistTracksAsync(string artistName, CancellationToken cancellationToken);

    /// <summary>One track by Spotify ID.</summary>
    Task<Track> TrackAsync(string id, CancellationToken cancellationToken);
}
