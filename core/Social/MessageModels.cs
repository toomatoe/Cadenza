namespace Cadenza.Core.Social;

public enum ChatMessageKind
{
    /// <summary>Plain text.</summary>
    Text,

    /// <summary>A track shared as a card the friend can play or queue.</summary>
    Share,

    /// <summary>A track offered for the friend's Up next.</summary>
    Gift
}

/// <summary>
/// A one-to-one message. Shares and gifts carry only a Spotify track ID; each client fetches the
/// track's name and artwork through its own Spotify session, so the server stores no Spotify content.
/// </summary>
public sealed class ChatMessage
{
    public ChatMessage(long id, string fromId, string toId, ChatMessageKind kind, string text, string? trackId, DateTimeOffset sentAt)
    {
        Id = id;
        FromId = fromId;
        ToId = toId;
        Kind = kind;
        Text = text;
        TrackId = trackId;
        SentAt = sentAt;
    }

    public long Id { get; }

    public string FromId { get; }

    public string ToId { get; }

    public ChatMessageKind Kind { get; }

    public string Text { get; }

    public string? TrackId { get; }

    public DateTimeOffset SentAt { get; }
}

public sealed class SendMessageRequest
{
    public SendMessageRequest(ChatMessageKind kind, string text, string? trackId)
    {
        Kind = kind;
        Text = text;
        TrackId = trackId;
    }

    public ChatMessageKind Kind { get; }

    public string Text { get; }

    public string? TrackId { get; }
}

public sealed class ReportRequest
{
    public ReportRequest(long messageId, string reason)
    {
        MessageId = messageId;
        Reason = reason;
    }

    public long MessageId { get; }

    public string Reason { get; }
}

/// <summary>A real-time notification pushed over the WebSocket.</summary>
public sealed class SocialEvent
{
    public SocialEvent(string type, ChatMessage? message = null, SocialUser? user = null)
    {
        Type = type;
        Message = message;
        User = user;
    }

    public string Type { get; }

    public ChatMessage? Message { get; }

    public SocialUser? User { get; }

    public const string MessageType = "message";
    public const string FriendRequestType = "friend-request";
    public const string FriendAcceptedType = "friend-accepted";
    public const string FriendRemovedType = "friend-removed";
}

/// <summary>Error body returned by the server for any rejected request.</summary>
public sealed class SocialError
{
    public SocialError(string error)
    {
        Error = error;
    }

    public string Error { get; }
}
