namespace Cadenza.Core.Models;

/// <summary>An OAuth token set for one Spotify client ID.</summary>
public sealed class Tokens
{
    /// <param name="accessToken">Short-lived bearer token.</param>
    /// <param name="refreshToken">Long-lived token used to obtain new access tokens.</param>
    /// <param name="expiresAt">When <paramref name="accessToken"/> expires.</param>
    /// <param name="clientId">The Spotify client ID the tokens were issued to.</param>
    public Tokens(string accessToken, string refreshToken, DateTimeOffset expiresAt, string clientId)
    {
        AccessToken = accessToken;
        RefreshToken = refreshToken;
        ExpiresAt = expiresAt;
        ClientId = clientId;
    }

    /// <summary>Short-lived bearer token.</summary>
    public string AccessToken { get; }

    /// <summary>Long-lived token used to obtain new access tokens.</summary>
    public string RefreshToken { get; }

    /// <summary>When <see cref="AccessToken"/> expires.</summary>
    public DateTimeOffset ExpiresAt { get; }

    /// <summary>The Spotify client ID the tokens were issued to.</summary>
    public string ClientId { get; }
}
