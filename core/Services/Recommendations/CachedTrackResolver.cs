using System.Text.Json;
using Cadenza.Core.Interfaces;
using Cadenza.Core.Models;
using Cadenza.Core.Models.Recommendations;

namespace Cadenza.Core.Services.Recommendations;

/// <summary>
/// Remembers each artist's tracks so a refresh doesn't repeat ~20 Spotify searches. Entries last
/// <see cref="Lifetime"/>; the file holds at most <see cref="MaxArtists"/> artists (oldest dropped).
/// </summary>
public sealed class CachedTrackResolver : ITrackResolver
{
    public const int MaxArtists = 400;
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(7);

    private readonly ITrackResolver _inner;
    private readonly string? _path;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Lock _lock = new();
    private Dictionary<string, Entry> _entries = [];

    /// <param name="path">JSON cache file, or null to cache in memory only.</param>
    public CachedTrackResolver(ITrackResolver inner, string? path, Func<DateTimeOffset>? clock = null)
    {
        _inner = inner;
        _path = path;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        Load();
    }

    public async Task<IReadOnlyList<Track>> FindTracksAsync(string artistName, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(artistName);
        var key = TasteProfile.Key(artistName);
        lock (_lock)
        {
            if (_entries.TryGetValue(key, out var cached) && _clock() - cached.Fetched < Lifetime)
                return cached.Tracks.Select(record => record.ToTrack()).ToArray();
        }

        var tracks = await _inner.FindTracksAsync(artistName, cancellationToken);
        lock (_lock)
        {
            _entries[key] = new Entry(_clock(), tracks.Select(TrackRecord.From).ToArray());
            if (_entries.Count > MaxArtists)
            {
                foreach (var stale in _entries.OrderBy(pair => pair.Value.Fetched).Take(_entries.Count - MaxArtists).Select(pair => pair.Key).ToArray())
                {
                    _entries.Remove(stale);
                }
            }

            Save();
        }

        return tracks;
    }

    public void Clear()
    {
        lock (_lock)
        {
            _entries = [];
            if (_path is not null && File.Exists(_path))
                File.Delete(_path);
        }
    }

    private void Load()
    {
        if (_path is null || !File.Exists(_path))
            return;

        try
        {
            _entries = JsonSerializer.Deserialize<Dictionary<string, Entry>>(File.ReadAllText(_path)) ?? [];
        }
        catch (Exception error) when (error is IOException or JsonException)
        {
            _entries = [];
        }
    }

    private void Save()
    {
        if (_path is null)
            return;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temporary = _path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(_entries));
            File.Move(temporary, _path, overwrite: true);
        }
        catch (IOException)
        {
            // Only an optimisation; the next refresh searches again.
        }
    }

    private sealed class Entry
    {
        public Entry(DateTimeOffset fetched, TrackRecord[] tracks)
        {
            Fetched = fetched;
            Tracks = tracks;
        }

        public DateTimeOffset Fetched { get; }

        public TrackRecord[] Tracks { get; }
    }}
