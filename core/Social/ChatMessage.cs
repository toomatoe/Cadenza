namespace Cadenza.Core.Social;

/// <summary>
/// A one-to-one message. Shares and gifts carry only a Spotify track ID; each client fetches the
/// track's name and artwork through its own Spotify session, so the server stores no Spotify content.
/// </summary>
public sealed record ChatMessage(
    long Id,
    string FromId,
    string ToId,
    ChatMessageKind Kind,
    string Text,
    string? TrackId,
    DateTimeOffset SentAt);
