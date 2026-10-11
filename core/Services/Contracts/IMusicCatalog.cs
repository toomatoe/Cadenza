using Cadenza.Core.Models;

namespace Cadenza.Core.Services.Contracts;

/// <summary>Metadata requests used by catalog controllers, independent of WinUI and playback.</summary>
public interface IMusicCatalog
{
    Task<Page<Track>> SearchAsync(string query, int offset, CancellationToken ct);
    Task<Page<Track>> SavedTracksAsync(int offset, CancellationToken ct);
    Task<Page<Track>> PlaylistTracksAsync(string id, int offset, CancellationToken ct);
    Task<Page<Playlist>> PlaylistsAsync(int offset, CancellationToken ct);
}
