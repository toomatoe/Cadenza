using Cadenza.Core.Models;

namespace Cadenza.Core.Recommendations;

/// <summary>A <see cref="Track"/> in a shape JSON can round-trip, for the recommendation caches.</summary>
public sealed record TrackRecord(
    string Id,
    string Name,
    string Artist,
    string ArtistId,
    string Album,
    int DurationMs,
    string Url,
    bool Explicit,
    string ArtworkUrl,
    string LeadArtist)
{
    public static TrackRecord From(Track track)
    {
        ArgumentNullException.ThrowIfNull(track);
        return new(track.Id, track.Name, track.Artist, track.ArtistId, track.Album, track.DurationMs, track.Url, track.Explicit, track.ArtworkUrl, track.LeadArtist);
    }

    public Track ToTrack() => new(Id, Name, Artist, ArtistId, Album, DurationMs, Url, Explicit, ArtworkUrl, LeadArtist);
}
