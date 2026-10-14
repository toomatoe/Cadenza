using System.Runtime.InteropServices;
using Cadenza.Core.Services.Playback;

namespace Cadenza.UnitTests.Playback;

/// <summary>
/// Drives the compiled Rust audio engine through its C ABI. build.ps1 copies cadenza_audio.dll next to the
/// test assembly; without it (for example on a machine without Rust) these tests are inconclusive.
/// </summary>
[TestClass]
[TestCategory("Native")]
public sealed class NativeBridgeTests
{
    private const string Library = "cadenza_audio";
    private const uint AbiVersion = 3;

    [TestInitialize]
    public void RequireNativeEngine()
    {
        if (NativeLibrary.TryLoad(Library, typeof(NativeEngine).Assembly, null, out var handle))
        {
            NativeLibrary.Free(handle);
            return;
        }

        // A DLL that is present but fails to load is a real failure, so only a missing file is inconclusive.
        if (!File.Exists(Path.Combine(AppContext.BaseDirectory, Library + ".dll")))
            Assert.Inconclusive($"{Library}.dll is not available. Build it with build.ps1 to run the native tests.");
    }

    [TestMethod]
    public async Task DiagnosticRoundTripReportsTheSupportedAbi()
    {
        await using var engine = new NativeEngine();

        var abi = await engine.CheckBridgeAsync(CancellationToken.None);

        Assert.AreEqual(AbiVersion, abi, "C#/Rust diagnostic round trip (ABI 3)");
    }

    [TestMethod]
    public async Task UnavailablePlaybackIsNotReportedAsSuccess()
    {
        await using var engine = new NativeEngine();

        await Assert.ThrowsAsync<NotSupportedException>(() => engine.RequestPlaybackAsync(CancellationToken.None), "Unavailable playback reported success");
    }

    [TestMethod]
    public async Task PlaybackWithoutASessionIsRefused()
    {
        await using var engine = new NativeEngine();

        await Assert.ThrowsAsync<InvalidOperationException>(() => engine.ResumeAsync(CancellationToken.None), "Playback without a session accepted");
    }

    [TestMethod]
    public async Task MalformedTrackIsRefused()
    {
        await using var engine = new NativeEngine();

        await Assert.ThrowsAsync<ArgumentException>(() => engine.LoadTrackAsync("invalid", CancellationToken.None), "Malformed track accepted");
    }

    [TestMethod]
    public async Task UnboundedVolumeIsRefused()
    {
        await using var engine = new NativeEngine();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => engine.SetVolumeAsync(101, CancellationToken.None), "Unbounded volume accepted");
    }

    [TestMethod]
    public async Task RelativeMusicPathIsRefused()
    {
        await using var engine = new NativeEngine();

        await Assert.ThrowsAsync<ArgumentException>(() => engine.LoadFileAsync("relative.mp3", CancellationToken.None), "Relative music path accepted");
    }

    [TestMethod]
    public async Task MissingMusicFileIsRefused()
    {
        await using var engine = new NativeEngine();
        var missing = Path.Combine(Path.GetTempPath(), "cadenza-missing-song.mp3");

        await Assert.ThrowsAsync<InvalidOperationException>(() => engine.LoadFileAsync(missing, CancellationToken.None), "Missing music file accepted");
    }

    [TestMethod]
    public async Task NonAudioFileIsRefused()
    {
        await using var engine = new NativeEngine();
        var notAudio = Path.Combine(Path.GetTempPath(), $"cadenza-not-audio-{Guid.NewGuid():N}.mp3");
        await File.WriteAllTextAsync(notAudio, "not audio");

        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => engine.LoadFileAsync(notAudio, CancellationToken.None), "Non-audio file accepted");
        }
        finally
        {
            File.Delete(notAudio);
        }
    }

    [TestMethod]
    public async Task CanceledRequestIsRefused()
    {
        await using var engine = new NativeEngine();
        using var canceled = new CancellationTokenSource();
        await canceled.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() => engine.CheckBridgeAsync(canceled.Token), "Canceled request accepted");
    }

    [TestMethod]
    public async Task ConcurrentNativeCompletionsAllSucceed()
    {
        await using var engine = new NativeEngine();

        var results = await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => engine.CheckBridgeAsync(CancellationToken.None)));

        Assert.IsTrue(results.All(abi => abi == AbiVersion), "Concurrent native completions");
    }

    [TestMethod]
    public async Task DisposedEngineRefusesCommands()
    {
        var engine = new NativeEngine();

        await engine.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => engine.CheckBridgeAsync(CancellationToken.None), "Disposed engine accepted command");
    }
}
