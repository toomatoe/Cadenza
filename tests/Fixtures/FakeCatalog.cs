using Cadenza.Core.Models;
using Cadenza.Core.Services.Contracts;

namespace Cadenza.Tests.Fixtures;

internal sealed class FakeCatalog : IMusicCatalog
{
    public IReadOnlyList<Track> Tracks { get; set; } = [];
    public IReadOnlyList<Playlist> Playlists { get; set; } = [];
    public Action? BeforeReturn { get; set; }
    public int Calls { get; private set; }
    public int LastOffset { get; private set; }
    public string LastValue { get; private set; } = "";
    public TrackSource LastSource { get; private set; }

    public Task<Page<Track>> SearchAsync(string query, int offset, CancellationToken ct) => TrackPage(TrackSource.Search, query, offset);
    public Task<Page<Track>> SavedTracksAsync(int offset, CancellationToken ct) => TrackPage(TrackSource.SavedLibrary, "", offset);
    public Task<Page<Track>> PlaylistTracksAsync(string id, int offset, CancellationToken ct) => TrackPage(TrackSource.Playlist, id, offset);
    private Task<Page<Track>> TrackPage(TrackSource source, string value, int offset)
    {
        Calls++;
        LastSource = source;
        LastValue = value;
        LastOffset = offset;
        BeforeReturn?.Invoke();
        return Task.FromResult(new Page<Track>(Tracks, offset, true));
    }
    public Task<Page<Playlist>> PlaylistsAsync(int offset, CancellationToken ct)
    {
        Calls++;
        LastOffset = offset;
        BeforeReturn?.Invoke();
        return Task.FromResult(new Page<Playlist>(Playlists, offset, true));
    }
}
