using System.Net;
using System.Text;
using Cadenza.Core.Services.Playback;
using Cadenza.Tests.Infrastructure;
using static Cadenza.Tests.Fixtures.TestData;

namespace Cadenza.Tests.Suites;

internal static class PlaybackErrorChecks
{
    public static void Run(CheckRunner checks)
    {
        checks.Check(!PlaybackErrors.RequiresReconnect(9) && !PlaybackErrors.RequiresReconnect(10), "Unavailable track and audio-key refusal preserve a valid session");
        checks.Check(PlaybackErrors.RequiresReconnect(6) && PlaybackErrors.RequiresReconnect(8), "Disconnected session and failed audio device are recreated");
        checks.Check(PlaybackErrors.Describe(10).Contains("audio key") && !PlaybackErrors.Describe(10).Contains("Reconnect"), "Audio-key rejection does not suggest ineffective OAuth reconnect");
        checks.Check(PlaybackErrors.Describe(11) != PlaybackErrors.Describe(13), "Metadata and decoder failures have distinct messages");
    }
}
