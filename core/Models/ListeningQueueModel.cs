namespace Cadenza.Core.Models;

public class ListeningQueue
{
    public const int Capacity = 500;
    private readonly List<QueueEntry> entries = [];
    private readonly List<QueueEntry[]> undo = [];
    public IReadOnlyList<QueueEntry> Entries => entries.AsReadOnly();
    public IReadOnlyList<Track> Tracks => entries.Select(entry => entry.Track).ToArray();
    public long DurationMs => entries.Sum(entry => (long)Math.Max(0, entry.Track.DurationMs));
    public bool CanUndo => undo.Count > 0;
    private void Snapshot()
    {
        if (undo.Count == 20) undo.RemoveAt(0);
        undo.Add(entries.ToArray());
    }
    public void Add(Track track) => AddRange([track]);
    public void AddRange(IEnumerable<Track> tracks)
    {
        // Bound enumeration and reject the whole batch if it cannot fit.
        var batch = tracks.Take(Capacity - entries.Count + 1).ToArray();
        if (batch.Length > Capacity - entries.Count)
            throw new InvalidOperationException("Queue is full (500 tracks). Remove tracks first.");
        if (batch.Length == 0) return;
        Snapshot();
        entries.AddRange(batch.Select(track => new QueueEntry(Guid.NewGuid(), track)));
    }
    public void PlayNext(Track track)
    {
        if (entries.Count >= Capacity) throw new InvalidOperationException("Queue is full (500 tracks). Remove a track first.");
        Snapshot();
        entries.Insert(0, new QueueEntry(Guid.NewGuid(), track));
    }
    public void Consume(Guid entryId)
    {
        // Playback is not an edit: undo must never resurrect an already played occurrence.
        entries.RemoveAll(entry => entry.Id == entryId);
        for (var i = 0; i < undo.Count; i++)
            undo[i] = undo[i].Where(entry => entry.Id != entryId).ToArray();
    }
    public void RemoveAt(int index)
    {
        if (index < 0 || index >= entries.Count) return;
        Snapshot();
        entries.RemoveAt(index);
    }
    public void Move(int index, int destination)
    {
        if (index < 0 || index >= entries.Count || destination < 0 || destination >= entries.Count || index == destination) return;
        Snapshot();
        var track = entries[index];
        entries.RemoveAt(index);
        entries.Insert(destination, track);
    }
    public void Clear()
    {
        if (entries.Count == 0) return;
        Snapshot();
        entries.Clear();
    }
    public void Undo()
    {
        if (!CanUndo) return;
        entries.Clear();
        entries.AddRange(undo[^1]);
        undo.RemoveAt(undo.Count - 1);
    }
    public void Forget()
    {
        entries.Clear();
        undo.Clear();
    }
    public void Shuffle(int artistSpacing, Random random)
    {
        if (entries.Count < 2) return;
        Snapshot();
        var remaining = entries.ToList();
        entries.Clear();
        while (remaining.Count > 0)
        {
            var recent = entries.TakeLast(Math.Clamp(artistSpacing, 0, 10)).Select(t => t.Track.ArtistId).ToHashSet();
            var candidates = remaining.Where(t => !recent.Contains(t.Track.ArtistId)).ToArray();
            // If the requested spacing is impossible, relax it rather than discard entries.
            var next = candidates.Length > 0 ? candidates[random.Next(candidates.Length)] : remaining[random.Next(remaining.Count)];
            entries.Add(next);
            remaining.Remove(next);
        }
    }
}
