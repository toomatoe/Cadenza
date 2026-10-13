namespace Cadenza.Core.Models;

/// <summary>One page of results from a paged Spotify endpoint.</summary>
/// <param name="Items">The items on this page.</param>
/// <param name="Offset">Index of the first item within the full result set.</param>
/// <param name="HasMore">Whether another page follows.</param>
public sealed record Page<T>(IReadOnlyList<T> Items, int Offset, bool HasMore);
