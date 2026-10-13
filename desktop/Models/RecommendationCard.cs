using System.Globalization;
using Cadenza.Core.Models;
using Cadenza.Core.Recommendations;

namespace Cadenza.Desktop.Models;

/// <summary>A recommendation as shown on a "For you" card: the track, its reason and the score behind it.</summary>
public sealed class RecommendationCard(Recommendation recommendation)
{
    public Track Track { get; } = recommendation.Track;

    public string Reason { get; } = recommendation.Reason;

    public RecommendationShelf Shelf { get; } = recommendation.Shelf;

    /// <summary>The score terms in plain words, for the "Why?" tooltip.</summary>
    public string Why { get; } = string.Format(
        CultureInfo.CurrentCulture,
        "Close to your favourites {0:0.00}\nFamiliar artist {1:0.00}\nNew to you {2:0.00}\nShown recently or disliked −{3:0.00}\nScore {4:0.00}",
        recommendation.Score.Similarity,
        recommendation.Score.Affinity,
        recommendation.Score.Novelty,
        recommendation.Score.Penalty,
        recommendation.Score.Total);
}
