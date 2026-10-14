using Cadenza.Core.Interfaces;
using Cadenza.Core.Models;
using Cadenza.Core.Services.Spotify;

namespace Cadenza.Core.Services.Recommendations;

/// <summary>Resolves candidate artists to playable Spotify tracks through search.</summary>
public sealed class SpotifyTrackResolver : ITrackResolver
{
    private readonly SpotifyApi _api;

    public SpotifyTrackResolver(SpotifyApi api)
    {
        _api = api;
    }

    public Task<IReadOnlyList<Track>> FindTracksAsync(string artistName, CancellationToken cancellationToken) =>
        _api.SearchArtistTracksAsync(artistName, cancellationToken);
}
