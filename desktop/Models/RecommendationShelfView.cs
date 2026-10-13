namespace Cadenza.Desktop.Models;

/// <summary>One titled row of cards on the "For you" page.</summary>
public sealed class RecommendationShelfView(string title, IReadOnlyList<RecommendationCard> cards)
{
    public string Title { get; } = title;

    public IReadOnlyList<RecommendationCard> Cards { get; } = cards;
}
