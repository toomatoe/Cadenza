namespace Cadenza.Core.Models;

/// <summary>One page of results from a paged Spotify endpoint.</summary>
public sealed class Page<T>
{
    /// <param name="items">The items on this page.</param>
    /// <param name="offset">Index of the first item within the full result set.</param>
    /// <param name="hasMore">Whether another page follows.</param>
    public Page(IReadOnlyList<T> items, int offset, bool hasMore)
    {
        Items = items;
        Offset = offset;
        HasMore = hasMore;
    }

    /// <summary>The items on this page.</summary>
    public IReadOnlyList<T> Items { get; }

    /// <summary>Index of the first item within the full result set.</summary>
    public int Offset { get; }

    /// <summary>Whether another page follows.</summary>
    public bool HasMore { get; }
}
