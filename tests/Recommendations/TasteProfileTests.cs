using Cadenza.Core.Models.Recommendations;
using Cadenza.Core.Services.Recommendations;
using static Cadenza.UnitTests.Fixtures.RecommendationData;

namespace Cadenza.UnitTests.Recommendations;

[TestClass]
public sealed class TasteProfileTests
{
    private const string Gamma = "Gamma";
    private const string Beta = "Beta";

    [TestMethod]
    public void DecayHalvesAfterOneHalfLife()
    {
        var decay = TasteProfile.Decay(TimeSpan.FromDays(14), TimeSpan.FromDays(14));

        Assert.AreEqual(0.5, decay, 1e-9, "Decay halves after one half-life");
    }

    [TestMethod]
    public void RankFactorRunsFromOneToPointTwo()
    {
        Assert.AreEqual(1.0, TasteProfile.RankFactor(0, 50), "Rank factor starts at 1");
        Assert.AreEqual(0.2, TasteProfile.RankFactor(49, 50), 1e-9, "Rank factor ends at 0.2");
    }

    [TestMethod]
    public void ShortTermTopArtistOutweighsTwoSavedTracks()
    {
        var profile = SampleProfile();

        Assert.IsGreaterThan(profile.Affinity("Alpha"), profile.Affinity(Beta), "A short-term top artist outweighs two saved tracks");
    }

    [TestMethod]
    public void SkipsAndNotInterestedMakeAffinityNegative() =>
        Assert.IsLessThan(0.0, SampleProfile().Affinity(Gamma), "Skips and 'not interested' make affinity negative");

    [TestMethod]
    public void NormalisedAffinityIsClampedToZeroThroughOne()
    {
        var profile = SampleProfile();

        Assert.AreEqual(0.0, profile.NormalizedAffinity(Gamma), "Normalised affinity is clamped to 0");
        Assert.AreEqual(1.0, profile.NormalizedAffinity(Beta), "Normalised affinity is clamped to 1");
    }

    [TestMethod]
    public void KnownAndRejectedTracksAreTracked()
    {
        var profile = SampleProfile();

        Assert.Contains("s1", profile.KnownTrackIds, "Known tracks are tracked");
        Assert.Contains("x2", profile.RejectedTrackIds, "Rejected tracks are tracked");
    }

    [TestMethod]
    public void SeedsAreTheHighestAffinityArtists() =>
        Assert.AreEqual(Beta, SampleProfile().TopArtists(1)[0].Name, "Seeds are the highest-affinity artists");

    [TestMethod]
    public void NoDataGivesAnEmptyProfile() =>
        Assert.IsTrue(TasteProfile.Build(TasteInputs.Empty, Now).IsEmpty, "No data gives an empty profile");

    /// <summary>Two saved Alpha tracks, Beta as a short-term top artist, and a skip plus "not interested" for Gamma.</summary>
    private static TasteProfile SampleProfile()
    {
        var inputs = Inputs(
            saved: [Dated(Song("s1", "Alpha"), 0), Dated(Song("s2", "Alpha"), 400)],
            topArtists: [new ArtistRef("b", Beta)],
            events:
            [
                new ListeningEvent(ListeningEventKind.Skipped, "x1", Gamma, Now),
                new ListeningEvent(ListeningEventKind.NotInterested, "x2", Gamma, Now),
            ]);

        return TasteProfile.Build(inputs, Now);
    }
}
