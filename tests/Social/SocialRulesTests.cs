using Cadenza.Core.Models.Social;
using Cadenza.Core.Services.Social;
using Cadenza.Server.Services;

namespace Cadenza.UnitTests.Social;

[TestClass]
public sealed class SocialRulesTests
{
    [TestMethod]
    [DataRow("mochi_42", true)]
    [DataRow("Mochi", false)]
    [DataRow("ab", false)]
    public void UsernamesAreThreeToTwentyLowercaseLettersDigitsOrUnderscores(string username, bool valid) =>
        Assert.AreEqual(valid, SocialRules.IsValidUsername(username), "Usernames are 3–20 lowercase letters, digits or underscores");

    [TestMethod]
    public void BlankMessagesAreRejected() =>
        Assert.IsNotNull(SocialRules.ValidateMessage(new SendMessageRequest(ChatMessageKind.Text, "  ", null)), "Blank messages are rejected");

    [TestMethod]
    public void GiftsNeedASpotifyTrackId() =>
        Assert.IsNotNull(SocialRules.ValidateMessage(new SendMessageRequest(ChatMessageKind.Gift, "", "not-an-id")), "Gifts need a Spotify track ID");

    [TestMethod]
    public void LongMessagesAreRejected()
    {
        var request = new SendMessageRequest(ChatMessageKind.Text, new string('a', SocialRules.MaxMessageLength + 1), null);

        Assert.IsNotNull(SocialRules.ValidateMessage(request), "Long messages are rejected");
    }

    [TestMethod]
    public void TokensAreRandomAndHashed()
    {
        Assert.AreNotEqual(TokenHasher.Hash("a"), TokenHasher.Hash("b"), "Tokens are hashed");
        Assert.AreNotEqual(TokenHasher.NewToken(), TokenHasher.NewToken(), "Tokens are random");
    }
}
