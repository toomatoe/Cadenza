using Cadenza.Core.Controllers;
using Cadenza.Core.Models;
using static Cadenza.UnitTests.Fixtures.TestData;

namespace Cadenza.UnitTests.Controllers;

[TestClass]
public sealed class QueuePlaybackControllerTests
{
    private const string SameId = "same";

    private static readonly Track s_duplicate = Song(SameId, "artist");

    [TestMethod]
    public async Task LoadingConsumesItsOccurrenceAfterReorderAndInsertionPreservingDuplicates()
    {
        var queue = new ListeningQueue();

        var (played, consumedId) = await PlayWhileEditingAsync(queue);

        Assert.IsTrue(played, "Queue playback completes");
        Assert.IsTrue(
            queue.Entries.All(e => e.Id != consumedId) && queue.Tracks[0].Id == "inserted" && queue.Tracks.Count(t => t.Id == SameId) == 1,
            "Loading consumes its occurrence after reorder and insertion, preserving duplicates");
    }

    [TestMethod]
    public async Task UndoNeverRestoresTheConsumedOccurrence()
    {
        var queue = new ListeningQueue();
        var (_, consumedId) = await PlayWhileEditingAsync(queue);

        while (queue.CanUndo)
        {
            queue.Undo();
            Assert.IsTrue(queue.Entries.All(e => e.Id != consumedId), "Undo never restores consumed occurrence");
        }
    }

    [TestMethod]
    public async Task ClearingDuringLoadingNeverConsumesTheNewlyAddedTrack()
    {
        var queue = new ListeningQueue();

        await ClearWhileLoadingAsync(queue);

        Assert.HasCount(1, queue.Tracks, "Clearing during loading never consumes newly added track");
        Assert.AreEqual("new", queue.Tracks[0].Id, "Clearing during loading never consumes newly added track");
    }

    [TestMethod]
    public async Task UndoAfterClearExcludesTheConsumedOccurrence()
    {
        var queue = new ListeningQueue();
        await ClearWhileLoadingAsync(queue);

        queue.Undo();
        queue.Undo();

        Assert.HasCount(1, queue.Tracks, "Undo after clear excludes consumed occurrence");
        Assert.AreEqual("waiting", queue.Tracks[0].Id, "Undo after clear excludes consumed occurrence");
    }

    [TestMethod]
    public async Task FailedLoadReportsTheErrorAndPreservesTheQueue()
    {
        var queue = new ListeningQueue();
        var controller = new QueuePlaybackController(queue);
        queue.Add(s_duplicate);
        var pendingId = queue.Entries[0].Id;

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => controller.PlayAsync(0, (_, _) => Task.FromException(new InvalidOperationException("load failed")), CancellationToken.None),
            "Failed loads report error");

        Assert.AreEqual(pendingId, queue.Entries[0].Id, "Failed load preserves queue");
    }

    [TestMethod]
    public async Task CanceledLoadReportsCancellationAndPreservesTheQueue()
    {
        var queue = new ListeningQueue();
        var controller = new QueuePlaybackController(queue);
        queue.Add(s_duplicate);
        var pendingId = queue.Entries[0].Id;
        using var canceled = new CancellationTokenSource();
        await canceled.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => controller.PlayAsync(0, (_, _) => Task.CompletedTask, canceled.Token),
            "Canceled load reports cancellation");

        Assert.AreEqual(pendingId, queue.Entries[0].Id, "Canceled load preserves queue");
    }

    [TestMethod]
    public async Task InvalidSelectionDoesNotLoad()
    {
        var queue = new ListeningQueue();
        var controller = new QueuePlaybackController(queue);
        queue.Add(s_duplicate);

        var played = await controller.PlayAsync(-1, (_, _) => throw new InvalidOperationException("must not load"), CancellationToken.None);

        Assert.IsFalse(played, "Invalid selection does not load");
    }

    /// <summary>Starts playing the first of two duplicates, then reorders and inserts while the track is still loading.</summary>
    private static async Task<(bool Played, Guid ConsumedId)> PlayWhileEditingAsync(ListeningQueue queue)
    {
        queue.AddRange([s_duplicate, s_duplicate, Song("last", "b")]);
        var controller = new QueuePlaybackController(queue);
        var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var consumedId = queue.Entries[0].Id;
        var play = controller.PlayAsync(
            0,
            async (_, _) =>
            {
                started.SetResult();
                await loaded.Task;
            },
            CancellationToken.None);
        await started.Task;
        queue.Move(0, 2);
        queue.PlayNext(Song("inserted", "c"));
        loaded.SetResult();
        var played = await play;

        return (played, consumedId);
    }

    /// <summary>Starts playing the first track, then clears the queue and adds a new track while it is still loading.</summary>
    private static async Task ClearWhileLoadingAsync(ListeningQueue queue)
    {
        queue.AddRange([s_duplicate, Song("waiting", "b")]);
        var controller = new QueuePlaybackController(queue);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var inFlight = controller.PlayAsync(0, (_, _) => release.Task, CancellationToken.None);
        queue.Clear();
        queue.Add(Song("new", "c"));
        release.SetResult();
        await inFlight;
    }
}
