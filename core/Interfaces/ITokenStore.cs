using Cadenza.Core.Models;

namespace Cadenza.Core.Interfaces;

/// <summary>Persists OAuth tokens per Spotify client ID.</summary>
public interface ITokenStore
{
    /// <returns>The saved tokens, or <see langword="null"/> if none are stored.</returns>
    Task<Tokens?> LoadAsync(string clientId, CancellationToken cancellationToken);

    Task SaveAsync(Tokens tokens, CancellationToken cancellationToken);

    Task DeleteAsync(string clientId, CancellationToken cancellationToken);
}
