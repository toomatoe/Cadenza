using Cadenza.Core.Models;

namespace Cadenza.Core.Social;

/// <summary>A song in the shared Cadenza library: music someone uploaded because it isn't on Spotify.</summary>
public sealed class LibraryTrack
{
    public LibraryTrack(string id, string title, string artist, string album, int durationMs, string format, string uploadedBy)
    {
        Id = id;
        Title = title;
        Artist = artist;
        Album = album;
        DurationMs = durationMs;
        Format = format;
        UploadedBy = uploadedBy;
    }

    public string Id { get; }

    public string Title { get; }

    public string Artist { get; }

    public string Album { get; }

    public int DurationMs { get; }

    public string Format { get; }

    public string UploadedBy { get; }

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

/// <summary>Song details sent with an upload, read from the file's tags (or its name) on the uploader's PC.</summary>
public sealed class UploadMetadata
{
    public UploadMetadata(string title, string artist, string album, int durationMs)
    {
        Title = title;
        Artist = artist;
        Album = album;
        DurationMs = durationMs;
    }

    public string Title { get; }

    public string Artist { get; }

    public string Album { get; }

    public int DurationMs { get; }
}
