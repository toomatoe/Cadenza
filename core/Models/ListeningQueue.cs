using System.Diagnostics.CodeAnalysis;

namespace Cadenza.Core.Models;

/// <summary>
/// The in-memory listening queue: bounded, with undo for edits. Each occurrence of a track has its own
/// <see cref="QueueEntry.Id"/>, so duplicates can be reordered, played and removed independently.
/// </summary>
[SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix",
    Justification = "\"Listening queue\" is the product term; the type is not a System.Collections.Queue.")]
public sealed class ListeningQueue
{
    public const int Capacity = 500;
    private const int UndoLimit = 20;
    private const int MaxArtistSpacing = 10;

    private readonly List<QueueEntry> _entries = [];
    private readonly List<QueueEntry[]> _undo = [];

    public IReadOnlyList<QueueEntry> Entries => _entries.AsReadOnly();

    public IReadOnlyList<Track> Tracks => _entries.Select(entry => entry.Track).ToArray();

    public long DurationMs => _entries.Sum(entry => (long)Math.Max(0, entry.Track.DurationMs));

    public bool CanUndo => _undo.Count > 0;

    public void Add(Track track) => AddRange([track]);

    /// <summary>Adds all tracks as one undoable edit, or none if they would exceed <see cref="Capacity"/>.</summary>
    /// <exception cref="InvalidOperationException">The queue cannot hold every track.</exception>
    public void AddRange(IEnumerable<Track> tracks)
    {
        // Bound enumeration and reject the whole batch if it cannot fit.
        var batch = tracks.Take(Capacity - _entries.Count + 1).ToArray();
        if (batch.Length > Capacity - _entries.Count)
        {
            throw new InvalidOperationException($"Queue is full ({Capacity} tracks). Remove tracks first.");
        }

        if (batch.Length == 0)
        {
            return;
        }

        Snapshot();
        _entries.AddRange(batch.Select(track => new QueueEntry(Guid.NewGuid(), track)));
    }

    /// <exception cref="InvalidOperationException">The queue is full.</exception>
    public void PlayNext(Track track)
    {
        if (_entries.Count >= Capacity)
        {
            throw new InvalidOperationException($"Queue is full ({Capacity} tracks). Remove a track first.");
        }

        Snapshot();
        _entries.Insert(0, new QueueEntry(Guid.NewGuid(), track));
    }

    /// <summary>Removes an entry that started playing. This is not an edit and cannot be undone.</summary>
    public void Consume(Guid entryId)
    {
        // Undo must never resurrect an occurrence that has already played.
        _entries.RemoveAll(entry => entry.Id == entryId);
        for (var index = 0; index < _undo.Count; index++)
        {
            _undo[index] = _undo[index].Where(entry => entry.Id != entryId).ToArray();
        }
    }

    public void RemoveAt(int index)
    {
        if (index < 0 || index >= _entries.Count)
        {
            return;
        }

        Snapshot();
        _entries.RemoveAt(index);
    }

    public void Move(int index, int destination)
    {
        if (index < 0 || index >= _entries.Count || destination < 0 || destination >= _entries.Count || index == destination)
        {
            return;
        }

        Snapshot();
        var entry = _entries[index];
        _entries.RemoveAt(index);
        _entries.Insert(destination, entry);
    }

    public void Clear()
    {
        if (_entries.Count == 0)
        {
            return;
        }

        Snapshot();
        _entries.Clear();
    }

    public void Undo()
    {
        if (!CanUndo)
        {
            return;
        }

        _entries.Clear();
        _entries.AddRange(_undo[^1]);
        _undo.RemoveAt(_undo.Count - 1);
    }

    /// <summary>Clears entries and undo history, for example when the user disconnects.</summary>
    public void Forget()
    {
        _entries.Clear();
        _undo.Clear();
    }

    /// <summary>
    /// Shuffles while trying to keep <paramref name="artistSpacing"/> tracks between the same lead artist.
    /// Spacing is relaxed when it cannot be met; no entries are dropped.
    /// </summary>
    public void Shuffle(int artistSpacing, Random random)
    {
        ArgumentNullException.ThrowIfNull(random);
        if (_entries.Count < 2)
        {
            return;
        }

        Snapshot();
        var remaining = _entries.ToList();
        _entries.Clear();
        while (remaining.Count > 0)
        {
            var recentArtists = _entries
                .TakeLast(Math.Clamp(artistSpacing, 0, MaxArtistSpacing))
                .Select(entry => entry.Track.ArtistId)
                .ToHashSet();
            var candidates = remaining.Where(entry => !recentArtists.Contains(entry.Track.ArtistId)).ToArray();
            var next = candidates.Length > 0
                ? candidates[random.Next(candidates.Length)]
                : remaining[random.Next(remaining.Count)];
            _entries.Add(next);
            remaining.Remove(next);
        }
    }

    private void Snapshot()
    {
        if (_undo.Count == UndoLimit)
        {
            _undo.RemoveAt(0);
        }

        _undo.Add(_entries.ToArray());
    }
}
