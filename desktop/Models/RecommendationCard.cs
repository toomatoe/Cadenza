using System.Globalization;
using Cadenza.Core.Models;
using Cadenza.Core.Models.Recommendations;

namespace Cadenza.Desktop.Models;

/// <summary>A recommendation as shown on a "For you" card: the track, its reason and the score behind it.</summary>
public sealed class RecommendationCard
{
    public RecommendationCard(Recommendation recommendation)
    {
        Track = recommendation.Track;
        Reason = recommendation.Reason;
        Shelf = recommendation.Shelf;
        Why = string.Format(
        CultureInfo.CurrentCulture,
        "Close to your favourites {0:0.00}\nFamiliar artist {1:0.00}\nNew to you {2:0.00}\nShown recently or disliked −{3:0.00}\nScore {4:0.00}",
        recommendation.Score.Similarity,
        recommendation.Score.Affinity,
        recommendation.Score.Novelty,
        recommendation.Score.Penalty,
        recommendation.Score.Total);
    }

    public Track Track { get; }

    public string Reason { get; }

    public RecommendationShelf Shelf { get; }

    /// <summary>The score terms in plain words, for the "Why?" tooltip.</summary>
    public string Why { get; }
}
