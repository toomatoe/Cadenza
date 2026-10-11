namespace Cadenza.Core.Models;

public sealed record PlaybackUpdate(PlaybackState State, uint PositionMs, uint TrackStamp, int ErrorCode);
