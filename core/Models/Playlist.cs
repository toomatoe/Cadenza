namespace Cadenza.Core.Models;

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
