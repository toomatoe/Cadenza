namespace Cadenza.Core;

public sealed class ListeningQueue
{
    public const int Capacity = 500;
    private readonly List<Track> tracks = [];
    private readonly List<Track[]> undo = [];
    public IReadOnlyList<Track> Tracks => tracks.AsReadOnly();
    public bool CanUndo => undo.Count > 0;
    private void Snapshot()
    {
        if (undo.Count == 20) undo.RemoveAt(0);
        undo.Add(tracks.ToArray());
    }
    public void Add(Track track)
    {
        if (tracks.Count >= Capacity) throw new InvalidOperationException("Queue is full (500 tracks). Remove a track first.");
        Snapshot(); tracks.Add(track);
    }
    public void RemoveAt(int index) { if (index < 0 || index >= tracks.Count) return; Snapshot(); tracks.RemoveAt(index); }
    public void Move(int index, int destination)
    {
        if (index < 0 || index >= tracks.Count || destination < 0 || destination >= tracks.Count || index == destination) return;
        Snapshot(); var track = tracks[index]; tracks.RemoveAt(index); tracks.Insert(destination, track);
    }
    public void Clear() { if (tracks.Count == 0) return; Snapshot(); tracks.Clear(); }
    public void Undo() { if (!CanUndo) return; tracks.Clear(); tracks.AddRange(undo[^1]); undo.RemoveAt(undo.Count - 1); }
    public void Forget() { tracks.Clear(); undo.Clear(); }
    public void Shuffle(int artistSpacing, Random random)
    {
        if (tracks.Count < 2) return;
        Snapshot();
        var remaining = tracks.ToList();
        tracks.Clear();
        while (remaining.Count > 0)
        {
            var recent = tracks.TakeLast(Math.Clamp(artistSpacing, 0, 10)).Select(t => t.ArtistId).ToHashSet();
            var candidates = remaining.Where(t => !recent.Contains(t.ArtistId)).ToArray();
            // If the requested spacing is impossible, relax it rather than discard tracks.
            var next = candidates.Length > 0 ? candidates[random.Next(candidates.Length)] : remaining[random.Next(remaining.Count)];
            tracks.Add(next); remaining.Remove(next);
        }
    }
}
