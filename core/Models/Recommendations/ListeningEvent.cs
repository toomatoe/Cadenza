namespace Cadenza.Core.Models.Recommendations;

/// <summary>One listening action, stored only on this device.</summary>
public sealed record ListeningEvent(ListeningEventKind Kind, string TrackId, string ArtistName, DateTimeOffset At);
