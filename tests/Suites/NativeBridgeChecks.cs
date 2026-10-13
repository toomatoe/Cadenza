using Cadenza.Core.Services.Playback;
using Cadenza.Tests.Infrastructure;

namespace Cadenza.Tests.Suites;

internal static class NativeBridgeChecks
{
    public static async Task RunAsync(CheckRunner checks)
    {

        await using var engine = new NativeEngine();
        checks.Check(await engine.CheckBridgeAsync(CancellationToken.None) == 3, "C#/Rust diagnostic round trip (ABI 3)");
        await checks.ThrowsAsync<NotSupportedException>(() => engine.RequestPlaybackAsync(CancellationToken.None), "Unavailable playback reported success");
        await checks.ThrowsAsync<InvalidOperationException>(() => engine.ResumeAsync(CancellationToken.None), "Playback without a session accepted");
        await checks.ThrowsAsync<ArgumentException>(() => engine.LoadTrackAsync("invalid", CancellationToken.None), "Malformed track accepted");
        await checks.ThrowsAsync<ArgumentOutOfRangeException>(() => engine.SetVolumeAsync(101, CancellationToken.None), "Unbounded volume accepted");
        await checks.ThrowsAsync<ArgumentException>(() => engine.LoadFileAsync("relative.mp3", CancellationToken.None), "Relative music path accepted");
        await checks.ThrowsAsync<InvalidOperationException>(
            () => engine.LoadFileAsync(Path.Combine(Path.GetTempPath(), "cadenza-missing-song.mp3"), CancellationToken.None), "Missing music file accepted");
        var notAudio = Path.Combine(Path.GetTempPath(), "cadenza-not-audio.mp3");
        await File.WriteAllTextAsync(notAudio, "not audio");
        await checks.ThrowsAsync<InvalidOperationException>(() => engine.LoadFileAsync(notAudio, CancellationToken.None), "Non-audio file accepted");
        File.Delete(notAudio);
        using (var canceled = new CancellationTokenSource())
        {
            canceled.Cancel();
            await checks.ThrowsAsync<OperationCanceledException>(() => engine.CheckBridgeAsync(canceled.Token), "Canceled request accepted");
        }
        var tasks = Enumerable.Range(0, 16).Select(_ => engine.CheckBridgeAsync(CancellationToken.None)).ToArray();
        checks.Check((await Task.WhenAll(tasks)).All(v => v == 3), "Concurrent native completions");
        await engine.DisposeAsync();
        await checks.ThrowsAsync<ObjectDisposedException>(() => engine.CheckBridgeAsync(CancellationToken.None), "Disposed engine accepted command");
    }
}
