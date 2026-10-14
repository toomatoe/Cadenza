using Cadenza.Core.Models.Recommendations;

namespace Cadenza.Core.Interfaces;

/// <summary>Cadenza's local listening events and recently suggested tracks (bounded; never uploaded).</summary>
public interface IListeningHistoryStore
{
    /// <summary>Recorded events, oldest first.</summary>
    IReadOnlyList<ListeningEvent> Events { get; }

    /// <summary>Track IDs shown in the last few refreshes.</summary>
    IReadOnlySet<string> RecentlySuggested { get; }

    /// <summary>Adds an event, dropping the oldest once the store is full.</summary>
    void Record(ListeningEvent listeningEvent);

    /// <summary>Remembers one refresh's suggestions so the next refreshes push them down.</summary>
    void RememberSuggestions(IEnumerable<string> trackIds);

    /// <summary>Forgets everything.</summary>
    void Clear();
}
