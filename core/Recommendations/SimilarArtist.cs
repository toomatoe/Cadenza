namespace Cadenza.Core.Recommendations;

/// <summary>An artist similar to a seed, with similarity normalised to 0–1 (1 = most similar for that seed).</summary>
public sealed record SimilarArtist(string Name, string MusicBrainzId, double Similarity);
