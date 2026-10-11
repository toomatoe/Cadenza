namespace Cadenza.Core.Models;

public sealed record Page<T>(IReadOnlyList<T> Items, int Offset, bool HasMore);
