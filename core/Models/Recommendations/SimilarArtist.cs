namespace Cadenza.Core.Models.Recommendations;

/// <summary>An artist similar to a seed, with similarity normalised to 0–1 (1 = most similar for that seed).</summary>
public sealed class SimilarArtist
{
    public SimilarArtist(string name, string musicBrainzId, double similarity)
    {
        Name = name;
        MusicBrainzId = musicBrainzId;
        Similarity = similarity;
    }

    public string Name { get; }

    public string MusicBrainzId { get; }

    public double Similarity { get; }
}
