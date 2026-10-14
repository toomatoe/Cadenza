using Cadenza.Core.Interfaces;
using Cadenza.Core.Models;

namespace Cadenza.Core.Services.Recommendations;

/// <summary>Resolves candidate artists to playable Spotify tracks through search.</summary>
public sealed class SpotifyTrackResolver : ITrackResolver
{
    private readonly ISpotifyApi _api;

    public SpotifyTrackResolver(ISpotifyApi api)
    {
        _api = api;
    }

    public Task<IReadOnlyList<Track>> FindTracksAsync(string artistName, CancellationToken cancellationToken) =>
        _api.SearchArtistTracksAsync(artistName, cancellationToken);
}
