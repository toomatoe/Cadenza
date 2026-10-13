namespace Cadenza.Core.Social;

/// <summary>Error body returned by the server for any rejected request.</summary>
public sealed record SocialError(string Error);
