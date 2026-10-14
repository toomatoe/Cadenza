namespace Cadenza.Desktop.Models;

/// <summary>One titled row of cards on the "For you" page.</summary>
public sealed class RecommendationShelfView
{
    public RecommendationShelfView(string title, IReadOnlyList<RecommendationCard> cards)
    {
        Title = title;
        Cards = cards;
    }

    public string Title { get; }

    public IReadOnlyList<RecommendationCard> Cards { get; }
}
