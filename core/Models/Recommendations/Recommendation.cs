

namespace Cadenza.Core.Models.Recommendations;

/// <summary>A suggested track, why it was suggested, and how its score was formed.</summary>
public sealed class Recommendation
{
    public Recommendation(Track track, RecommendationShelf shelf, string reason, ScoreBreakdown score)
    {
        Track = track;
        Shelf = shelf;
        Reason = reason;
        Score = score;
    }

    public Track Track { get; }

    public RecommendationShelf Shelf { get; }

    public string Reason { get; }

    public ScoreBreakdown Score { get; }
}
