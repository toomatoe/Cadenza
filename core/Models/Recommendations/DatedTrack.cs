

namespace Cadenza.Core.Models.Recommendations;

/// <summary>A track with the moment it was saved or played.</summary>
public sealed record DatedTrack(Track Track, DateTimeOffset At);
