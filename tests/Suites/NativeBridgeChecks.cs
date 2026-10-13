using Cadenza.Core.Services.Playback;
using Cadenza.Tests.Infrastructure;

namespace Cadenza.Tests.Suites;

internal static class NativeBridgeChecks
{
    public static async Task RunAsync(CheckRunner checks)
    {

        await using var engine = new NativeEngine();
        checks.Check(await engine.CheckBridgeAsync(CancellationToken.None) == 2, "C#/Rust diagnostic round trip");
        await checks.ThrowsAsync<NotSupportedException>(() => engine.RequestPlaybackAsync(CancellationToken.None), "Unavailable playback reported success");
        await checks.ThrowsAsync<InvalidOperationException>(() => engine.ResumeAsync(CancellationToken.None), "Playback without a session accepted");
        await checks.ThrowsAsync<ArgumentException>(() => engine.LoadTrackAsync("invalid", CancellationToken.None), "Malformed track accepted");
        await checks.ThrowsAsync<ArgumentOutOfRangeException>(() => engine.SetVolumeAsync(101, CancellationToken.None), "Unbounded volume accepted");
        using (var canceled = new CancellationTokenSource())
        {
            canceled.Cancel();
            await checks.ThrowsAsync<OperationCanceledException>(() => engine.CheckBridgeAsync(canceled.Token), "Canceled request accepted");
        }
        var tasks = Enumerable.Range(0, 16).Select(_ => engine.CheckBridgeAsync(CancellationToken.None)).ToArray();
        checks.Check((await Task.WhenAll(tasks)).All(v => v == 2), "Concurrent native completions");
        await engine.DisposeAsync();
        await checks.ThrowsAsync<ObjectDisposedException>(() => engine.CheckBridgeAsync(CancellationToken.None), "Disposed engine accepted command");
    }
}
