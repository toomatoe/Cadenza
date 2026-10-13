namespace Cadenza.Core.Models;

/// <summary>Where a track plays from.</summary>
public enum MusicSource
{
    /// <summary>Spotify, through librespot; <see cref="Track.Id"/> is a Spotify track ID.</summary>
    Spotify,

    /// <summary>The shared Cadenza library on the Friends server; <see cref="Track.Id"/> is the library ID.</summary>
    Library
}
