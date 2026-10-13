using Cadenza.Core.Models;

namespace Cadenza.Core.Recommendations;

/// <summary>A suggested track, why it was suggested, and how its score was formed.</summary>
public sealed record Recommendation(Track Track, RecommendationShelf Shelf, string Reason, ScoreBreakdown Score);
