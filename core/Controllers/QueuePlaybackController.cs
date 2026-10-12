using Cadenza.Core.Models;

namespace Cadenza.Core.Controllers;

public sealed class QueuePlaybackController(ListeningQueue queue)
{
    public async Task<bool> PlayAsync(int index, Func<Track, CancellationToken, Task> load, CancellationToken ct)
    {
        if (index < 0 || index >= queue.Entries.Count) return false;
        var entry = queue.Entries[index];
        await load(entry.Track, ct);
        ct.ThrowIfCancellationRequested();
        queue.Consume(entry.Id);
        return true;
    }
}
