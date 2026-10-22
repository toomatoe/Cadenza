using System.Windows.Automation;
using Cadenza.UI.Tests.Infrastructure;

namespace Cadenza.UI.Tests.Pages;

/// <summary>Checks the Friends page renders either the join card or the joined view. Never joins or sends anything.</summary>
[TestClass]
public sealed class FriendsTests : UiTestBase
{
    [TestInitialize]
    public void OpenFriends() => App.Navigate("Friends", "Friends");

    [TestMethod]
    public void FriendsShowsTheJoinCardOrTheFriendsList() =>
        App.WaitUntil(() =>
        {
            var names = App.Names();
            return names.Contains("Join Cadenza Friends") || names.Contains("Create an invite code");
        }, "the join card or the friends list");

    [TestMethod]
    public void ChatComposerHasShareAndQueueGiftButtons()
    {
        if (!App.Names().Contains("Create an invite code"))
            Assert.Inconclusive("Not joined to Cadenza Friends on this machine.");

        Assert.IsNotNull(App.ByName("Share what's playing", ControlType.Button));
        Assert.IsNotNull(App.ByName("Send what's playing to their queue", ControlType.Button));
    }
}
