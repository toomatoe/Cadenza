namespace Cadenza.Core.Interfaces;

/// <summary>Spotify OAuth sign-in (PKCE) and token refresh for one client ID.</summary>
public interface ISpotifyAuth : IDisposable
{
    /// <summary>Whether a token set is loaded.</summary>
    bool IsConnected { get; }

    /// <summary>The client ID of the loaded tokens, or empty.</summary>
    string ClientId { get; }

    /// <summary>Loads saved tokens for <paramref name="clientId"/>, if any.</summary>
    Task RestoreAsync(string clientId, CancellationToken cancellationToken);

    /// <summary>Signs in through the browser and saves the tokens.</summary>
    Task ConnectAsync(string clientId, string redirectUri, CancellationToken cancellationToken);

    /// <summary>A valid access token, refreshing it first when it's about to expire (or when <paramref name="forceRefresh"/> is set).</summary>
    Task<string> GetAccessTokenAsync(bool forceRefresh, CancellationToken cancellationToken);

    /// <summary>Forgets and deletes the saved tokens.</summary>
    Task DisconnectAsync(CancellationToken cancellationToken);
}
