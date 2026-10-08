namespace Cadenza.Core;

public sealed record Track
{
    // WinUI's metadata generator treats init-only setters as regular setters.
    // Read-only properties keep the model immutable and safe for XAML binding.
    public string Id { get; }
    public string Name { get; }
    public string Artist { get; }
    public string ArtistId { get; }
    public string Album { get; }
    public int DurationMs { get; }
    public string Url { get; }
    public bool Explicit { get; }
    public string ArtworkUrl { get; }
    public Track(string id, string name, string artist, string artistId,
        string album, int durationMs, string url, bool explicitContent, string artworkUrl = "")
    {
        Id = id; Name = name; Artist = artist; ArtistId = artistId;
        Album = album; DurationMs = durationMs; Url = url; Explicit = explicitContent; ArtworkUrl = artworkUrl;
    }
    public string Duration => TimeSpan.FromMilliseconds(DurationMs).ToString(@"m\:ss");
}
public sealed record Playlist
{
    public string Id { get; }
    public string Name { get; }
    public int Count { get; }
    public string ArtworkUrl { get; }
    public string Owner { get; }
    public string Subtitle => $"{Count} tracks" + (Owner.Length > 0 ? $" · {Owner}" : "");
    public Playlist(string id, string name, int count, string artworkUrl = "", string owner = "")
    { Id = id; Name = name; Count = count; ArtworkUrl = artworkUrl; Owner = owner; }
}
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
