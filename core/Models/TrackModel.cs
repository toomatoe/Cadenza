namespace Cadenza.Core.Models;

public record Track
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
