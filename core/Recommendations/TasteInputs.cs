namespace Cadenza.Core.Recommendations;

/// <summary>Raw listening data from the user's own account and in-app use.</summary>
/// <param name="SavedTracks">Saved ("liked") Spotify tracks with the date added.</param>
/// <param name="TopArtists">Top artists for the short, medium and long term, best first.</param>
/// <param name="TopTracks">Top tracks for the short, medium and long term, best first.</param>
/// <param name="RecentlyPlayed">Recently played tracks with play times.</param>
/// <param name="Events">Cadenza's local listening events.</param>
public sealed record TasteInputs(
    IReadOnlyList<DatedTrack> SavedTracks,
    IReadOnlyDictionary<TimeRange, IReadOnlyList<ArtistRef>> TopArtists,
    IReadOnlyDictionary<TimeRange, IReadOnlyList<Models.Track>> TopTracks,
    IReadOnlyList<DatedTrack> RecentlyPlayed,
    IReadOnlyList<ListeningEvent> Events)
{
    public static TasteInputs Empty { get; } = new([], new Dictionary<TimeRange, IReadOnlyList<ArtistRef>>(), new Dictionary<TimeRange, IReadOnlyList<Models.Track>>(), [], []);
}
