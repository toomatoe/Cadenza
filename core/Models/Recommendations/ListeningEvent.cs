namespace Cadenza.Core.Models.Recommendations;

/// <summary>One listening action, stored only on this device.</summary>
public sealed class ListeningEvent
{
    public ListeningEvent(ListeningEventKind kind, string trackId, string artistName, DateTimeOffset at)
    {
        Kind = kind;
        TrackId = trackId;
        ArtistName = artistName;
        At = at;
    }

    public ListeningEventKind Kind { get; }

    public string TrackId { get; }

    public string ArtistName { get; }

    public DateTimeOffset At { get; }
}
