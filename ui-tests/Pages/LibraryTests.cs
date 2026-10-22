using System.Windows.Automation;
using Cadenza.UI.Tests.Infrastructure;

namespace Cadenza.UI.Tests.Pages;

[TestClass]
public sealed class LibraryTests : UiTestBase
{
    [TestInitialize]
    public void OpenLibrary() => App.Navigate("Library", "Your library");

    [TestMethod]
    public void LibraryShowsRecentlyAddedTracks()
    {
        RequireSignIn();

        App.WaitUntil(() => App.Names().Contains("Recently added"), "the Recently added section");
        App.WaitUntil(() => App.Names().Any(name => name == "Play track in Cadenza"), "track cards with Play buttons");
    }

    [TestMethod]
    public void LibraryOffersAddingSongsToTheSharedLibrary() =>
        Assert.IsNotNull(App.ByName("Add songs to the shared library", ControlType.Button));
}
