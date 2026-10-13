using System.Security.Cryptography;
using System.Text;

namespace Cadenza.Server;

/// <summary>Creates device tokens and invite codes. Only SHA-256 hashes of tokens are stored.</summary>
public static class TokenHasher
{
    private const int TokenBytes = 32;

    // No 0/O or 1/I/L, so codes read aloud or typed from a message don't get mistaken.
    private const string InviteAlphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
    private const int InviteHalfLength = 5;

    public static string NewToken() => Base64Url(RandomNumberGenerator.GetBytes(TokenBytes));

    public static string Hash(string token) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    public static string NewInviteCode() =>
        $"{RandomNumberGenerator.GetString(InviteAlphabet, InviteHalfLength)}-{RandomNumberGenerator.GetString(InviteAlphabet, InviteHalfLength)}";

    /// <summary>Uppercases and trims a typed code so "abcde-fghjk " still matches.</summary>
    public static string NormalizeInvite(string code) => code.Trim().ToUpperInvariant();

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
