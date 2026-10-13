namespace Cadenza.Core.Models;

/// <summary>An OAuth token set for one Spotify client ID.</summary>
/// <param name="AccessToken">Short-lived bearer token.</param>
/// <param name="RefreshToken">Long-lived token used to obtain new access tokens.</param>
/// <param name="ExpiresAt">When <paramref name="AccessToken"/> expires.</param>
/// <param name="ClientId">The Spotify client ID the tokens were issued to.</param>
public sealed record Tokens(string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt, string ClientId);
