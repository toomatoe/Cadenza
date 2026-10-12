namespace Cadenza.Core.Models;

public record Tokens(string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt, string ClientId);
