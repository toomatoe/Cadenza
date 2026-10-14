namespace Cadenza.Core.Models.Recommendations;

/// <summary>Raw listening data from the user's own account and in-app use.</summary>
public sealed class TasteInputs
{
    /// <param name="savedTracks">Saved ("liked") Spotify tracks with the date added.</param>
    /// <param name="topArtists">Top artists for the short, medium and long term, best first.</param>
    /// <param name="topTracks">Top tracks for the short, medium and long term, best first.</param>
    /// <param name="recentlyPlayed">Recently played tracks with play times.</param>
    /// <param name="events">Cadenza's local listening events.</param>
    public TasteInputs(IReadOnlyList<DatedTrack> savedTracks, IReadOnlyDictionary<TimeRange, IReadOnlyList<ArtistRef>> topArtists, IReadOnlyDictionary<TimeRange, IReadOnlyList<Models.Track>> topTracks, IReadOnlyList<DatedTrack> recentlyPlayed, IReadOnlyList<ListeningEvent> events)
    {
        SavedTracks = savedTracks;
        TopArtists = topArtists;
        TopTracks = topTracks;
        RecentlyPlayed = recentlyPlayed;
        Events = events;
    }

    /// <summary>Saved ("liked") Spotify tracks with the date added.</summary>
    public IReadOnlyList<DatedTrack> SavedTracks { get; }

    /// <summary>Top artists for the short, medium and long term, best first.</summary>
    public IReadOnlyDictionary<TimeRange, IReadOnlyList<ArtistRef>> TopArtists { get; }

    /// <summary>Top tracks for the short, medium and long term, best first.</summary>
    public IReadOnlyDictionary<TimeRange, IReadOnlyList<Models.Track>> TopTracks { get; }

    /// <summary>Recently played tracks with play times.</summary>
    public IReadOnlyList<DatedTrack> RecentlyPlayed { get; }

    /// <summary>Cadenza's local listening events.</summary>
    public IReadOnlyList<ListeningEvent> Events { get; }
    public static TasteInputs Empty { get; } = new([], new Dictionary<TimeRange, IReadOnlyList<ArtistRef>>(), new Dictionary<TimeRange, IReadOnlyList<Models.Track>>(), [], []);
}
