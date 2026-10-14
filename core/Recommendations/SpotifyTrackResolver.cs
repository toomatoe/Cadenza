using Cadenza.Core.Models;
using Cadenza.Core.Services.Spotify;

namespace Cadenza.Core.Recommendations;

/// <summary>Resolves candidate artists to playable Spotify tracks through search.</summary>
public sealed class SpotifyTrackResolver(SpotifyApi api) : ITrackResolver
{
    public Task<IReadOnlyList<Track>> FindTracksAsync(string artistName, CancellationToken cancellationToken) =>
        api.SearchArtistTracksAsync(artistName, cancellationToken);
}
