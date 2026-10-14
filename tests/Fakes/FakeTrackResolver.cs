using Cadenza.Core.Interfaces;
using Cadenza.Core.Models;
using Cadenza.UnitTests.Fixtures;

namespace Cadenza.UnitTests.Fakes;

/// <summary>Every artist has five tracks, with IDs like "alpha-1"; Alpha's first two are the saved ones in the tests.</summary>
internal sealed class FakeTrackResolver : ITrackResolver
{
    public static IReadOnlyList<Track> TracksBy(string artistName) =>
        Enumerable.Range(1, 5)
            .Select(i => RecommendationData.Song($"{artistName.ToLowerInvariant()}-{i}", artistName))
            .ToArray();

    public Task<IReadOnlyList<Track>> FindTracksAsync(string artistName, CancellationToken cancellationToken) =>
        Task.FromResult(TracksBy(artistName));
}
