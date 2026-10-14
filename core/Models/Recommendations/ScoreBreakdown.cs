namespace Cadenza.Core.Models.Recommendations;

/// <summary>The weighted terms that make up a recommendation's score, for the "Why?" explanation.</summary>
public sealed record ScoreBreakdown(double Similarity, double Affinity, double Novelty, double Penalty)
{
    public double Total => Similarity + Affinity + Novelty - Penalty;
}
