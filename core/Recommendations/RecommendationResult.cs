namespace Cadenza.Core.Recommendations;

/// <summary>Recommendations plus any sources that couldn't be reached.</summary>
public sealed record RecommendationResult(IReadOnlyList<Recommendation> Items, IReadOnlyList<string> Warnings);
