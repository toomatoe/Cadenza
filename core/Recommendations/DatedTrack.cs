using Cadenza.Core.Models;

namespace Cadenza.Core.Recommendations;

/// <summary>A track with the moment it was saved or played.</summary>
public sealed record DatedTrack(Track Track, DateTimeOffset At);
