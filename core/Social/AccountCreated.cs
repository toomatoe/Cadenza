namespace Cadenza.Core.Social;

/// <summary>The new account and its device token. The token is shown once; the server keeps only a hash.</summary>
public sealed record AccountCreated(SocialUser User, string Token);
