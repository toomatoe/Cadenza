namespace Cadenza.Core.Social;

/// <summary>Request body for joining with an invite code.</summary>
public sealed record CreateAccountRequest(string InviteCode, string Username, string DisplayName);
