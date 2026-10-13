namespace Cadenza.Core.Models;

/// <summary>Player states. Values match the native engine's event kinds (see native/README.md).</summary>
public enum PlaybackState
{
    Loading = 10,
    Playing = 11,
    Paused = 12,
    Stopped = 13,
    Ended = 14,
    Failed = 15
}
