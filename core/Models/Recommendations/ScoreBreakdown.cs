namespace Cadenza.Core.Models.Recommendations;

/// <summary>The weighted terms that make up a recommendation's score, for the "Why?" explanation.</summary>
public sealed class ScoreBreakdown
{
    public ScoreBreakdown(double similarity, double affinity, double novelty, double penalty)
    {
        Similarity = similarity;
        Affinity = affinity;
        Novelty = novelty;
        Penalty = penalty;
    }

    public double Similarity { get; }

    public double Affinity { get; }

    public double Novelty { get; }

    public double Penalty { get; }
    public double Total => Similarity + Affinity + Novelty - Penalty;
}
