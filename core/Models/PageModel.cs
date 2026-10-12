namespace Cadenza.Core.Models;

public record Page<T>(IReadOnlyList<T> Items, int Offset, bool HasMore);
