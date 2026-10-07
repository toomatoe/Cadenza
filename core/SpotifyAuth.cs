using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ApurvaSpotify.Core;

public static class Pkce
{
    public static string RandomValue() => Base64Url(RandomNumberGenerator.GetBytes(32));
    public static string Challenge(string verifier) => Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
    private static string Base64Url(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    public static string ReadCode(Uri callback, string expectedState)
    {
        var pairs = callback.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('=', 2)).ToArray();
        if (pairs.GroupBy(p => p[0]).Any(g => g.Count() > 1))
            throw new InvalidOperationException("Duplicate authorization parameters.");
        var fields = pairs.ToDictionary(p => Uri.UnescapeDataString(p[0]),
            p => p.Length == 2 ? Uri.UnescapeDataString(p[1].Replace('+', ' ')) : "");
        if (!fields.TryGetValue("state", out var state) ||
            !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(state), Encoding.UTF8.GetBytes(expectedState)))
            throw new InvalidOperationException("Authorization state did not match. Try connecting again.");
        if (fields.ContainsKey("error")) throw new InvalidOperationException("Spotify authorization was declined.");
        if (!fields.TryGetValue("code", out var code) || string.IsNullOrWhiteSpace(code))
            throw new InvalidOperationException("Spotify did not provide an authorization code.");
        return code;
    }
}

public sealed class SpotifyAuth(HttpClient http, ITokenStore store)
{
    public const string RedirectUri = "http://127.0.0.1:8888/callback";
    private const string TokenUrl = "https://accounts.spotify.com/api/token";
    private readonly SemaphoreSlim refreshGate = new(1, 1);
    private Tokens? tokens;
    private string clientId = "";
    public bool IsConnected => tokens is not null;
    public string ClientId => tokens?.ClientId ?? "";

    public static string ValidateClientId(string value)
    {
        value = value.Trim();
        if (value.Length != 32 || value.Any(c => !Uri.IsHexDigit(c)))
            throw new ArgumentException("Enter the 32-character Client ID from your Spotify developer app.");
        return value;
    }

    public async Task RestoreAsync(string value, CancellationToken ct)
    {
        clientId = ValidateClientId(value);
        var saved = await store.LoadAsync(clientId, ct);
        tokens = saved?.ClientId == clientId ? saved : null;
    }

    public async Task ConnectAsync(string value, CancellationToken ct)
    {
        var newClientId = ValidateClientId(value);
        var verifier = Pkce.RandomValue();
        var state = Pkce.RandomValue();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromMinutes(3));
        using var listener = new HttpListener();
        listener.Prefixes.Add("http://127.0.0.1:8888/");
        try { listener.Start(); }
        catch (HttpListenerException) { throw new InvalidOperationException("Sign-in port 8888 is busy. Close another sign-in attempt and retry."); }
        var parameters = new Dictionary<string, string>
        {
            ["client_id"] = newClientId, ["response_type"] = "code", ["redirect_uri"] = RedirectUri,
            ["code_challenge_method"] = "S256", ["code_challenge"] = Pkce.Challenge(verifier),
            ["state"] = state, ["scope"] = "user-library-read playlist-read-private playlist-read-collaborative streaming"
        };
        var url = "https://accounts.spotify.com/authorize?" + string.Join('&',
            parameters.Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value)}"));
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        string code;
        while (true)
        {
            var context = await listener.GetContextAsync().WaitAsync(timeout.Token);
            var callback = context.Request.Url;
            if (context.Request.HttpMethod != "GET" || callback?.AbsolutePath != "/callback")
            {
                context.Response.StatusCode = 404;
                context.Response.Close();
                continue;
            }
            try { code = Pkce.ReadCode(callback, state); }
            catch (InvalidOperationException)
            {
                context.Response.StatusCode = 400;
                context.Response.Close();
                throw;
            }
            var body = Encoding.UTF8.GetBytes("Sign-in received. You can close this tab and return to ApurvaSpotify.");
            context.Response.ContentType = "text/plain; charset=utf-8";
            context.Response.Headers["Cache-Control"] = "no-store";
            context.Response.Headers["Referrer-Policy"] = "no-referrer";
            context.Response.ContentLength64 = body.Length;
            await context.Response.OutputStream.WriteAsync(body, timeout.Token);
            context.Response.Close();
            break;
        }
        listener.Stop();
        var result = await ExchangeAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code", ["code"] = code,
            ["redirect_uri"] = RedirectUri, ["client_id"] = newClientId, ["code_verifier"] = verifier
        }, newClientId, "", timeout.Token);
        await store.SaveAsync(result, timeout.Token);
        clientId = newClientId;
        tokens = result;
    }

    public async Task<string> GetAccessTokenAsync(CancellationToken ct, bool forceRefresh = false)
    {
        await refreshGate.WaitAsync(ct);
        try
        {
            var current = tokens ?? throw new InvalidOperationException("Connect your Spotify account in Settings first.");
            if (!forceRefresh && current.ExpiresAt > DateTimeOffset.UtcNow.AddSeconds(60)) return current.AccessToken;
            var refreshed = await ExchangeAsync(new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token", ["refresh_token"] = current.RefreshToken, ["client_id"] = clientId
            }, clientId, current.RefreshToken, ct);
            await store.SaveAsync(refreshed, ct);
            tokens = refreshed;
            return refreshed.AccessToken;
        }
        finally { refreshGate.Release(); }
    }

    public async Task DisconnectAsync(CancellationToken ct)
    {
        await refreshGate.WaitAsync(ct);
        try
        {
            if (clientId.Length > 0) await store.DeleteAsync(clientId, ct);
            tokens = null;
        }
        finally { refreshGate.Release(); }
    }

    private async Task<Tokens> ExchangeAsync(Dictionary<string, string> form, string id, string fallbackRefresh, CancellationToken ct)
    {
        using var response = await http.PostAsync(TokenUrl, new FormUrlEncodedContent(form), ct);
        if (!response.IsSuccessStatusCode)
            throw new SpotifyApiException("Spotify sign-in expired or was rejected. Connect again in Settings.", (int)response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var root = json.RootElement;
        var refresh = root.TryGetProperty("refresh_token", out var value) ? value.GetString()! : fallbackRefresh;
        if (string.IsNullOrEmpty(refresh)) throw new InvalidOperationException("Spotify did not return a refresh token.");
        return new Tokens(root.GetProperty("access_token").GetString()!, refresh,
            DateTimeOffset.UtcNow.AddSeconds(root.GetProperty("expires_in").GetInt32()), id);
    }
}
