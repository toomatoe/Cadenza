using Cadenza.Core.Models;

namespace Cadenza.Core.Social;

/// <summary>A song in the shared Cadenza library: music someone uploaded because it isn't on Spotify.</summary>
public sealed record LibraryTrack(
    string Id,
    string Title,
    string Artist,
    string Album,
    int DurationMs,
    string Format,
    string UploadedBy)
{
    /// <summary>As a <see cref="Track"/> for the search list, queue and player.</summary>
    public Track ToTrack() => new(
        Id,
        Title,
        Artist,
        artistId: "",
        album: Album.Length > 0 ? Album : $"Cadenza library · added by {UploadedBy}",
        DurationMs,
        url: "",
        explicitContent: false,
        source: MusicSource.Library);
}
