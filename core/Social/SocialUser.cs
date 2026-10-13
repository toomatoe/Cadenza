namespace Cadenza.Core.Social;

/// <summary>A Cadenza Friends account as other people see it. Unrelated to the Spotify account.</summary>
public sealed record SocialUser
{
    // Get-only properties set by the constructor, like Track: WinUI's metadata generator treats
    // init-only setters as regular setters. System.Text.Json binds through the constructor.
    public SocialUser(string id, string username, string displayName)
    {
        Id = id;
        Username = username;
        DisplayName = displayName;
    }

    public string Id { get; }

    public string Username { get; }

    public string DisplayName { get; }
}
