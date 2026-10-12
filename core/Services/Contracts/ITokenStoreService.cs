using Cadenza.Core.Models;

namespace Cadenza.Core.Services.Contracts;

public interface ITokenStore
{
    Task<Tokens?> LoadAsync(string clientId, CancellationToken cancellationToken);
    Task SaveAsync(Tokens tokens, CancellationToken cancellationToken);
    Task DeleteAsync(string clientId, CancellationToken cancellationToken);
}
