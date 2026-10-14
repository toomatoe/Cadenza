using Cadenza.Core.Models;
using Cadenza.Core.Recommendations;
using Cadenza.Tests.Infrastructure;

namespace Cadenza.Tests.Suites;

internal static class RecommendationChecks
{
    private static readonly DateTimeOffset s_now = new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);

    public static async Task RunAsync(CheckRunner checks)
    {
        CheckProfile(checks);
        await CheckEngineAsync(checks);
        await CheckBalanceAsync(checks);
        await CheckCachesAsync(checks);
        CheckHistory(checks);
    }

    /// <summary>One favourite with many saved tracks must not take over the list.</summary>
    private static async Task CheckBalanceAsync(CheckRunner checks)
    {
        SimilarArtist[] Neighbours(string prefix) =>
            Enumerable.Range(1, 10).Select(i => new SimilarArtist($"{prefix}{i}", $"{prefix}{i}", 1.0 - (i * 0.04))).ToArray();
        var similar = new FakeSimilar(new Dictionary<string, SimilarArtist[]>
        {
            ["Alpha"] = Neighbours("A"),
            ["Beta"] = Neighbours("B"),
            ["Gamma"] = Neighbours("G"),
        });
        var saved = Enumerable.Range(1, 30).Select(i => Dated(Song($"alpha-saved-{i}", "Alpha"), i))
            .Concat([Dated(Song("beta-saved", "Beta"), 1), Dated(Song("gamma-saved", "Gamma"), 1)])
            .ToArray();
        var profile = TasteProfile.Build(Inputs(saved, [], []), s_now);
        var result = await new RecommendationEngine(similar, new FakeResolver()).RecommendAsync(profile, 0.3, new HashSet<string>(), CancellationToken.None);

        var seedsInTopTen = result.Items.Take(10).Select(item => item.Reason).Where(reason => reason.StartsWith("Similar to ", StringComparison.Ordinal)).Distinct().Count();
        checks.Check(seedsInTopTen >= 2, "The top ten draws on more than one favourite artist");
        checks.Check(result.Items.Count(item => item.Reason == "Similar to Alpha") <= RecommendationEngine.MaxPerSeed,
            "One favourite fills at most MaxPerSeed suggestions");
        checks.Check(result.Items.Any(item => item.Reason == "Similar to Gamma"), "Lighter favourites still get suggestions");
    }

    private static async Task CheckCachesAsync(CheckRunner checks)
    {
        var folder = Directory.CreateTempSubdirectory("cadenza-recs-");
        try
        {
            var now = s_now;
            var counting = new CountingResolver();
            var path = Path.Combine(folder.FullName, "tracks.json");
            var cached = new CachedTrackResolver(counting, path, () => now);
            await cached.FindTracksAsync("Alpha", CancellationToken.None);
            var again = await new CachedTrackResolver(counting, path, () => now).FindTracksAsync("alpha ", CancellationToken.None);
            checks.Check(counting.Calls == 1 && again.Count == 5 && again[0].LeadArtist == "Alpha",
                "Artist track searches are cached on disk across restarts");
            now += CachedTrackResolver.Lifetime + TimeSpan.FromMinutes(1);
            await cached.FindTracksAsync("Alpha", CancellationToken.None);
            checks.Check(counting.Calls == 2, "Cached searches expire after a week");

            var snapshotPath = Path.Combine(folder.FullName, "last.json");
            var track = Song("snap-1", "Alpha");
            var made = new RecommendationResult([new Recommendation(track, RecommendationShelf.DeeperCuts, "You listen to Alpha a lot", new ScoreBreakdown(0.5, 0.2, 0.1, 0))], []);
            RecommendationSnapshot.Save(snapshotPath, made, s_now);
            var loaded = RecommendationSnapshot.Load(snapshotPath, s_now.AddHours(1));
            checks.Check(loaded?.Result.Items is [{ Track.Id: "snap-1", Shelf: RecommendationShelf.DeeperCuts, Score.Similarity: 0.5 }],
                "The last suggestions are saved and reloaded");
            checks.Check(RecommendationSnapshot.Load(snapshotPath, s_now + RecommendationSnapshot.FreshFor + TimeSpan.FromMinutes(1)) is null,
                "Old saved suggestions are ignored");
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    private static void CheckProfile(CheckRunner checks)
    {
        checks.Check(Math.Abs(TasteProfile.Decay(TimeSpan.FromDays(14), TimeSpan.FromDays(14)) - 0.5) < 1e-9, "Decay halves after one half-life");
        checks.Check(TasteProfile.RankFactor(0, 50) == 1 && Math.Abs(TasteProfile.RankFactor(49, 50) - 0.2) < 1e-9, "Rank factor runs from 1 to 0.2");

        var inputs = Inputs(
            saved: [Dated(Song("s1", "Alpha"), 0), Dated(Song("s2", "Alpha"), 400)],
            topArtists: [new ArtistRef("b", "Beta")],
            events:
            [
                new ListeningEvent(ListeningEventKind.Skipped, "x1", "Gamma", s_now),
                new ListeningEvent(ListeningEventKind.NotInterested, "x2", "Gamma", s_now),
            ]);
        var profile = TasteProfile.Build(inputs, s_now);
        checks.Check(profile.Affinity("Beta") > profile.Affinity("Alpha"), "A short-term top artist outweighs two saved tracks");
        checks.Check(profile.Affinity("Gamma") < 0, "Skips and 'not interested' make affinity negative");
        checks.Check(profile.NormalizedAffinity("Gamma") == 0 && profile.NormalizedAffinity("Beta") == 1, "Normalised affinity is clamped to 0–1");
        checks.Check(profile.KnownTrackIds.Contains("s1") && profile.RejectedTrackIds.Contains("x2"), "Known and rejected tracks are tracked");
        checks.Check(profile.TopArtists(1)[0].Name == "Beta", "Seeds are the highest-affinity artists");
        checks.Check(TasteProfile.Build(TasteInputs.Empty, s_now).IsEmpty, "No data gives an empty profile");
    }

    private static async Task CheckEngineAsync(CheckRunner checks)
    {
        var similar = new FakeSimilar(new Dictionary<string, SimilarArtist[]>
        {
            ["Alpha"] = [new("Delta", "d", 1.0), new("Epsilon", "e", 0.6), new("Hated", "h", 0.9)],
            ["Beta"] = [new("Delta", "d", 0.8), new("Zeta", "z", 0.5)],
        });
        var resolver = new FakeResolver();
        var engine = new RecommendationEngine(similar, resolver);
        var inputs = Inputs(
            saved: [Dated(Song("alpha-1", "Alpha"), 1), Dated(Song("alpha-2", "Alpha"), 2)],
            topArtists: [new ArtistRef("a", "Alpha"), new ArtistRef("b", "Beta")],
            events: [new ListeningEvent(ListeningEventKind.NotInterested, "hated-1", "Hated", s_now)]);
        var profile = TasteProfile.Build(inputs, s_now);
        var none = new HashSet<string>();

        var balanced = await engine.RecommendAsync(profile, 0.3, none, CancellationToken.None);
        var ids = balanced.Items.Select(item => item.Track.Id).ToArray();
        checks.Check(balanced.Items.Count > 0, "Engine returns recommendations");
        checks.Check(!ids.Contains("alpha-1") && !ids.Contains("alpha-2"), "Already-saved tracks are never recommended");
        checks.Check(balanced.Items.All(item => item.Track.LeadArtist != "Hated"), "Artists with negative affinity are excluded");
        checks.Check(
            balanced.Items.GroupBy(item => item.Track.LeadArtist).All(group => group.Count() <= RecommendationEngine.MaxPerArtist),
            "At most two tracks per artist");
        checks.Check(
            balanced.Items.Where(item => item.Track.LeadArtist == "Delta").All(item => item.Reason == "Similar to Alpha"),
            "Reasons name the seed that contributed most");
        checks.Check(
            balanced.Items.Where(item => item.Track.LeadArtist == "Alpha").All(item => item.Shelf == RecommendationShelf.DeeperCuts),
            "Unsaved tracks by favourite artists are deeper cuts");
        checks.Check(balanced.Items.All(item => Math.Abs(item.Score.Total - (item.Score.Similarity + item.Score.Affinity + item.Score.Novelty - item.Score.Penalty)) < 1e-9),
            "Score is the sum of its documented terms");

        var again = await engine.RecommendAsync(profile, 0.3, none, CancellationToken.None);
        checks.Check(again.Items.Select(item => item.Track.Id).SequenceEqual(ids), "Same inputs give the same ranking");

        var familiar = await engine.RecommendAsync(profile, 0.0, none, CancellationToken.None);
        var adventurous = await engine.RecommendAsync(profile, 1.0, none, CancellationToken.None);
        static double NewShareOfTop(RecommendationResult result) =>
            result.Items.Take(4).Count(item => item.Shelf != RecommendationShelf.DeeperCuts) / 4.0;
        checks.Check(NewShareOfTop(adventurous) >= NewShareOfTop(familiar), "Raising discovery favours new artists");

        var top = balanced.Items[0].Track.Id;
        var penalised = await engine.RecommendAsync(profile, 0.3, new HashSet<string> { top }, CancellationToken.None);
        checks.Check(penalised.Items.FirstOrDefault(item => item.Track.Id == top) is not { } repeat || repeat.Score.Penalty > 0, "Recently suggested tracks are pushed down");

        var offline = new RecommendationEngine(new FailingSimilar(), resolver);
        var degraded = await offline.RecommendAsync(profile, 0.3, none, CancellationToken.None);
        checks.Check(degraded.Warnings.Count > 0 && degraded.Items.All(item => item.Shelf == RecommendationShelf.DeeperCuts),
            "Without the similarity service, only deeper cuts are offered, with a warning");

        var empty = await engine.RecommendAsync(TasteProfile.Build(TasteInputs.Empty, s_now), 0.3, none, CancellationToken.None);
        checks.Check(empty.Items.Count == 0, "No taste data gives no recommendations");
    }

    private static void CheckHistory(CheckRunner checks)
    {
        var store = new ListeningHistoryStore(path: null);
        for (var i = 0; i < ListeningHistoryStore.MaxEvents + 5; i++)
        {
            store.Record(new ListeningEvent(ListeningEventKind.Completed, $"t{i}", "Alpha", s_now));
        }

        checks.Check(store.Events.Count == ListeningHistoryStore.MaxEvents, "Listening history is bounded");
        for (var refresh = 0; refresh < ListeningHistoryStore.RememberedRefreshes + 1; refresh++)
        {
            store.RememberSuggestions([$"r{refresh}"]);
        }

        checks.Check(!store.RecentlySuggested.Contains("r0") && store.RecentlySuggested.Contains("r3"), "Only the last few refreshes are remembered");
        store.Clear();
        checks.Check(store.Events.Count == 0 && store.RecentlySuggested.Count == 0, "History can be cleared");
    }

    private static Track Song(string id, string artist) =>
        new(id, id, artist, artist, "Album", 180000, "https://open.spotify.com/track/" + id, false);

    private static DatedTrack Dated(Track track, int daysAgo) => new(track, s_now.AddDays(-daysAgo));

    private static TasteInputs Inputs(DatedTrack[] saved, ArtistRef[] topArtists, ListeningEvent[] events) =>
        new(saved, new Dictionary<TimeRange, IReadOnlyList<ArtistRef>> { [TimeRange.ShortTerm] = topArtists },
            new Dictionary<TimeRange, IReadOnlyList<Track>>(), [], events);

    private sealed class FakeSimilar(Dictionary<string, SimilarArtist[]> map) : ISimilarArtistProvider
    {
        public Task<IReadOnlyList<SimilarArtist>> GetSimilarAsync(string artistName, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<SimilarArtist>>(map.TryGetValue(artistName, out var similar) ? similar : []);
    }

    private sealed class FailingSimilar : ISimilarArtistProvider
    {
        public Task<IReadOnlyList<SimilarArtist>> GetSimilarAsync(string artistName, CancellationToken ct) =>
            Task.FromException<IReadOnlyList<SimilarArtist>>(new HttpRequestException("offline"));
    }

    private sealed class CountingResolver : ITrackResolver
    {
        public int Calls { get; private set; }

        public Task<IReadOnlyList<Track>> FindTracksAsync(string artistName, CancellationToken ct)
        {
            Calls++;
            return new FakeResolver().FindTracksAsync(artistName, ct);
        }
    }

    /// <summary>Every artist has five tracks; Alpha's first two are the saved ones.</summary>
    private sealed class FakeResolver : ITrackResolver
    {
        public Task<IReadOnlyList<Track>> FindTracksAsync(string artistName, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<Track>>(Enumerable.Range(1, 5)
                .Select(i => Song($"{artistName.ToLowerInvariant()}-{i}", artistName))
                .ToArray());
    }
}
