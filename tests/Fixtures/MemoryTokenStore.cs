using Cadenza.Core.Models;
using Cadenza.Core.Services.Contracts;

namespace Cadenza.Tests.Fixtures;

internal sealed class MemoryTokenStore(Tokens? initial) : ITokenStore
{
    public Tokens? Value = initial;
    public Task<Tokens?> LoadAsync(string clientId, CancellationToken ct) => Task.FromResult(Value);
    public Task SaveAsync(Tokens value, CancellationToken ct) { Value = value; return Task.CompletedTask; }
    public Task DeleteAsync(string clientId, CancellationToken ct) { Value = null; return Task.CompletedTask; }
}
