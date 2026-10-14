using Cadenza.Core.Services.Playback;

namespace Cadenza.UnitTests.Playback;

[TestClass]
public sealed class PlaybackErrorTests
{
    [TestMethod]
    public void UnavailableTrackAndAudioKeyRefusalPreserveAValidSession()
    {
        Assert.IsFalse(PlaybackErrors.RequiresReconnect(9), "Unavailable track preserves a valid session");
        Assert.IsFalse(PlaybackErrors.RequiresReconnect(10), "Audio-key refusal preserves a valid session");
    }

    [TestMethod]
    public void DisconnectedSessionAndFailedAudioDeviceAreRecreated()
    {
        Assert.IsTrue(PlaybackErrors.RequiresReconnect(6), "Disconnected session is recreated");
        Assert.IsTrue(PlaybackErrors.RequiresReconnect(8), "Failed audio device is recreated");
    }

    [TestMethod]
    public void AudioKeyRejectionDoesNotSuggestIneffectiveOAuthReconnect()
    {
        var message = PlaybackErrors.Describe(10);

        Assert.Contains("audio key", message, StringComparison.Ordinal);
        Assert.DoesNotContain("Reconnect", message, StringComparison.Ordinal);
    }

    [TestMethod]
    public void MetadataAndDecoderFailuresHaveDistinctMessages() =>
        Assert.AreNotEqual(PlaybackErrors.Describe(11), PlaybackErrors.Describe(13), "Metadata and decoder failures have distinct messages");
}
