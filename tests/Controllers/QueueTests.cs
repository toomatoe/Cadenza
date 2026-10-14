using Cadenza.Core.Models;
using static Cadenza.UnitTests.Fixtures.TestData;

namespace Cadenza.UnitTests.Controllers;

[TestClass]
public sealed class QueueTests
{
    private static readonly string[] s_shuffledIds = ["1", "2", "3", "4"];

    [TestMethod]
    public void MoveReordersTheQueue()
    {
        var queue = FourSongs();

        queue.Move(0, 3);

        Assert.AreEqual("1", queue.Tracks[^1].Id, "Queue move");
    }

    [TestMethod]
    public void UndoRevertsTheLastEdit()
    {
        var queue = FourSongs();
        queue.Move(0, 3);

        queue.Undo();

        Assert.AreEqual("1", queue.Tracks[0].Id, "Queue undo");
    }

    [TestMethod]
    public void ShuffleRetainsAllTracks()
    {
        var queue = FourSongs();

        queue.Shuffle(1, new Random(4));

        Assert.IsTrue(queue.Tracks.Select(t => t.Id).Order().SequenceEqual(s_shuffledIds), "Shuffle retains all tracks");
    }

    [TestMethod]
    public void ShuffleSpacesOutArtistsWhenPossible()
    {
        var queue = FourSongs();

        queue.Shuffle(1, new Random(4));

        Assert.IsTrue(
            queue.Tracks.Zip(queue.Tracks.Skip(1)).All(pair => pair.First.ArtistId != pair.Second.ArtistId),
            "Artist spacing when possible");
    }

    [TestMethod]
    public void DisconnectClearsQueueHistory()
    {
        var queue = FourSongs();
        queue.Shuffle(1, new Random(4));

        queue.Forget();

        Assert.IsFalse(queue.CanUndo, "Disconnect clears queue history");
        Assert.IsEmpty(queue.Tracks, "Disconnect clears queue history");
    }

    [TestMethod]
    public void QueueLimitIsEnforced()
    {
        var queue = new ListeningQueue();
        for (var i = 0; i < ListeningQueue.Capacity; i++)
            queue.Add(Song(i, "a"));

        Assert.Throws<InvalidOperationException>(() => queue.Add(Song("overflow", "a")), "Queue limit not enforced");
    }

    [TestMethod]
    public void DuplicateOccurrencesHaveDistinctIdentities()
    {
        var queue = new ListeningQueue();
        var duplicate = Song("same", "artist");

        queue.AddRange([duplicate, duplicate, Song("last", "b")]);

        Assert.AreNotEqual(queue.Entries[0].Id, queue.Entries[1].Id, "Duplicate occurrences have distinct identities");
    }

    [TestMethod]
    public void NoOpEditsDoNotCreateUndoHistory()
    {
        var queue = new ListeningQueue();

        queue.Move(-1, 0);
        queue.RemoveAt(0);
        queue.Clear();
        queue.AddRange([]);

        Assert.IsFalse(queue.CanUndo, "No-op edits do not create undo history");
    }

    [TestMethod]
    public void OversizedBatchIsRejectedAndAddsNothing()
    {
        var queue = new ListeningQueue();
        var duplicate = Song("same", "artist");
        queue.AddRange(Enumerable.Range(0, ListeningQueue.Capacity - 1).Select(i => Song(i, "a")));

        Assert.Throws<InvalidOperationException>(() => queue.AddRange([duplicate, duplicate]), "Oversized batch rejected");
        Assert.HasCount(ListeningQueue.Capacity - 1, queue.Entries, "Oversized batch adds nothing");
    }

    [TestMethod]
    public void BulkAddIsASingleUndoOperationEvenAfterARejectedBatch()
    {
        var queue = new ListeningQueue();
        var duplicate = Song("same", "artist");
        queue.AddRange(Enumerable.Range(0, ListeningQueue.Capacity - 1).Select(i => Song(i, "a")));
        Assert.Throws<InvalidOperationException>(() => queue.AddRange([duplicate, duplicate]));

        queue.Undo();

        Assert.IsEmpty(queue.Entries, "Bulk add is a single undo operation");
    }

    [TestMethod]
    public void SummaryCountsDuplicateDurations()
    {
        var queue = new ListeningQueue();
        var duplicate = Song("same", "artist");

        queue.AddRange([duplicate, duplicate]);

        Assert.AreEqual(2L * duplicate.DurationMs, queue.DurationMs, "Summary counts duplicate durations");
    }

    [TestMethod]
    public void ClearIsUndoable()
    {
        var queue = new ListeningQueue();
        var duplicate = Song("same", "artist");
        queue.AddRange([duplicate, duplicate]);

        queue.Clear();
        queue.Undo();

        Assert.HasCount(2, queue.Entries, "Clear is undoable");
    }

    [TestMethod]
    public void PlayNextEnforcesCapacity()
    {
        var queue = new ListeningQueue();
        queue.AddRange(Enumerable.Range(0, ListeningQueue.Capacity).Select(i => Song(i, "a")));

        Assert.Throws<InvalidOperationException>(() => queue.PlayNext(Song("same", "artist")), "Play next enforces capacity");
    }

    [TestMethod]
    public void ForgetClearsEntriesUndoAndDuration()
    {
        var queue = new ListeningQueue();
        queue.AddRange(Enumerable.Range(0, ListeningQueue.Capacity).Select(i => Song(i, "a")));

        queue.Forget();

        Assert.IsFalse(queue.CanUndo, "Forget clears undo");
        Assert.AreEqual(0L, queue.DurationMs, "Forget clears duration");
    }

    private static ListeningQueue FourSongs()
    {
        var queue = new ListeningQueue();
        queue.Add(Song("1", "a"));
        queue.Add(Song("2", "a"));
        queue.Add(Song("3", "b"));
        queue.Add(Song("4", "b"));

        return queue;
    }
}
