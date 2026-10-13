namespace Cadenza.Core.Models;

/// <summary>A playback event from the native engine.</summary>
/// <param name="State">The player state.</param>
/// <param name="PositionMs">Playback position in milliseconds.</param>
/// <param name="TrackStamp">Identifies the load request this event belongs to; stale events have an older stamp.</param>
/// <param name="ErrorCode">Failure reason for <see cref="PlaybackState.Failed"/>; see PlaybackErrors.</param>
public sealed record PlaybackUpdate(PlaybackState State, uint PositionMs, uint TrackStamp, int ErrorCode);
