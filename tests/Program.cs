using Cadenza.Tests.Infrastructure;
using Cadenza.Tests.Suites;

namespace Cadenza.Tests;

internal static class Program
{
    private static async Task Main(string[] args)
    {
        var checks = new CheckRunner();
        PkceChecks.Run(checks);
        QueueChecks.Run(checks);
        await QueueChecks.RunPlaybackAsync(checks);
        PlaybackErrorChecks.Run(checks);
        await SpotifyApiChecks.RunAsync(checks);
        await CatalogControllerChecks.RunAsync(checks);
        if (args.Contains("--native"))
            await NativeBridgeChecks.RunAsync(checks);
        Console.WriteLine($"PASS: {checks.Passed} checks");
    }
}
