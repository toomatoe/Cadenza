namespace Cadenza.Core.Models.Recommendations;

/// <summary>Recommendations plus any sources that couldn't be reached.</summary>
public sealed class RecommendationResult
{
    public RecommendationResult(IReadOnlyList<Recommendation> items, IReadOnlyList<string> warnings)
    {
        Items = items;
        Warnings = warnings;
    }

    public IReadOnlyList<Recommendation> Items { get; }

    public IReadOnlyList<string> Warnings { get; }
}
