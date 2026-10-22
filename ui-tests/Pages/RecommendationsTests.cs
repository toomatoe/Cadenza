using System.Windows.Automation;
using Cadenza.UI.Tests.Infrastructure;

namespace Cadenza.UI.Tests.Pages;

[TestClass]
public sealed class RecommendationsTests : UiTestBase
{
    private static readonly string[] _shelves = ["More like your favourites", "Deeper cuts from artists you love", "Something new"];

    [TestInitialize]
    public void OpenForYou() => App.Navigate("For you", "For you");

    [TestMethod]
    public void ForYouHasTheDiscoverySliderAndRefresh()
    {
        Assert.IsNotNull(App.ByName("Discovery: familiar to new", ControlType.Slider));
        Assert.IsNotNull(App.ByName("Refresh suggestions", ControlType.Button));
    }

    [TestMethod]
    public void ForYouShowsSuggestionShelves()
    {
        RequireSignIn();

        // The first run can take a while (Spotify searches); later runs show saved suggestions instantly.
        App.WaitUntil(() => App.Names().Intersect(_shelves).Any(), "at least one suggestion shelf");
        Assert.IsTrue(App.Names().Any(name => name == "Not interested"), "Suggestion cards have a Not interested button.");
    }
}
