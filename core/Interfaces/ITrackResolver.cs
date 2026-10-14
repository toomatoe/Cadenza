using Cadenza.Core.Models;

namespace Cadenza.Core.Interfaces;

/// <summary>Turns an artist name into playable tracks.</summary>
public interface ITrackResolver
{
    Task<IReadOnlyList<Track>> FindTracksAsync(string artistName, CancellationToken cancellationToken);
}
