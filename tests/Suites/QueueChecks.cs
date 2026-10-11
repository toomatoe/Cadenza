using System.Net;
using System.Text;
using Cadenza.Core.Models;
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
}
