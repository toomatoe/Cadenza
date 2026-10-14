using Cadenza.Core.Models;
using Cadenza.Core.Models.Social;

namespace Cadenza.Core.Interfaces;

/// <summary>The shared Cadenza library on the Friends server: adding songs, finding them, and getting a file to play.</summary>
public interface ISharedLibraryService
{
    /// <summary>Library songs matching <paramref name="query"/>, as tracks for the search list.</summary>
    Task<IReadOnlyList<Track>> SearchAsync(ISocialClient client, string query, CancellationToken cancellationToken);

    /// <summary>A local file for a library track, downloading it into the bounded cache first if needed.</summary>
    Task<string> GetPlayableFileAsync(ISocialClient client, Track track, CancellationToken cancellationToken);

    /// <summary>Reads each file's tags and uploads it; files that aren't supported music, or that the server refuses, are skipped.</summary>
    /// <param name="progress">Receives "Adding 2 of 5: name…" messages.</param>
    /// <exception cref="Exceptions.SocialException">The sign-in ended (other server errors only skip that file).</exception>
    Task<UploadSummary> UploadAsync(ISocialClient client, IReadOnlyList<string> paths, IProgress<string>? progress, CancellationToken cancellationToken);

    /// <summary>Deletes downloaded songs (when leaving Friends).</summary>
    void ClearCache();
}
