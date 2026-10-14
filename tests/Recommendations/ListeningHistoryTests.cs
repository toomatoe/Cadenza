using Cadenza.Core.Recommendations;
using static Cadenza.UnitTests.Fixtures.RecommendationData;

namespace Cadenza.UnitTests.Recommendations;

[TestClass]
public sealed class ListeningHistoryTests
{
    [TestMethod]
    public void ListeningHistoryIsBounded()
    {
        var store = new ListeningHistoryStore(path: null);

        RecordMoreThanTheLimit(store);

        Assert.HasCount(ListeningHistoryStore.MaxEvents, store.Events, "Listening history is bounded");
    }

    [TestMethod]
    public void OnlyTheLastFewRefreshesAreRemembered()
    {
        var store = new ListeningHistoryStore(path: null);

        RememberOneRefreshTooMany(store);

        Assert.DoesNotContain("r0", store.RecentlySuggested, "Only the last few refreshes are remembered");
        Assert.Contains("r3", store.RecentlySuggested, "Only the last few refreshes are remembered");
    }

    [TestMethod]
    public void HistoryCanBeCleared()
    {
        var store = new ListeningHistoryStore(path: null);
        RecordMoreThanTheLimit(store);
        RememberOneRefreshTooMany(store);

        store.Clear();

        Assert.IsEmpty(store.Events, "History can be cleared");
        Assert.IsEmpty(store.RecentlySuggested, "History can be cleared");
    }

    private static void RecordMoreThanTheLimit(ListeningHistoryStore store)
    {
        for (var i = 0; i < ListeningHistoryStore.MaxEvents + 5; i++)
            store.Record(new ListeningEvent(ListeningEventKind.Completed, $"t{i}", "Alpha", Now));
    }

    private static void RememberOneRefreshTooMany(ListeningHistoryStore store)
    {
        for (var refresh = 0; refresh < ListeningHistoryStore.RememberedRefreshes + 1; refresh++)
            store.RememberSuggestions([$"r{refresh}"]);
    }
}
