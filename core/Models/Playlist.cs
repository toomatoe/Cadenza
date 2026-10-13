namespace Cadenza.Core.Models;

/// <summary>A playlist in the user's Spotify library.</summary>
public sealed record Playlist
{
    // Get-only properties for WinUI binding; see the comment on Track.
    public Playlist(string id, string name, int count, string artworkUrl = "", string owner = "")
    {
        Id = id;
        Name = name;
        Count = count;
        ArtworkUrl = artworkUrl;
        Owner = owner;
    }

    public string Id { get; }

    public string Name { get; }

    /// <summary>Number of tracks.</summary>
    public int Count { get; }

    public string ArtworkUrl { get; }

    /// <summary>Owner display name, or empty if Spotify omits it.</summary>
    public string Owner { get; }

    public string Subtitle => Owner.Length > 0 ? $"{Count} tracks · {Owner}" : $"{Count} tracks";
}
