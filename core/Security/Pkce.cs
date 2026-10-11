using System.Security.Cryptography;
using System.Text;

namespace Cadenza.Core.Security;

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
