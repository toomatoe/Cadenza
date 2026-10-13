namespace Cadenza.Core.Social;

public sealed record SendMessageRequest(ChatMessageKind Kind, string Text, string? TrackId);
