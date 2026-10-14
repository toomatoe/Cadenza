using Cadenza.Core.Recommendations;
using Cadenza.UnitTests.Fakes;
using Moq;
using static Cadenza.UnitTests.Fixtures.RecommendationData;

namespace Cadenza.UnitTests.Recommendations;

/// <summary>The on-disk artist search cache and the saved last set of suggestions.</summary>
[TestClass]
public sealed class RecommendationCacheTests : IDisposable
{
    private const string Alpha = "Alpha";

    private readonly DirectoryInfo _folder = Directory.CreateTempSubdirectory("cadenza-recs-");
    private readonly Mock<ITrackResolver> _resolver = new();

    public RecommendationCacheTests()
    {
        _resolver.Setup(resolver => resolver.FindTracksAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string artistName, CancellationToken _) => FakeTrackResolver.TracksBy(artistName));
    }

    private string TracksPath => Path.Combine(_folder.FullName, "tracks.json");

    private string SnapshotPath => Path.Combine(_folder.FullName, "last.json");

    public void Dispose() => _folder.Delete(recursive: true);

    [TestMethod]
    public async Task ArtistTrackSearchesAreCachedOnDiskAcrossRestarts()
    {
        await new CachedTrackResolver(_resolver.Object, TracksPath, () => Now).FindTracksAsync(Alpha, CancellationToken.None);

        var again = await new CachedTrackResolver(_resolver.Object, TracksPath, () => Now).FindTracksAsync("alpha ", CancellationToken.None);

        _resolver.Verify(resolver => resolver.FindTracksAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once());
        Assert.HasCount(5, again, "Artist track searches are cached on disk across restarts");
        Assert.AreEqual(Alpha, again[0].LeadArtist, "Artist track searches are cached on disk across restarts");
    }

    [TestMethod]
    public async Task CachedSearchesExpireAfterAWeek()
    {
        var now = Now;
        var cached = new CachedTrackResolver(_resolver.Object, TracksPath, () => now);
        await cached.FindTracksAsync(Alpha, CancellationToken.None);
        now += CachedTrackResolver.Lifetime + TimeSpan.FromMinutes(1);

        await cached.FindTracksAsync(Alpha, CancellationToken.None);

        _resolver.Verify(resolver => resolver.FindTracksAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [TestMethod]
    public void LastSuggestionsAreSavedAndReloaded()
    {
        RecommendationSnapshot.Save(SnapshotPath, SampleResult(), Now);

        var loaded = RecommendationSnapshot.Load(SnapshotPath, Now.AddHours(1));

        Assert.IsTrue(
            loaded?.Result.Items is [{ Track.Id: "snap-1", Shelf: RecommendationShelf.DeeperCuts, Score.Similarity: 0.5 }],
            "The last suggestions are saved and reloaded");
    }

    [TestMethod]
    public void OldSavedSuggestionsAreIgnored()
    {
        RecommendationSnapshot.Save(SnapshotPath, SampleResult(), Now);

        var loaded = RecommendationSnapshot.Load(SnapshotPath, Now + RecommendationSnapshot.FreshFor + TimeSpan.FromMinutes(1));

        Assert.IsNull(loaded, "Old saved suggestions are ignored");
    }

    private static RecommendationResult SampleResult()
    {
        return new RecommendationResult(
            [new Recommendation(Song("snap-1", Alpha),RecommendationShelf.DeeperCuts, "You listen to Alpha a lot", new ScoreBreakdown(0.5, 0.2, 0.1, 0))],
            []);
    }
}
