

namespace Cadenza.Core.Models.Recommendations;

/// <summary>A track with the moment it was saved or played.</summary>
public sealed class DatedTrack
{
    public DatedTrack(Track track, DateTimeOffset at)
    {
        Track = track;
        At = at;
    }

    public Track Track { get; }

    public DateTimeOffset At { get; }
}
