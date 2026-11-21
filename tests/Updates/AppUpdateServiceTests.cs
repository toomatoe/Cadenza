using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Cadenza.Core.Exceptions;
using Cadenza.Core.Models;
using Cadenza.Core.Services.Updates;
using Cadenza.UnitTests.Fakes;

namespace Cadenza.UnitTests.Updates;

[TestClass]
public sealed class AppUpdateServiceTests : IDisposable
{
    private const string InstallerName = "Cadenza-Setup.exe";
    private const string Tag = "v0.2.0";
    private const string Prefix = "https://github.com/toomatoe/Cadenza/releases/download/";
    private readonly FakeHandler _handler = new();
    private readonly HttpClient _http;
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "cadenza-update-test-" + Guid.NewGuid());

    public AppUpdateServiceTests() => _http = new HttpClient(_handler);

    public void Dispose()
    {
        _http.Dispose();
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    [TestMethod]
    public async Task ChoosesHighestEligibleVersionRatherThanApiOrder()
    {
        Releases("v0.2.0", "v0.10.0", "v0.3.0-beta.2");

        var update = await new AppUpdateService(_http).CheckAsync("0.1.0", false);

        Assert.AreEqual("v0.10.0", update?.Version);
    }

    [TestMethod]
    public async Task PreviewVersionsUseNumericIdentifiersAndStableWins()
    {
        Releases("v0.3.0-beta.2", "v0.3.0-beta.10");
        var service = new AppUpdateService(_http);

        var update = await service.CheckAsync("0.3.0-beta.9", true);

        Assert.AreEqual("v0.3.0-beta.10", update?.Version);
        Releases("v0.3.0", "v0.3.0-beta.10");
        Assert.AreEqual("v0.3.0", (await service.CheckAsync("0.3.0-beta.10", true))?.Version);
    }

    [TestMethod]
    public async Task DoesNotOfferDowngradesOrTheInstalledVersion()
    {
        Releases("v0.1.0", "v0.2.0");

        Assert.IsNull(await new AppUpdateService(_http).CheckAsync("0.2.0+commit", false));
    }

    [TestMethod]
    public async Task VerifiedDownloadReplacesInstallerAndRemovesPartial()
    {
        var bytes = Encoding.UTF8.GetBytes("test installer bytes");
        DownloadResponses(bytes, Convert.ToHexString(SHA256.HashData(bytes)));

        var path = await new AppUpdateService(_http).DownloadAsync(Update(bytes.Length), _directory);

        CollectionAssert.AreEqual(bytes, await File.ReadAllBytesAsync(path));
        Assert.IsFalse(File.Exists(path + ".part"));
    }

    [TestMethod]
    public async Task BadChecksumCannotReplaceExistingInstaller()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, InstallerName);
        await File.WriteAllTextAsync(path, "previous verified installer");
        var bytes = Encoding.UTF8.GetBytes("corrupt installer");
        DownloadResponses(bytes, new string('0', 64));

        await Assert.ThrowsExactlyAsync<AppUpdateException>(() => new AppUpdateService(_http).DownloadAsync(Update(bytes.Length), _directory));

        Assert.AreEqual("previous verified installer", await File.ReadAllTextAsync(path));
        Assert.IsFalse(File.Exists(path + ".part"));
    }

    [TestMethod]
    public async Task RejectsUnexpectedDownloadHostBeforeAnyRequest()
    {
        var update = new AppUpdate(Tag, new Uri("https://example.com/setup.exe"), new Uri(Prefix + Tag + "/" + InstallerName + ".sha256"), 10);

        await Assert.ThrowsExactlyAsync<AppUpdateException>(() => new AppUpdateService(_http).DownloadAsync(update, _directory));

        Assert.HasCount(0, _handler.Requests);
    }

    [TestMethod]
    public async Task CanceledDownloadDoesNotLeaveAnInstaller()
    {
        using var canceled = new CancellationTokenSource();
        await canceled.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() => new AppUpdateService(_http).DownloadAsync(Update(10), _directory, canceled.Token));

        Assert.IsFalse(Directory.Exists(_directory));
    }

    private void Releases(params string[] tags)
    {
        var releases = tags.Select(tag => new
        {
            tag_name = tag,
            draft = false,
            prerelease = tag.Contains('-', StringComparison.Ordinal),
            assets = new[]
            {
                new { name = InstallerName, size = 100, browser_download_url = Prefix + tag + "/" + InstallerName },
                new { name = InstallerName + ".sha256", size = 84, browser_download_url = Prefix + tag + "/" + InstallerName + ".sha256" }
            }
        });
        _handler.Responses.Enqueue(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(releases)) });
    }

    private void DownloadResponses(byte[] bytes, string checksum)
    {
        _handler.Responses.Enqueue(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(checksum + "  " + InstallerName) });
        _handler.Responses.Enqueue(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
    }

    private static AppUpdate Update(int size) => new(Tag, new Uri(Prefix + Tag + "/" + InstallerName), new Uri(Prefix + Tag + "/" + InstallerName + ".sha256"), size);
}
