using Cadenza.Core.Interfaces;
using Cadenza.Core.Models;

namespace Cadenza.UnitTests.Fakes;

/// <summary>Returns the configured tracks and playlists and remembers the last request.</summary>
internal sealed class FakeCatalog : IMusicCatalog
{
    public IReadOnlyList<Track> Tracks { get; set; } = [];

    public IReadOnlyList<Playlist> Playlists { get; set; } = [];

    public Action? BeforeReturn { get; set; }

    public int Calls { get; private set; }

    public int LastOffset { get; private set; }

    public string LastValue { get; private set; } = "";

    public TrackSource LastSource { get; private set; }

    public Task<Page<Track>> SearchAsync(string query, int offset, CancellationToken cancellationToken) =>
        TrackPage(TrackSource.Search, query, offset);

    public Task<Page<Track>> SavedTracksAsync(int offset, CancellationToken cancellationToken) =>
        TrackPage(TrackSource.SavedLibrary, "", offset);

    public Task<Page<Track>> PlaylistTracksAsync(string id, int offset, CancellationToken cancellationToken) =>
        TrackPage(TrackSource.Playlist, id, offset);

    public Task<Page<Playlist>> PlaylistsAsync(int offset, CancellationToken cancellationToken)
    {
        Calls++;
        LastOffset = offset;
        BeforeReturn?.Invoke();

        return Task.FromResult(new Page<Playlist>(Playlists, offset, true));
    }

    private Task<Page<Track>> TrackPage(TrackSource source, string value, int offset)
    {
        Calls++;
        LastSource = source;
        LastValue = value;
        LastOffset = offset;
        BeforeReturn?.Invoke();

        return Task.FromResult(new Page<Track>(Tracks, offset, true));
    }
}
