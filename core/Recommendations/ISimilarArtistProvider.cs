namespace Cadenza.Core.Recommendations;

/// <summary>Finds artists similar to a seed artist.</summary>
public interface ISimilarArtistProvider
{
    /// <returns>Similar artists, most similar first; empty if the artist is unknown.</returns>
    Task<IReadOnlyList<SimilarArtist>> GetSimilarAsync(string artistName, CancellationToken ct);
}
