namespace Cadenza.Core.Social;

/// <summary>A single-use invite code for a friend to join.</summary>
public sealed record InviteCreated(string Code, DateTimeOffset ExpiresAt);
