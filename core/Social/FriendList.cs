namespace Cadenza.Core.Social;

/// <summary>Accepted friends plus pending requests in both directions.</summary>
public sealed record FriendList(
    IReadOnlyList<SocialUser> Friends,
    IReadOnlyList<SocialUser> Incoming,
    IReadOnlyList<SocialUser> Outgoing);
