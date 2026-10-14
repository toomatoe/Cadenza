using Cadenza.Core.Models;
using Cadenza.Core.Models.Recommendations;

namespace Cadenza.UnitTests.Fixtures;

/// <summary>A fixed clock and small builders for taste-profile and recommendation tests.</summary>
internal static class RecommendationData
{
    public static readonly DateTimeOffset Now = new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);

    public static Track Song(string id, string artist) =>
        new(id, id, artist, artist, "Album", 180000, "https://open.spotify.com/track/" + id, false);

    public static DatedTrack Dated(Track track, int daysAgo) => new(track, Now.AddDays(-daysAgo));

    public static TasteInputs Inputs(DatedTrack[] saved, ArtistRef[] topArtists, ListeningEvent[] events) =>
        new(saved, new Dictionary<TimeRange, IReadOnlyList<ArtistRef>> { [TimeRange.ShortTerm] = topArtists },
            new Dictionary<TimeRange, IReadOnlyList<Track>>(), [], events);
}
