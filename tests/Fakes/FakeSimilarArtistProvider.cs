using Cadenza.Core.Interfaces;
using Cadenza.Core.Models.Recommendations;

namespace Cadenza.UnitTests.Fakes;

/// <summary>Answers similar-artist lookups from a fixed map; unknown artists have no neighbours.</summary>
internal sealed class FakeSimilarArtistProvider : ISimilarArtistProvider
{
    private readonly Dictionary<string, SimilarArtist[]> _map;

    public FakeSimilarArtistProvider(Dictionary<string, SimilarArtist[]> map)
    {
        _map = map;
    }

    public Task<IReadOnlyList<SimilarArtist>> GetSimilarAsync(string artistName, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<SimilarArtist>>(_map.TryGetValue(artistName, out var similar) ? similar : []);
}
