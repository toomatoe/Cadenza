namespace Cadenza.Core.Models;

public record PlaybackUpdate(PlaybackState State, uint PositionMs, uint TrackStamp, int ErrorCode);
