namespace Cadenza.Core.Models;

/// <summary>One occurrence of a track in the <see cref="ListeningQueue"/>.</summary>
public sealed record QueueEntry
{
    // Get-only properties for WinUI binding; see the comment on Track.
    public QueueEntry(Guid id, Track track)
    {
        Id = id;
        Track = track;
    }

    public Guid Id { get; }

    public Track Track { get; }
}
