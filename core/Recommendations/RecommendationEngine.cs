using Cadenza.Core.Models;

namespace Cadenza.Core.Recommendations;

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
public sealed class RecommendationEngine(ISimilarArtistProvider similarArtists, ITrackResolver tracks)
{
    public const int SeedCount = 8;
    public const int SimilarPerSeed = 25;
    public const int CandidateArtistCount = 20;
    public const int DeeperCutArtistCount = 6;
    public const int TracksPerArtist = 3;
    public const int ResultCount = 24;
    public const int MaxPerArtist = 2;
    public const int ExplorationSlots = 3;

    public const double SimilarityWeight = 1.0;
    public const double FamiliarityWeight = 0.8;
    public const double NoveltyWeight = 0.8;
    public const double RecentlySuggestedPenalty = 0.6;
    public const double NegativeArtistPenalty = 0.5;
    public const double SameArtistPenalty = 0.35;

    /// <summary>Artists with at most this normalised affinity count as "new to you".</summary>
    public const double NewArtistThreshold = 0.05;

    /// <param name="profile">The listener's taste.</param>
    /// <param name="discovery">0 = stick to what you know, 1 = mostly new artists.</param>
    /// <param name="recentlySuggested">Track IDs shown in recent refreshes, gently pushed down.</param>
    public async Task<RecommendationResult> RecommendAsync(
        TasteProfile profile, double discovery, IReadOnlySet<string> recentlySuggested, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(recentlySuggested);
        discovery = Math.Clamp(discovery, 0, 1);
        var warnings = new List<string>();
        if (profile.IsEmpty)
        {
            return new RecommendationResult([], warnings);
        }

        var seeds = profile.TopArtists(SeedCount);
        var similarity = await CollectSimilarArtistsAsync(seeds, profile, warnings, ct);

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
            ct.ThrowIfCancellationRequested();
            IReadOnlyList<Track> found;
            try
            {
                found = await tracks.FindTracksAsync(artist, ct);
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
        IReadOnlyList<(string Name, double Weight)> seeds, TasteProfile profile, List<string> warnings, CancellationToken ct)
    {
        var seedKeys = seeds.Select(seed => TasteProfile.Key(seed.Name)).ToHashSet();
        var result = new Dictionary<string, SimilarCandidate>();
        foreach (var (seedName, seedWeight) in seeds)
        {
            IReadOnlyList<SimilarArtist> similar;
            try
            {
                similar = await similarArtists.GetSimilarAsync(seedName, ct);
            }
            catch (HttpRequestException)
            {
                warnings.Add($"Couldn't reach the similar-artist service for {seedName}.");
                continue;
            }

            foreach (var artist in similar.Take(SimilarPerSeed))
            {
                var key = TasteProfile.Key(artist.Name);
                if (seedKeys.Contains(key) || profile.Affinity(artist.Name) < 0)
                {
                    continue;
                }

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
    /// Greedy diversity re-rank: repeatedly take the best remaining track after a penalty for each pick
    /// already made from the same artist, capped at <see cref="MaxPerArtist"/>; then make sure at least
    /// <see cref="ExplorationSlots"/> picks are artists new to the listener.
    /// </summary>
    private static List<Scored> Rerank(List<Scored> candidates)
    {
        var picks = new List<Scored>();
        var perArtist = new Dictionary<string, int>();
        var remaining = candidates.ToList();
        while (picks.Count < ResultCount && remaining.Count > 0)
        {
            var best = remaining
                .Select(candidate => (Candidate: candidate, Adjusted: candidate.Score.Total - (SameArtistPenalty * perArtist.GetValueOrDefault(candidate.ArtistKey))))
                .OrderByDescending(pair => pair.Adjusted)
                .ThenBy(pair => pair.Candidate.Track.Id, StringComparer.Ordinal)
                .First().Candidate;
            remaining.Remove(best);
            if (perArtist.GetValueOrDefault(best.ArtistKey) >= MaxPerArtist)
            {
                continue;
            }

            picks.Add(best);
            perArtist[best.ArtistKey] = perArtist.GetValueOrDefault(best.ArtistKey) + 1;
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
            {
                picks.Remove(weakest);
            }

            picks.Add(explorer with { Explored = true });
        }

        return picks.OrderByDescending(pick => pick.Score.Total).ThenBy(pick => pick.Track.Id, StringComparer.Ordinal).ToList();
    }

    private static Recommendation ToRecommendation(Scored pick)
    {
        if (!pick.IsNewArtist)
        {
            return new Recommendation(pick.Track, RecommendationShelf.DeeperCuts, $"You listen to {pick.Track.LeadArtist} a lot", pick.Score);
        }

        if (pick.Explored)
        {
            var exploredReason = pick.BestSeed is null ? "New to you" : $"New to you · close to {pick.BestSeed}";
            return new Recommendation(pick.Track, RecommendationShelf.SomethingNew, exploredReason, pick.Score);
        }

        var reason = pick.BestSeed is null ? "New to you" : $"Similar to {pick.BestSeed}";
        return new Recommendation(pick.Track, RecommendationShelf.MoreLikeYourFavourites, reason, pick.Score);
    }

    private sealed record SimilarCandidate(string Name, double Score, string BestSeed, double BestContribution);

    private sealed record Scored(Track Track, ScoreBreakdown Score, double Familiarity, string? BestSeed, bool Explored = false)
    {
        public string ArtistKey => TasteProfile.Key(Track.LeadArtist);

        public bool IsNewArtist => Familiarity <= NewArtistThreshold;
    }
}
