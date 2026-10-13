using System.Text.Json;

namespace Cadenza.Core.Recommendations;

/// <summary>
/// Cadenza's local listening events and recently suggested tracks, kept in one JSON file on this
/// device. Bounded so it never grows without limit; never uploaded.
/// </summary>
public sealed class ListeningHistoryStore
{
    public const int MaxEvents = 2000;
    public const int RememberedRefreshes = 3;

    private readonly string? _path;
    private State _state = new();

    /// <param name="path">JSON file, or null to keep history in memory only.</param>
    public ListeningHistoryStore(string? path)
    {
        _path = path;
        Load();
    }

    public IReadOnlyList<ListeningEvent> Events => _state.Events;

    /// <summary>Track IDs shown in the last few refreshes.</summary>
    public IReadOnlySet<string> RecentlySuggested => _state.Suggestions.SelectMany(ids => ids).ToHashSet(StringComparer.Ordinal);

    public void Record(ListeningEvent listeningEvent)
    {
        ArgumentNullException.ThrowIfNull(listeningEvent);
        _state.Events.Add(listeningEvent);
        if (_state.Events.Count > MaxEvents)
        {
            _state.Events.RemoveRange(0, _state.Events.Count - MaxEvents);
        }

        Save();
    }

    public void RememberSuggestions(IEnumerable<string> trackIds)
    {
        _state.Suggestions.Add(trackIds.ToArray());
        if (_state.Suggestions.Count > RememberedRefreshes)
        {
            _state.Suggestions.RemoveAt(0);
        }

        Save();
    }

    /// <summary>Deletes all local listening history, for example on disconnect.</summary>
    public void Clear()
    {
        _state = new State();
        Save();
    }

    private void Load()
    {
        if (_path is null || !File.Exists(_path))
        {
            return;
        }

        try
        {
            _state = JsonSerializer.Deserialize<State>(File.ReadAllText(_path)) ?? new State();
        }
        catch (Exception error) when (error is IOException or JsonException)
        {
            _state = new State();
        }
    }

    private void Save()
    {
        if (_path is null)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temporary = _path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(_state));
            File.Move(temporary, _path, overwrite: true);
        }
        catch (IOException)
        {
            // History improves recommendations but isn't essential; keep working in memory.
        }
    }

    private sealed class State
    {
        public List<ListeningEvent> Events { get; init; } = [];

        public List<string[]> Suggestions { get; init; } = [];
    }
}
