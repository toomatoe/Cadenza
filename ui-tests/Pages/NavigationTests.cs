using Cadenza.UI.Tests.Infrastructure;

namespace Cadenza.UI.Tests.Pages;

[TestClass]
public sealed class NavigationTests : UiTestBase
{
    [TestMethod]
    public void AppStartsOnTheLibraryPage() => Assert.AreEqual("Your library", App.PageTitle);

    [TestMethod]
    [DataRow("For you", "For you")]
    [DataRow("Search", "Search")]
    [DataRow("Playlists", "Playlists")]
    [DataRow("Queue", "Listening queue")]
    [DataRow("Friends", "Friends")]
    [DataRow("Settings", "Settings")]
    [DataRow("Library", "Your library")]
    public void SidebarOpensEveryPage(string navigation, string title)
    {
        App.Navigate(navigation, title);

        Assert.AreEqual(title, App.PageTitle);
    }
}
