using System.Windows.Automation;
using Cadenza.UI.Tests.Infrastructure;

namespace Cadenza.UI.Tests.Pages;

/// <summary>Searches Spotify and adds a result to the queue, then clears the queue again (the queue isn't saved).</summary>
[TestClass]
public sealed class SearchAndQueueTests : UiTestBase
{
    private const string Query = "Radiohead";
    private const string TrackListId = "TrackList";
    private const string QueueBadgeId = "QueueCountBadge";

    [TestMethod]
    public void SearchShowsMatchingTracks()
    {
        RequireSignIn();
        Search();

        var first = App.ListItems(TrackListId)[0];
        Assert.IsTrue(App.Names(first).Any(name => name.Contains(Query, StringComparison.OrdinalIgnoreCase)), $"The first result should mention {Query}.");
    }

    [TestMethod]
    public void AddingASearchResultUpdatesTheQueue()
    {
        RequireSignIn();
        Search();
        var before = QueueCount();

        var add = App.ListItems(TrackListId)[0].FindFirst(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.NameProperty, "Add track to listening queue"));
        Assert.IsNotNull(add, "Each result has an Add to queue button.");
        CadenzaApp.Click(add);
        App.WaitUntil(() => QueueCount() == before + 1, "the queue badge to count the new track");

        App.Navigate("Queue", "Listening queue");
        App.WaitUntil(() => App.ListItems(TrackListId).Count == before + 1, "the track in the queue list");

        CadenzaApp.Click(App.ByName("Clear queue", ControlType.Button));
        App.WaitUntil(() => QueueCount() == 0, "the queue to be cleared");
    }

    private static void Search()
    {
        App.Navigate("Search", "Search");
        App.SetText("SearchBox", Query);
        App.WaitUntil(() => App.ListItems(TrackListId).Count > 0, $"search results for {Query}");
    }

    private static int QueueCount() => int.TryParse(App.ById(QueueBadgeId).Current.Name, out var count) ? count : 0;
}
