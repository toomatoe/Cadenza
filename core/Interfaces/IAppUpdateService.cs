using Cadenza.Core.Models;

namespace Cadenza.Core.Interfaces;

/// <summary>Checks published releases on demand and downloads a checksum-verified installer.</summary>
public interface IAppUpdateService
{
    Task<AppUpdate?> CheckAsync(string currentVersion, bool includePrereleases, CancellationToken cancellationToken = default);

    /// <summary>Streams into the supplied app-owned directory; returns the verified installer path.</summary>
    Task<string> DownloadAsync(AppUpdate update, string directory, CancellationToken cancellationToken = default);
}
