using System.Net;
using System.Text;
using Cadenza.Core.Models;
using Cadenza.Core.Controllers;
using Cadenza.Tests.Infrastructure;
using static Cadenza.Tests.Fixtures.TestData;

namespace Cadenza.Tests.Suites;

internal static class QueueChecks
{
    public static void Run(CheckRunner checks)
    {
        var queue = new ListeningQueue();
        queue.Add(Song("1", "a")); queue.Add(Song("2", "a")); queue.Add(Song("3", "b")); queue.Add(Song("4", "b"));
        queue.Move(0, 3); checks.Check(queue.Tracks[^1].Id == "1", "Queue move");
        queue.Undo(); checks.Check(queue.Tracks[0].Id == "1", "Queue undo");
        queue.Shuffle(1, new Random(4));
        checks.Check(queue.Tracks.Select(t => t.Id).Order().SequenceEqual(new[] { "1", "2", "3", "4" }), "Shuffle retains all tracks");
        checks.Check(queue.Tracks.Zip(queue.Tracks.Skip(1)).All(pair => pair.First.ArtistId != pair.Second.ArtistId), "Artist spacing when possible");
        queue.Forget(); checks.Check(!queue.CanUndo && queue.Tracks.Count == 0, "Disconnect clears queue history");
        for (var i = 0; i < ListeningQueue.Capacity; i++) queue.Add(Song(i.ToString(), "a"));
        checks.Throws<InvalidOperationException>(() => queue.Add(Song("overflow", "a")), "Queue limit not enforced");

    }
    public static async Task RunPlaybackAsync(CheckRunner checks)
    {
        var queue = new ListeningQueue();
        var duplicate = Song("same", "artist");
        queue.AddRange([duplicate, duplicate, Song("last", "b")]);
        checks.Check(queue.Entries[0].Id != queue.Entries[1].Id, "Duplicate occurrences have distinct identities");
        var controller = new QueuePlaybackController(queue);
        var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var consumedId = queue.Entries[0].Id;
        var play = controller.PlayAsync(0, async (_, _) => { started.SetResult(); await loaded.Task; }, CancellationToken.None);
        await started.Task;
        queue.Move(0, 2);
        queue.PlayNext(Song("inserted", "c"));
        loaded.SetResult();
        checks.Check(await play, "Queue playback completes");
        checks.Check(queue.Entries.All(e => e.Id != consumedId) && queue.Tracks[0].Id == "inserted" && queue.Tracks.Count(t => t.Id == "same") == 1,
            "Loading consumes its occurrence after reorder and insertion, preserving duplicates");
        while (queue.CanUndo)
        {
            queue.Undo();
            checks.Check(queue.Entries.All(e => e.Id != consumedId), "Undo never restores consumed occurrence");
        }
        queue.Forget(); queue.AddRange([duplicate, Song("waiting", "b")]);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var inFlight = controller.PlayAsync(0, (_, _) => release.Task, CancellationToken.None);
        queue.Clear(); queue.Add(Song("new", "c"));
        release.SetResult(); await inFlight;
        checks.Check(queue.Tracks.Count == 1 && queue.Tracks[0].Id == "new", "Clearing during loading never consumes newly added track");
        queue.Undo(); queue.Undo();
        checks.Check(queue.Tracks.Count == 1 && queue.Tracks[0].Id == "waiting", "Undo after clear excludes consumed occurrence");
        queue.Forget(); queue.Move(-1, 0); queue.RemoveAt(0); queue.Clear(); queue.AddRange([]);
        checks.Check(!queue.CanUndo, "No-op edits do not create undo history");
        queue.Forget(); queue.Add(duplicate);
        var pendingId = queue.Entries[0].Id;
        await checks.ThrowsAsync<InvalidOperationException>(() => controller.PlayAsync(0, (_, _) => Task.FromException(new InvalidOperationException("load failed")), CancellationToken.None), "Failed loads report error");
        checks.Check(queue.Entries[0].Id == pendingId, "Failed load preserves queue");
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        await checks.ThrowsAsync<OperationCanceledException>(() => controller.PlayAsync(0, (_, _) => Task.CompletedTask, canceled.Token), "Canceled load reports cancellation");
        checks.Check(queue.Entries[0].Id == pendingId, "Canceled load preserves queue");
        checks.Check(!await controller.PlayAsync(-1, (_, _) => throw new Exception("must not load"), CancellationToken.None), "Invalid selection does not load");
        queue.Forget();
        queue.AddRange(Enumerable.Range(0, ListeningQueue.Capacity - 1).Select(i => Song(i.ToString(), "a")));
        checks.Throws<InvalidOperationException>(() => queue.AddRange([duplicate, duplicate]), "Oversized batch rejected");
        checks.Check(queue.Entries.Count == ListeningQueue.Capacity - 1, "Oversized batch adds nothing");
        queue.Undo(); checks.Check(queue.Entries.Count == 0, "Bulk add is a single undo operation");
        queue.AddRange([duplicate, duplicate]);
        checks.Check(queue.DurationMs == 2L * duplicate.DurationMs, "Summary counts duplicate durations");
        queue.Clear(); queue.Undo(); checks.Check(queue.Entries.Count == 2, "Clear is undoable");
        queue.Forget();
        queue.AddRange(Enumerable.Range(0, ListeningQueue.Capacity).Select(i => Song(i.ToString(), "a")));
        checks.Throws<InvalidOperationException>(() => queue.PlayNext(duplicate), "Play next enforces capacity");
        queue.Forget(); checks.Check(!queue.CanUndo && queue.DurationMs == 0, "Forget clears entries, undo, and duration");
    }

}
