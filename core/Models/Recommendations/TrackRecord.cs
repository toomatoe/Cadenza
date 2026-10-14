

namespace Cadenza.Core.Models.Recommendations;

/// <summary>A <see cref="Track"/> in a shape JSON can round-trip, for the recommendation caches.</summary>
public sealed class TrackRecord
{
    public TrackRecord(string id, string name, string artist, string artistId, string album, int durationMs, string url, bool @explicit, string artworkUrl, string leadArtist)
    {
        Id = id;
        Name = name;
        Artist = artist;
        ArtistId = artistId;
        Album = album;
        DurationMs = durationMs;
        Url = url;
        Explicit = @explicit;
        ArtworkUrl = artworkUrl;
        LeadArtist = leadArtist;
    }

    public string Id { get; }

    public string Name { get; }

    public string Artist { get; }

    public string ArtistId { get; }

    public string Album { get; }

    public int DurationMs { get; }

    public string Url { get; }

    public bool Explicit { get; }

    public string ArtworkUrl { get; }

    public string LeadArtist { get; }
    public static TrackRecord From(Track track)
    {
        ArgumentNullException.ThrowIfNull(track);

        return new(track.Id, track.Name, track.Artist, track.ArtistId, track.Album, track.DurationMs, track.Url, track.Explicit, track.ArtworkUrl, track.LeadArtist);
    }

    public Track ToTrack() => new(Id, Name, Artist, ArtistId, Album, DurationMs, Url, Explicit, ArtworkUrl, LeadArtist);
}
