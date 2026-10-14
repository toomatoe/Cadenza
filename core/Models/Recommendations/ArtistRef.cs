namespace Cadenza.Core.Models.Recommendations;

/// <summary>An artist identified by Spotify ID and display name.</summary>
public sealed class ArtistRef
{
    public ArtistRef(string id, string name)
    {
        Id = id;
        Name = name;
    }

    public string Id { get; }

    public string Name { get; }
}
