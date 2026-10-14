using Cadenza.Core.Models;
using Cadenza.Core.Models.Recommendations;

namespace Cadenza.Core.Interfaces;

/// <summary>
/// Everything the "For you" page needs: saved suggestions, fresh ones, and the listening signals that
/// feed the taste profile. Created on first use; nothing runs until it's asked to.
/// </summary>
public interface IRecommendationService
{
    /// <summary>The last saved suggestions if they're recent enough to show instantly, otherwise null.</summary>
    (RecommendationResult Result, DateTimeOffset MadeAt)? LoadSaved();

    /// <summary>Builds new suggestions from the listener's Spotify data and local history, and saves them.</summary>
    /// <param name="discovery">0 = stick to familiar artists, 1 = mostly new ones.</param>
    /// <returns>The suggestions, and whether the newer Spotify scopes are missing (a reconnect would improve them).</returns>
    Task<(RecommendationResult Result, bool MissingScopes)> RefreshAsync(double discovery, CancellationToken cancellationToken);

    /// <summary>Records a listening signal (completed, skipped, liked, not interested) for a track.</summary>
    void RecordListening(ListeningEventKind kind, Track track);

    /// <summary>Deletes local history, saved suggestions and caches (on disconnect).</summary>
    void Forget();
}
