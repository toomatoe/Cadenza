namespace Cadenza.Core.Models;

public sealed record Tokens(string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt, string ClientId);
