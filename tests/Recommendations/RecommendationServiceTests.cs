using Cadenza.Core.Interfaces;
using Cadenza.Core.Models;
using Cadenza.Core.Models.Recommendations;
using Cadenza.Core.Services.Recommendations;
using Moq;

namespace Cadenza.UnitTests.Recommendations;

[TestClass]
public sealed class RecommendationServiceTests
{
    private DirectoryInfo _folder = null!;

    [TestInitialize]
    public void CreateFolder() => _folder = Directory.CreateTempSubdirectory("cadenza-rec-service-");

    [TestCleanup]
    public void DeleteFolder() => _folder.Delete(recursive: true);

    [TestMethod]
    public void NothingSavedMeansNoInstantSuggestions()
    {
        using var service = CreateService();

        Assert.IsNull(service.LoadSaved());
    }

    [TestMethod]
    public void ListeningSignalsArePersistedAndForgotten()
    {
        var track = new Track("t1", "Song", "Alpha", "a1", "Album", 1000, "https://open.spotify.com/track/t1", false);
        using (var service = CreateService())
            service.RecordListening(ListeningEventKind.Liked, track);

        var history = new ListeningHistoryStore(Path.Combine(_folder.FullName, "listening-history.json"));
        Assert.HasCount(1, history.Events);
        Assert.AreEqual("Alpha", history.Events[0].ArtistName);

        using (var service = CreateService())
            service.Forget();

        Assert.IsEmpty(new ListeningHistoryStore(Path.Combine(_folder.FullName, "listening-history.json")).Events);
    }

    [TestMethod]
    public void CreatingTheServiceMakesNoNetworkCalls()
    {
        var spotify = new Mock<ISpotifyApi>(MockBehavior.Strict);

        using var service = new RecommendationService(new HttpClient(), spotify.Object, _folder.FullName, TimeProvider.System);
        service.LoadSaved();

        spotify.VerifyNoOtherCalls();
    }

    private RecommendationService CreateService() =>
        new(new HttpClient(), new Mock<ISpotifyApi>().Object, _folder.FullName, TimeProvider.System);
}
