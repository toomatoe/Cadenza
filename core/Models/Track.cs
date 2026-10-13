using System.Globalization;

namespace Cadenza.Core.Models;

/// <summary>A Spotify track as shown in the library, search results, playlists and the queue.</summary>
public sealed record Track
{
    // WinUI's metadata generator treats init-only setters as regular setters, so these are
    // get-only properties set by the constructor to keep the model immutable and bindable.
    public Track(
        string id,
        string name,
        string artist,
        string artistId,
        string album,
        int durationMs,
        string url,
        bool explicitContent,
        string artworkUrl = "",
        string leadArtist = "",
        MusicSource source = MusicSource.Spotify)
    {
        LeadArtist = leadArtist.Length > 0 ? leadArtist : artist;
        Source = source;
        Id = id;
        Name = name;
        Artist = artist;
        ArtistId = artistId;
        Album = album;
        DurationMs = durationMs;
        Url = url;
        Explicit = explicitContent;
        ArtworkUrl = artworkUrl;
    }

    public string Id { get; }

    public string Name { get; }

    /// <summary>All artist names, comma separated.</summary>
    public string Artist { get; }

    /// <summary>The first credited artist's name; used to match artists across services.</summary>
    public string LeadArtist { get; }

    /// <summary>The lead artist's ID, used for shuffle spacing.</summary>
    public string ArtistId { get; }

    public string Album { get; }

    public int DurationMs { get; }

    /// <summary>The track's open.spotify.com page.</summary>
    public string Url { get; }

    public bool Explicit { get; }

    public string ArtworkUrl { get; }

    public MusicSource Source { get; }

    /// <summary>True for Spotify tracks, which have an open.spotify.com page and can be shared by ID.</summary>
    public bool IsSpotify => Source == MusicSource.Spotify;

    /// <summary>Duration formatted as m:ss.</summary>
    public string Duration => TimeSpan.FromMilliseconds(DurationMs).ToString(@"m\:ss", CultureInfo.InvariantCulture);
}
