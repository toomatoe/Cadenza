using Cadenza.Core.Interfaces;
using Cadenza.Core.Models.Recommendations;
using Cadenza.Core.Services.Recommendations;
using Cadenza.UnitTests.Fakes;
using Moq;
using static Cadenza.UnitTests.Fixtures.RecommendationData;

namespace Cadenza.UnitTests.Recommendations;

[TestClass]
public sealed class RecommendationEngineTests
{
    private const double Balanced = 0.3;

    private static readonly HashSet<string> _none = [];

    private readonly RecommendationEngine _engine = new(
        new FakeSimilarArtistProvider(new Dictionary<string, SimilarArtist[]>
        {
            ["Alpha"] = [new("Delta", "d", 1.0), new("Epsilon", "e", 0.6), new("Hated", "h", 0.9)],
            ["Beta"] = [new("Delta", "d", 0.8), new("Zeta", "z", 0.5)],
        }),
        new FakeTrackResolver());

    [TestMethod]
    public async Task EngineReturnsRecommendations()
    {
        var result = await _engine.RecommendAsync(SampleProfile(), Balanced, _none, CancellationToken.None);

        Assert.IsNotEmpty(result.Items, "Engine returns recommendations");
    }

    [TestMethod]
    public async Task SavedTracksAreNeverRecommended()
    {
        var result = await _engine.RecommendAsync(SampleProfile(), Balanced, _none, CancellationToken.None);

        var ids = result.Items.Select(item => item.Track.Id).ToArray();
        Assert.DoesNotContain("alpha-1", ids, "Already-saved tracks are never recommended");
        Assert.DoesNotContain("alpha-2", ids, "Already-saved tracks are never recommended");
    }

    [TestMethod]
    public async Task ArtistsWithNegativeAffinityAreExcluded()
    {
        var result = await _engine.RecommendAsync(SampleProfile(), Balanced, _none, CancellationToken.None);

        Assert.DoesNotContain(item => item.Track.LeadArtist == "Hated", result.Items, "Artists with negative affinity are excluded");
    }

    [TestMethod]
    public async Task AtMostTwoTracksPerArtist()
    {
        var result = await _engine.RecommendAsync(SampleProfile(), Balanced, _none, CancellationToken.None);

        Assert.IsTrue(
            result.Items.GroupBy(item => item.Track.LeadArtist).All(group => group.Count() <= RecommendationEngine.MaxPerArtist),
            "At most two tracks per artist");
    }

    [TestMethod]
    public async Task ReasonsNameTheSeedThatContributedMost()
    {
        var result = await _engine.RecommendAsync(SampleProfile(), Balanced, _none, CancellationToken.None);

        Assert.IsTrue(
            result.Items.Where(item => item.Track.LeadArtist == "Delta").All(item => item.Reason == "Similar to Alpha"),
            "Reasons name the seed that contributed most");
    }

    [TestMethod]
    public async Task UnsavedTracksByFavouriteArtistsAreDeeperCuts()
    {
        var result = await _engine.RecommendAsync(SampleProfile(), Balanced, _none, CancellationToken.None);

        Assert.IsTrue(
            result.Items.Where(item => item.Track.LeadArtist == "Alpha").All(item => item.Shelf == RecommendationShelf.DeeperCuts),
            "Unsaved tracks by favourite artists are deeper cuts");
    }

    [TestMethod]
    public async Task ScoreIsTheSumOfItsDocumentedTerms()
    {
        var result = await _engine.RecommendAsync(SampleProfile(), Balanced, _none, CancellationToken.None);

        Assert.IsTrue(
            result.Items.All(item => Math.Abs(item.Score.Total - (item.Score.Similarity + item.Score.Affinity + item.Score.Novelty - item.Score.Penalty)) < 1e-9),
            "Score is the sum of its documented terms");
    }

    [TestMethod]
    public async Task SameInputsGiveTheSameRanking()
    {
        var profile = SampleProfile();
        var first = await _engine.RecommendAsync(profile, Balanced, _none, CancellationToken.None);

        var again = await _engine.RecommendAsync(profile, Balanced, _none, CancellationToken.None);

        Assert.IsTrue(again.Items.Select(item => item.Track.Id).SequenceEqual(first.Items.Select(item => item.Track.Id)), "Same inputs give the same ranking");
    }

    [TestMethod]
    public async Task RaisingDiscoveryFavoursNewArtists()
    {
        var profile = SampleProfile();

        var familiar = await _engine.RecommendAsync(profile, 0.0, _none, CancellationToken.None);
        var adventurous = await _engine.RecommendAsync(profile, 1.0, _none, CancellationToken.None);

        Assert.IsGreaterThanOrEqualTo(NewShareOfTop(familiar), NewShareOfTop(adventurous), "Raising discovery favours new artists");
    }

    [TestMethod]
    public async Task RecentlySuggestedTracksArePushedDown()
    {
        var profile = SampleProfile();
        var top = (await _engine.RecommendAsync(profile, Balanced, _none, CancellationToken.None)).Items[0].Track.Id;

        var penalised = await _engine.RecommendAsync(profile, Balanced, new HashSet<string> { top }, CancellationToken.None);

        Assert.IsTrue(
            penalised.Items.FirstOrDefault(item => item.Track.Id == top) is not { } repeat || repeat.Score.Penalty > 0,
            "Recently suggested tracks are pushed down");
    }

    [TestMethod]
    public async Task WithoutTheSimilarityServiceOnlyDeeperCutsAreOfferedWithAWarning()
    {
        var similar = new Mock<ISimilarArtistProvider>();
        similar.Setup(provider => provider.GetSimilarAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("offline"));
        var offline = new RecommendationEngine(similar.Object, new FakeTrackResolver());

        var degraded = await offline.RecommendAsync(SampleProfile(), Balanced, _none, CancellationToken.None);

        Assert.IsNotEmpty(degraded.Warnings, "Without the similarity service, a warning is shown");
        Assert.IsTrue(degraded.Items.All(item => item.Shelf == RecommendationShelf.DeeperCuts), "Without the similarity service, only deeper cuts are offered");
    }

    [TestMethod]
    public async Task NoTasteDataGivesNoRecommendations()
    {
        var empty = await _engine.RecommendAsync(TasteProfile.Build(TasteInputs.Empty, Now), Balanced, _none, CancellationToken.None);

        Assert.IsEmpty(empty.Items, "No taste data gives no recommendations");
    }

    [TestMethod]
    public async Task TopTenDrawsOnMoreThanOneFavouriteArtist()
    {
        var result = await RecommendForOneHeavyFavouriteAsync();

        var seedsInTopTen = result.Items.Take(10)
            .Select(item => item.Reason)
            .Where(reason => reason.StartsWith("Similar to ", StringComparison.Ordinal))
            .Distinct()
            .Count();
        Assert.IsGreaterThanOrEqualTo(2, seedsInTopTen, "The top ten draws on more than one favourite artist");
    }

    [TestMethod]
    public async Task OneFavouriteFillsAtMostMaxPerSeedSuggestions()
    {
        var result = await RecommendForOneHeavyFavouriteAsync();

        Assert.IsLessThanOrEqualTo(
            RecommendationEngine.MaxPerSeed,
            result.Items.Count(item => item.Reason == "Similar to Alpha"),
            "One favourite fills at most MaxPerSeed suggestions");
    }

    [TestMethod]
    public async Task LighterFavouritesStillGetSuggestions()
    {
        var result = await RecommendForOneHeavyFavouriteAsync();

        Assert.Contains(item => item.Reason == "Similar to Gamma", result.Items, "Lighter favourites still get suggestions");
    }

    private static double NewShareOfTop(RecommendationResult result) =>
        result.Items.Take(4).Count(item => item.Shelf != RecommendationShelf.DeeperCuts) / 4.0;

    /// <summary>Two saved Alpha tracks, Alpha and Beta as top artists, and Hated marked "not interested".</summary>
    private static TasteProfile SampleProfile()
    {
        var inputs = Inputs(
            saved: [Dated(Song("alpha-1", "Alpha"), 1), Dated(Song("alpha-2", "Alpha"), 2)],
            topArtists: [new ArtistRef("a", "Alpha"), new ArtistRef("b", "Beta")],
            events: [new ListeningEvent(ListeningEventKind.NotInterested, "hated-1", "Hated", Now)]);

        return TasteProfile.Build(inputs, Now);
    }

    /// <summary>One favourite with many saved tracks must not take over the list.</summary>
    private static async Task<RecommendationResult> RecommendForOneHeavyFavouriteAsync()
    {
        static SimilarArtist[] Neighbours(string prefix) =>
            Enumerable.Range(1, 10).Select(i => new SimilarArtist($"{prefix}{i}", $"{prefix}{i}", 1.0 - (i * 0.04))).ToArray();
        var similar = new FakeSimilarArtistProvider(new Dictionary<string, SimilarArtist[]>
        {
            ["Alpha"] = Neighbours("A"),
            ["Beta"] = Neighbours("B"),
            ["Gamma"] = Neighbours("G"),
        });
        var saved = Enumerable.Range(1, 30).Select(i => Dated(Song($"alpha-saved-{i}", "Alpha"), i))
            .Concat([Dated(Song("beta-saved", "Beta"), 1), Dated(Song("gamma-saved", "Gamma"), 1)])
            .ToArray();
        var profile = TasteProfile.Build(Inputs(saved, [], []), Now);

        return await new RecommendationEngine(similar, new FakeTrackResolver())
            .RecommendAsync(profile, Balanced, new HashSet<string>(), CancellationToken.None);
    }
}
