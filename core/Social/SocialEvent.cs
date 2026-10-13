namespace Cadenza.Core.Social;

/// <summary>A real-time notification pushed over the WebSocket.</summary>
/// <param name="Type">One of the <see cref="SocialEvent"/> type constants.</param>
/// <param name="Message">The new message, for <see cref="MessageType"/>.</param>
/// <param name="User">The other person, for friend events.</param>
public sealed record SocialEvent(string Type, ChatMessage? Message = null, SocialUser? User = null)
{
    public const string MessageType = "message";
    public const string FriendRequestType = "friend-request";
    public const string FriendAcceptedType = "friend-accepted";
    public const string FriendRemovedType = "friend-removed";
}
