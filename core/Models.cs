namespace ApurvaSpotify.Core;

public sealed record Track(string Id, string Name, string Artist, string ArtistId,
    string Album, int DurationMs, string Url, bool Explicit)
{
    public string Duration => TimeSpan.FromMilliseconds(DurationMs).ToString(@"m\:ss");
}
public sealed record Playlist(string Id, string Name, int Count);
public sealed record Page<T>(IReadOnlyList<T> Items, int Offset, bool HasMore);
public sealed record Tokens(string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt, string ClientId);

public interface ITokenStore
{
    Task<Tokens?> LoadAsync(string clientId, CancellationToken cancellationToken);
    Task SaveAsync(Tokens tokens, CancellationToken cancellationToken);
    Task DeleteAsync(string clientId, CancellationToken cancellationToken);
}

public sealed class SpotifyApiException(string message, int statusCode) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}
