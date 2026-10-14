using Cadenza.Core.Exceptions;
using Cadenza.Core.Interfaces;
using Cadenza.Core.Models;
using Cadenza.Core.Models.Social;
using Cadenza.Core.Services.Social;
using Cadenza.UnitTests.Fixtures;
using Moq;

namespace Cadenza.UnitTests.Social;

[TestClass]
public sealed class SharedLibraryServiceTests
{
    private DirectoryInfo _folder = null!;

    [TestInitialize]
    public void CreateFolder() => _folder = Directory.CreateTempSubdirectory("cadenza-shared-library-");

    [TestCleanup]
    public void DeleteFolder() => _folder.Delete(recursive: true);

    [TestMethod]
    public async Task UploadsMusicAndSkipsFilesThatArentMusic()
    {
        var song = Write("Neon Owls - Night Drive.mp3", MusicSamples.Mp3("Night Drive", "Neon Owls"));
        var notes = Write("notes.mp3", "just text"u8.ToArray());
        var client = new Mock<ISocialClient>();
        client.Setup(c => c.UploadMusicAsync(song, It.Is<UploadMetadata>(m => m.Title == "Night Drive"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LibraryTrack("abc", "Night Drive", "Neon Owls", "", 26_122, "mp3", "owner"));
        var progress = new List<string>();

        var summary = await CreateService().UploadAsync(client.Object, [song, notes], new SyncProgress(progress), CancellationToken.None);

        Assert.AreEqual(1, summary.Added);
        Assert.HasCount(1, summary.Skipped);
        StringAssert.Contains(summary.Skipped[0], "notes.mp3");
        Assert.HasCount(2, progress);
    }

    [TestMethod]
    public async Task ServerRefusalsSkipTheFileButSignOutStopsTheUpload()
    {
        var first = Write("a.mp3", MusicSamples.Mp3("A", "Band"));
        var second = Write("b.mp3", MusicSamples.Mp3("B", "Band"));
        var client = new Mock<ISocialClient>();
        client.Setup(c => c.UploadMusicAsync(first, It.IsAny<UploadMetadata>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new SocialException(413, "Too big"));
        client.Setup(c => c.UploadMusicAsync(second, It.IsAny<UploadMetadata>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new SocialException(401, "Signed out"));

        await Assert.ThrowsExactlyAsync<SocialException>(() => CreateService().UploadAsync(client.Object, [first, second], null, CancellationToken.None));
    }

    [TestMethod]
    public async Task SearchResultsRememberTheirFormatForPlayback()
    {
        var client = new Mock<ISocialClient>();
        client.Setup(c => c.SearchMusicAsync("glass", It.IsAny<CancellationToken>()))
            .ReturnsAsync([new LibraryTrack("flac1", "Glass", "Ice Choir", "", 10_000, "flac", "owner")]);
        client.Setup(c => c.DownloadMusicAsync("flac1", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, CancellationToken>((_, destination, _) => File.WriteAllBytes(destination, [1, 2, 3]))
            .Returns(Task.CompletedTask);
        var service = CreateService();

        var found = await service.SearchAsync(client.Object, "glass", CancellationToken.None);
        var path = await service.GetPlayableFileAsync(client.Object, found[0], CancellationToken.None);

        Assert.AreEqual(MusicSource.Library, found[0].Source);
        Assert.EndsWith(".flac", path);
    }

    private SharedLibraryService CreateService() => new(Path.Combine(_folder.FullName, "cache"));

    private string Write(string name, byte[] bytes)
    {
        var path = Path.Combine(_folder.FullName, name);
        File.WriteAllBytes(path, bytes);

        return path;
    }

    /// <summary>Reports synchronously (<see cref="Progress{T}"/> posts to a context, which tests don't have).</summary>
    private sealed class SyncProgress : IProgress<string>
    {
        private readonly List<string> _items;

        public SyncProgress(List<string> items)
        {
            _items = items;
        }

        public void Report(string value) => _items.Add(value);
    }
}
