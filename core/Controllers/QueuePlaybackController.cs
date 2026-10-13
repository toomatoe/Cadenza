using Cadenza.Core.Models;

namespace Cadenza.Core.Controllers;

/// <summary>Plays a queue entry and consumes it only once its track has loaded.</summary>
public sealed class QueuePlaybackController(ListeningQueue queue)
{
    /// <summary>
    /// Loads the entry at <paramref name="index"/> with <paramref name="load"/>, then removes that occurrence.
    /// The entry is tracked by ID, so edits made while loading never consume a different track.
    /// </summary>
    /// <returns><see langword="false"/> if <paramref name="index"/> is out of range.</returns>
    public async Task<bool> PlayAsync(int index, Func<Track, CancellationToken, Task> load, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(load);
        if (index < 0 || index >= queue.Entries.Count)
        {
            return false;
        }

        var entry = queue.Entries[index];
        await load(entry.Track, ct);
        ct.ThrowIfCancellationRequested();
        queue.Consume(entry.Id);
        return true;
    }
}
