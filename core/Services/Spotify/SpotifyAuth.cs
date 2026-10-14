using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using Cadenza.Core.Exceptions;
using Cadenza.Core.Interfaces;
using Cadenza.Core.Models;

namespace Cadenza.Core.Services.Spotify;

/// <summary>
/// Spotify OAuth sign-in (authorization code with PKCE, no client secret) and token refresh for one
/// client ID. Tokens are persisted through <see cref="ITokenStore"/>.
/// </summary>
/// <param name="http">Shared HTTP client.</param>
/// <param name="store">Where tokens are saved between sessions.</param>
/// <param name="scope">Space-separated OAuth scopes to request.</param>
public sealed class SpotifyAuth(HttpClient http, ITokenStore store, string scope = SpotifyAuth.LibraryScope) : IDisposable
{
    public const string RedirectUri = "http://127.0.0.1:8888/callback";
    public const string LibraryScope =
        "user-library-read playlist-read-private playlist-read-collaborative streaming user-top-read user-read-recently-played";

    // Spotify's streaming servers reject tokens issued to third-party Web API apps. Playback signs in
    // separately with the desktop client ID librespot is built for, on librespot's loopback redirect.
    public const string PlaybackClientId = "65b708073fc0480ea92a077233ca87bd";
    public const string PlaybackRedirectUri = "http://127.0.0.1:8898/login";
    public const string PlaybackScope =
        "streaming user-read-private user-read-email user-read-playback-state user-modify-playback-state user-library-read";

    private const string AuthorizeUrl = "https://accounts.spotify.com/authorize";
    private const string TokenUrl = "https://accounts.spotify.com/api/token";
    private const int ClientIdLength = 32;
    private static readonly TimeSpan _signInTimeout = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan _refreshMargin = TimeSpan.FromSeconds(60);

    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private Tokens? _tokens;
    private string _clientId = "";

    public bool IsConnected => _tokens is not null;

    /// <summary>The client ID of the current tokens, or empty when signed out.</summary>
    public string ClientId => _tokens?.ClientId ?? "";

    /// <returns>The trimmed client ID.</returns>
    /// <exception cref="ArgumentException">The value is not a 32-character hexadecimal client ID.</exception>
    public static string ValidateClientId(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        value = value.Trim();
        if (value.Length != ClientIdLength || value.Any(c => !Uri.IsHexDigit(c)))
            throw new ArgumentException("Enter the 32-character Client ID from your Spotify developer app.", nameof(value));

        return value;
    }

    /// <exception cref="ArgumentException">The value is not a plain http://127.0.0.1 loopback URL.</exception>
    public static Uri ValidateRedirectUri(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttp ||
            uri.Host != "127.0.0.1" ||
            uri.Port < 1 ||
            uri.UserInfo.Length > 0 ||
            uri.Query.Length > 0 ||
            uri.Fragment.Length > 0)
        {
            throw new ArgumentException(
                "Use an HTTP loopback URL such as http://127.0.0.1:8888/callback, with no query or fragment.",
                nameof(value));
        }

        return uri;
    }

    /// <summary>Loads saved tokens for <paramref name="clientId"/>, if any.</summary>
    public async Task RestoreAsync(string clientId, CancellationToken cancellationToken)
    {
        _clientId = ValidateClientId(clientId);
        var saved = await store.LoadAsync(_clientId, cancellationToken);
        _tokens = saved?.ClientId == _clientId ? saved : null;
    }

    /// <summary>
    /// Opens the Spotify consent page in the system browser and waits up to three minutes for the
    /// redirect on a temporary loopback listener, then exchanges the code for tokens.
    /// </summary>
    public async Task ConnectAsync(string clientId, CancellationToken cancellationToken, string redirectUri = RedirectUri)
    {
        var newClientId = ValidateClientId(clientId);
        var redirect = ValidateRedirectUri(redirectUri);
        redirectUri = redirect.AbsoluteUri;
        var verifier = Pkce.RandomValue();
        var state = Pkce.RandomValue();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_signInTimeout);

        using var listener = new HttpListener();
        listener.Prefixes.Add(redirect.GetLeftPart(UriPartial.Authority) + "/");
        try
        {
            listener.Start();
        }
        catch (HttpListenerException error)
        {
            throw new InvalidOperationException(
                $"Could not open sign-in port {redirect.Port}. Check that it is available and permitted, then retry.",
                error);
        }

        var parameters = new Dictionary<string, string>
        {
            ["client_id"] = newClientId,
            ["response_type"] = "code",
            ["redirect_uri"] = redirectUri,
            ["code_challenge_method"] = "S256",
            ["code_challenge"] = Pkce.Challenge(verifier),
            ["state"] = state,
            ["scope"] = scope
        };
        var url = AuthorizeUrl + "?" + string.Join('&', parameters.Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value)}"));
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

        var code = await ReceiveCodeAsync(listener, redirect, state, timeout.Token);
        listener.Stop();
        var result = await ExchangeAsync(
            new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["redirect_uri"] = redirectUri,
                ["client_id"] = newClientId,
                ["code_verifier"] = verifier
            },
            newClientId,
            fallbackRefreshToken: "",
            timeout.Token);
        await store.SaveAsync(result, timeout.Token);
        _clientId = newClientId;
        _tokens = result;
    }

    /// <summary>Returns a valid access token, refreshing it when it is about to expire.</summary>
    /// <exception cref="InvalidOperationException">Not signed in.</exception>
    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken, bool forceRefresh = false)
    {
        await _refreshGate.WaitAsync(cancellationToken);
        try
        {
            var current = _tokens ?? throw new InvalidOperationException("Connect your Spotify account in Settings first.");
            if (!forceRefresh && current.ExpiresAt > DateTimeOffset.UtcNow + _refreshMargin)
                return current.AccessToken;

            var refreshed = await ExchangeAsync(
                new Dictionary<string, string>
                {
                    ["grant_type"] = "refresh_token",
                    ["refresh_token"] = current.RefreshToken,
                    ["client_id"] = _clientId
                },
                _clientId,
                current.RefreshToken,
                cancellationToken);
            await store.SaveAsync(refreshed, cancellationToken);
            _tokens = refreshed;
            return refreshed.AccessToken;
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    /// <summary>Deletes saved tokens and signs out.</summary>
    public async Task DisconnectAsync(CancellationToken cancellationToken)
    {
        await _refreshGate.WaitAsync(cancellationToken);
        try
        {
            if (_clientId.Length > 0)
                await store.DeleteAsync(_clientId, cancellationToken);

            _tokens = null;
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    public void Dispose() => _refreshGate.Dispose();

    private static async Task<string> ReceiveCodeAsync(HttpListener listener, Uri redirect, string state, CancellationToken cancellationToken)
    {
        while (true)
        {
            var context = await listener.GetContextAsync().WaitAsync(cancellationToken);
            var callback = context.Request.Url;

            // Ignore stray requests such as favicon fetches.
            if (context.Request.HttpMethod != "GET" || callback?.AbsolutePath != redirect.AbsolutePath)
            {
                context.Response.StatusCode = (int)HttpStatusCode.NotFound;
                context.Response.Close();
                continue;
            }

            string code;
            try
            {
                code = Pkce.ReadCode(callback, state);
            }
            catch (InvalidOperationException)
            {
                context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                context.Response.Close();
                throw;
            }

            var body = Encoding.UTF8.GetBytes("Sign-in received. You can close this tab and return to Cadenza.");
            context.Response.ContentType = "text/plain; charset=utf-8";
            context.Response.Headers["Cache-Control"] = "no-store";
            context.Response.Headers["Referrer-Policy"] = "no-referrer";
            context.Response.ContentLength64 = body.Length;
            await context.Response.OutputStream.WriteAsync(body, cancellationToken);
            context.Response.Close();
            return code;
        }
    }

    /// <param name="fallbackRefreshToken">Kept when Spotify does not rotate the refresh token.</param>
    private async Task<Tokens> ExchangeAsync(
        Dictionary<string, string> form, string clientId, string fallbackRefreshToken, CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(form);
        using var response = await http.PostAsync(new Uri(TokenUrl), content, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new SpotifyApiException(
                "Spotify sign-in expired or was rejected. Connect again in Settings.", (int)response.StatusCode);
        }

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var root = json.RootElement;
        var refreshToken = root.TryGetProperty("refresh_token", out var value) ? value.GetString()! : fallbackRefreshToken;
        if (string.IsNullOrEmpty(refreshToken))
            throw new InvalidOperationException("Spotify did not return a refresh token.");

        return new Tokens(
            root.GetProperty("access_token").GetString()!,
            refreshToken,
            DateTimeOffset.UtcNow.AddSeconds(root.GetProperty("expires_in").GetInt32()),
            clientId);
    }
}
