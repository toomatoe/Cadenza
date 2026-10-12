namespace Cadenza.Core.Models;

public record QueueEntry
{
    public Guid Id { get; }
    public Track Track { get; }
    public QueueEntry(Guid id, Track track) { Id = id; Track = track; }
}
