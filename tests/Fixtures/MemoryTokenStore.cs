using Cadenza.Core.Models;
using Cadenza.Core.Services.Contracts;

namespace Cadenza.Tests.Fixtures;

internal sealed class MemoryTokenStore(Tokens? initial) : ITokenStore
{
    public Tokens? Value { get; private set; } = initial;

    public Task<Tokens?> LoadAsync(string clientId, CancellationToken cancellationToken) => Task.FromResult(Value);

    public Task SaveAsync(Tokens tokens, CancellationToken cancellationToken)
    {
        Value = tokens;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string clientId, CancellationToken cancellationToken)
    {
        Value = null;
        return Task.CompletedTask;
    }
}
