using System.Security.Cryptography;
using System.Text;

namespace Cadenza.Core.Security;

/// <summary>Helpers for the OAuth authorization-code flow with PKCE (RFC 7636).</summary>
public static class Pkce
{
    private const int RandomByteCount = 32;

    /// <summary>A URL-safe random value for the code verifier or state parameter.</summary>
    public static string RandomValue() => Base64Url(RandomNumberGenerator.GetBytes(RandomByteCount));

    /// <summary>The S256 code challenge for <paramref name="verifier"/>.</summary>
    public static string Challenge(string verifier) => Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

    /// <summary>Validates the redirect's state and returns its authorization code.</summary>
    /// <exception cref="InvalidOperationException">The callback is malformed, declined or forged.</exception>
    public static string ReadCode(Uri callback, string expectedState)
    {
        ArgumentNullException.ThrowIfNull(callback);
        var pairs = callback.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .ToArray();
        if (pairs.GroupBy(pair => pair[0]).Any(group => group.Count() > 1))
            throw new InvalidOperationException("Duplicate authorization parameters.");

        var fields = pairs.ToDictionary(
            pair => Uri.UnescapeDataString(pair[0]),
            pair => pair.Length == 2 ? Uri.UnescapeDataString(pair[1].Replace('+', ' ')) : "");

        // Constant-time comparison so the state value cannot be probed by timing.
        if (!fields.TryGetValue("state", out var state) ||
            !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(state), Encoding.UTF8.GetBytes(expectedState)))
            throw new InvalidOperationException("Authorization state did not match. Try connecting again.");

        if (fields.ContainsKey("error"))
            throw new InvalidOperationException("Spotify authorization was declined.");

        if (!fields.TryGetValue("code", out var code) || string.IsNullOrWhiteSpace(code))
            throw new InvalidOperationException("Spotify did not provide an authorization code.");

        return code;
    }

    private static string Base64Url(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
