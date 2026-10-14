namespace Cadenza.Core.Models;

/// <summary>A playback event from the native engine.</summary>
public sealed class PlaybackUpdate
{
    /// <param name="state">The player state.</param>
    /// <param name="positionMs">Playback position in milliseconds.</param>
    /// <param name="trackStamp">Identifies the load request this event belongs to; stale events have an older stamp.</param>
    /// <param name="errorCode">Failure reason for <see cref="PlaybackState.Failed"/>; see PlaybackErrors.</param>
    public PlaybackUpdate(PlaybackState state, uint positionMs, uint trackStamp, int errorCode)
    {
        State = state;
        PositionMs = positionMs;
        TrackStamp = trackStamp;
        ErrorCode = errorCode;
    }

    /// <summary>The player state.</summary>
    public PlaybackState State { get; }

    /// <summary>Playback position in milliseconds.</summary>
    public uint PositionMs { get; }

    /// <summary>Identifies the load request this event belongs to; stale events have an older stamp.</summary>
    public uint TrackStamp { get; }

    /// <summary>Failure reason for <see cref="PlaybackState.Failed"/>; see PlaybackErrors.</summary>
    public int ErrorCode { get; }
}
