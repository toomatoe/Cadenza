namespace Cadenza.Core.Social;

/// <summary>Accepted friends plus pending requests in both directions.</summary>
public sealed class FriendList
{
    public FriendList(IReadOnlyList<SocialUser> friends, IReadOnlyList<SocialUser> incoming, IReadOnlyList<SocialUser> outgoing)
    {
        Friends = friends;
        Incoming = incoming;
        Outgoing = outgoing;
    }

    public IReadOnlyList<SocialUser> Friends { get; }

    public IReadOnlyList<SocialUser> Incoming { get; }

    public IReadOnlyList<SocialUser> Outgoing { get; }
}

public sealed class FriendRequest
{
    public FriendRequest(string username)
    {
        Username = username;
    }

    public string Username { get; }
}
