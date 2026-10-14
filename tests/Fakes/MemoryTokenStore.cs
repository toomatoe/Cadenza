using Cadenza.Core.Models;
using Cadenza.Core.Services.Contracts;

namespace Cadenza.UnitTests.Fakes;

/// <summary>Keeps saved Spotify tokens in memory instead of Windows Credential Manager.</summary>
internal sealed class MemoryTokenStore : ITokenStore
{
    public MemoryTokenStore(Tokens? initial)
    {
        Value = initial;
    }

    public Tokens? Value { get; private set; }

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
