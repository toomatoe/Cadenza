using Cadenza.Core.Interfaces;
using Cadenza.Core.Models;
using Cadenza.Core.Models.Recommendations;

namespace Cadenza.Core.Services.Recommendations;

/// <summary>
/// Explainable, non-generative recommendations. Every step is deterministic arithmetic over the
/// listener's own signals and open similarity data:
/// <list type="number">
/// <item>Seeds: the listener's top artists by <see cref="TasteProfile"/> affinity.</item>
/// <item>Candidate artists: artists similar to each seed (ListenBrainz), weighted by the seed's affinity,
/// plus the seeds themselves for "deeper cuts".</item>
/// <item>Candidate tracks: each candidate artist's tracks, minus anything known or rejected.</item>
/// <item>Score = similarity + familiarity·(1 − discovery) + novelty·discovery − penalties.</item>
/// <item>Re-rank for diversity (at most two tracks per artist), and reserve slots for artists new to the listener.</item>
/// </list>
/// The full formula and constants are documented in docs/features/recommendations.md.
/// </summary>
public sealed class RecommendationEngine
{
    public const int SeedCount = 8;
    public const int SimilarPerSeed = 25;
    public const int CandidateArtistCount = 20;
    public const int DeeperCutArtistCount = 6;
    public const int TracksPerArtist = 3;
    public const int ResultCount = 24;
    public const int MaxPerArtist = 2;
    public const int ExplorationSlots = 3;

    /// <summary>At most this many picks may come from one favourite artist's neighbours.</summary>
    public const int MaxPerSeed = 5;

    public const double SimilarityWeight = 1.0;
    public const double FamiliarityWeight = 0.8;
    public const double NoveltyWeight = 0.8;
    public const double RecentlySuggestedPenalty = 0.6;
    public const double NegativeArtistPenalty = 0.5;
    public const double SameArtistPenalty = 0.35;

    /// <summary>Subtracted per earlier pick sharing the same "Similar to" seed, so one favourite can't take over the list.</summary>
    public const double SameSeedPenalty = 0.2;

    /// <summary>Artists with at most this normalised affinity count as "new to you".</summary>
    public const double NewArtistThreshold = 0.05;
    private readonly ISimilarArtistProvider _similarArtists;
    private readonly ITrackResolver _tracks;

    public RecommendationEngine(ISimilarArtistProvider similarArtists, ITrackResolver tracks)
    {
        _similarArtists = similarArtists;
        _tracks = tracks;
    }

    /// <param name="profile">The listener's taste.</param>
    /// <param name="discovery">0 = stick to what you know, 1 = mostly new artists.</param>
    /// <param name="recentlySuggested">Track IDs shown in recent refreshes, gently pushed down.</param>
    public async Task<RecommendationResult> RecommendAsync(
        TasteProfile profile, double discovery, IReadOnlySet<string> recentlySuggested, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(recentlySuggested);
        discovery = Math.Clamp(discovery, 0, 1);
        var warnings = new List<string>();
        if (profile.IsEmpty)
            return new RecommendationResult([], warnings);

        var seeds = profile.TopArtists(SeedCount);
        var similarity = await CollectSimilarArtistsAsync(seeds, profile, warnings, cancellationToken);

        // Candidate artists: the most similar non-seed artists, plus the favourites themselves.
        var candidateArtists = similarity.Values
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Name, StringComparer.Ordinal)
            .Take(CandidateArtistCount)
            .Select(candidate => candidate.Name)
            .Concat(seeds.Take(DeeperCutArtistCount).Select(seed => seed.Name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var maxSimilarity = similarity.Values.Select(candidate => candidate.Score).DefaultIfEmpty(0).Max();

        var scored = new List<Scored>();
        foreach (var artist in candidateArtists)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<Track> found;
            try
            {
                found = await _tracks.FindTracksAsync(artist, cancellationToken);
            }
            catch (HttpRequestException)
            {
                warnings.Add($"Couldn't look up tracks by {artist}.");
                continue;
            }

            var artistKey = TasteProfile.Key(artist);
            similarity.TryGetValue(artistKey, out var similar);
            var accepted = found
                .Where(track => TasteProfile.Key(track.LeadArtist) == artistKey)
                .Where(track => !profile.KnownTrackIds.Contains(track.Id) && !profile.RejectedTrackIds.Contains(track.Id))
                .DistinctBy(track => track.Id)
                .Take(TracksPerArtist);
            foreach (var track in accepted)
            {
                scored.Add(Score(track, similar, maxSimilarity, profile, discovery, recentlySuggested));
            }
        }

        var picks = Rerank(scored);

        return new RecommendationResult(picks.Select(ToRecommendation).ToArray(), warnings);
    }

    private async Task<Dictionary<string, SimilarCandidate>> CollectSimilarArtistsAsync(
        IReadOnlyList<(string Name, double Weight)> seeds, TasteProfile profile, List<string> warnings, CancellationToken cancellationToken)
    {
        var seedKeys = seeds.Select(seed => TasteProfile.Key(seed.Name)).ToHashSet();
        var result = new Dictionary<string, SimilarCandidate>();

        // Look up every seed at once (the provider throttles MusicBrainz itself), then combine
        // in seed order so the result stays deterministic.
        var lookups = await Task.WhenAll(seeds.Select(seed => TryGetSimilarAsync(seed.Name, cancellationToken)));
        for (var i = 0; i < seeds.Count; i++)
        {
            var (seedName, seedAffinity) = seeds[i];
            if (lookups[i] is not { } similar)
            {
                warnings.Add($"Couldn't reach the similar-artist service for {seedName}.");
                continue;
            }

            // Square root narrows the gap between your top artist and the rest, so one favourite
            // with many saved tracks doesn't drown out the others.
            var seedWeight = Math.Sqrt(seedAffinity);
            foreach (var artist in similar.Take(SimilarPerSeed))
            {
                var key = TasteProfile.Key(artist.Name);
                if (seedKeys.Contains(key) || profile.Affinity(artist.Name) < 0)
                    continue;

                // Sum over seeds: an artist close to several favourites ranks higher.
                var contribution = seedWeight * artist.Similarity;
                result.TryGetValue(key, out var existing);
                var bestSeed = existing is null || contribution > existing.BestContribution ? seedName : existing.BestSeed;
                result[key] = new SimilarCandidate(
                    artist.Name,
                    (existing?.Score ?? 0) + contribution,
                    bestSeed,
                    Math.Max(existing?.BestContribution ?? 0, contribution));
            }
        }

        return result;
    }

    private async Task<IReadOnlyList<SimilarArtist>?> TryGetSimilarAsync(string seedName, CancellationToken cancellationToken)
    {
        try
        {
            return await _similarArtists.GetSimilarAsync(seedName, cancellationToken);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    private static Scored Score(
        Track track,
        SimilarCandidate? similar,
        double maxSimilarity,
        TasteProfile profile,
        double discovery,
        IReadOnlySet<string> recentlySuggested)
    {
        var familiarity = profile.NormalizedAffinity(track.LeadArtist);
        var similarityTerm = similar is null || maxSimilarity <= 0 ? 0 : similar.Score / maxSimilarity;
        var novelty = 1 / (1 + (4 * familiarity));
        var penalty = (recentlySuggested.Contains(track.Id) ? RecentlySuggestedPenalty : 0) +
                      (profile.Affinity(track.LeadArtist) < 0 ? NegativeArtistPenalty : 0);
        var breakdown = new ScoreBreakdown(
            SimilarityWeight * similarityTerm,
            FamiliarityWeight * (1 - discovery) * familiarity,
            NoveltyWeight * discovery * novelty,
            penalty);

        return new Scored(track, breakdown, familiarity, similar?.BestSeed);
    }

    /// <summary>
    /// Greedy diversity re-rank: repeatedly take the best remaining track after penalties for earlier
    /// picks by the same artist and from the same seed, capped at <see cref="MaxPerArtist"/> and
    /// <see cref="MaxPerSeed"/>; then make sure at least <see cref="ExplorationSlots"/> picks are
    /// artists new to the listener. The pick order is the display order.
    /// </summary>
    private static List<Scored> Rerank(List<Scored> candidates)
    {
        var picks = new List<Scored>();
        var perArtist = new Dictionary<string, int>();
        var perSeed = new Dictionary<string, int>();
        var remaining = candidates.ToList();
        double Adjusted(Scored candidate) =>
            candidate.Score.Total
            - (SameArtistPenalty * perArtist.GetValueOrDefault(candidate.ArtistKey))
            - (candidate.BestSeed is { } seed ? SameSeedPenalty * perSeed.GetValueOrDefault(seed) : 0);
        while (picks.Count < ResultCount && remaining.Count > 0)
        {
            var best = remaining
                .OrderByDescending(Adjusted)
                .ThenBy(candidate => candidate.Track.Id, StringComparer.Ordinal)
                .First();
            remaining.Remove(best);
            if (perArtist.GetValueOrDefault(best.ArtistKey) >= MaxPerArtist ||
                (best.BestSeed is { } bestSeed && perSeed.GetValueOrDefault(bestSeed) >= MaxPerSeed))
                continue;

            picks.Add(best);
            perArtist[best.ArtistKey] = perArtist.GetValueOrDefault(best.ArtistKey) + 1;
            if (best.BestSeed is { } seedName)
                perSeed[seedName] = perSeed.GetValueOrDefault(seedName) + 1;
        }

        // Exploration: swap the weakest familiar picks for the best unpicked new-artist tracks.
        var newArtistPicks = picks.Count(pick => pick.IsNewArtist);
        var explorers = remaining
            .Where(candidate => candidate.IsNewArtist && perArtist.GetValueOrDefault(candidate.ArtistKey) == 0)
            .OrderByDescending(candidate => candidate.Score.Total)
            .ThenBy(candidate => candidate.Track.Id, StringComparer.Ordinal)
            .DistinctBy(candidate => candidate.ArtistKey)
            .Take(Math.Max(0, ExplorationSlots - newArtistPicks))
            .ToArray();
        foreach (var explorer in explorers)
        {
            var weakest = picks.Where(pick => !pick.IsNewArtist).OrderBy(pick => pick.Score.Total).FirstOrDefault();
            if (weakest is not null && picks.Count >= ResultCount)
                picks.Remove(weakest);

            picks.Add(new Scored(explorer.Track, explorer.Score, explorer.Familiarity, explorer.BestSeed, explored: true));
        }

        // Keep the re-ranked order: sorting by raw score again would regroup one seed's artists at the top.
        return picks;
    }

    private static Recommendation ToRecommendation(Scored pick)
    {
        if (!pick.IsNewArtist)
            return new Recommendation(pick.Track, RecommendationShelf.DeeperCuts, $"You listen to {pick.Track.LeadArtist} a lot", pick.Score);

        if (pick.Explored)
        {
            var exploredReason = pick.BestSeed is null ? "New to you" : $"New to you · close to {pick.BestSeed}";
            return new Recommendation(pick.Track, RecommendationShelf.SomethingNew, exploredReason, pick.Score);
        }

        var reason = pick.BestSeed is null ? "New to you" : $"Similar to {pick.BestSeed}";

        return new Recommendation(pick.Track, RecommendationShelf.MoreLikeYourFavourites, reason, pick.Score);
    }

    private sealed class SimilarCandidate
    {
        public SimilarCandidate(string name, double score, string bestSeed, double bestContribution)
        {
            Name = name;
            Score = score;
            BestSeed = bestSeed;
            BestContribution = bestContribution;
        }

        public string Name { get; }

        public double Score { get; }

        public string BestSeed { get; }

        public double BestContribution { get; }
    }
    private sealed class Scored
    {
        public Scored(Track track, ScoreBreakdown score, double familiarity, string? bestSeed, bool explored = false)
        {
            Track = track;
            Score = score;
            Familiarity = familiarity;
            BestSeed = bestSeed;
            Explored = explored;
        }

        public Track Track { get; }

        public ScoreBreakdown Score { get; }

        public double Familiarity { get; }

        public string? BestSeed { get; }

        public bool Explored { get; }
        public string ArtistKey => TasteProfile.Key(Track.LeadArtist);

        public bool IsNewArtist => Familiarity <= NewArtistThreshold;
    }
}
