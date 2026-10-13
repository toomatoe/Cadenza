using System.Collections.ObjectModel;
using Cadenza.Core.Models;
using Cadenza.Core.Services.Contracts;
using Cadenza.Core.Services.Spotify;

namespace Cadenza.Core.Controllers;

/// <summary>
/// Coordinates catalog requests and commits results to the <see cref="CatalogModel"/>. A result is
/// committed only if its request was not cancelled, even when the provider ignores cancellation.
/// </summary>
public sealed class CatalogController(IMusicCatalog catalog, CatalogModel model)
{
    /// <summary>Playlists shown in the sidebar and the library's playlist fan.</summary>
    public const int PinnedPlaylistCount = 6;

    /// <summary>Loads tracks from <paramref name="source"/>, replacing or appending to the current list.</summary>
    /// <param name="queryOrPlaylistId">The search text, or the playlist ID; ignored for the saved library.</param>
    public async Task LoadTracksAsync(TrackSource source, string queryOrPlaylistId, bool append, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(queryOrPlaylistId);
        if (append && model.Tracks.Count >= CatalogModel.ItemLimit)
        {
            model.HasMoreTracks = false;
            return;
        }

        var query = queryOrPlaylistId.Trim();
        if (source == TrackSource.Search && query.Length == 0)
        {
            return;
        }

        var offset = append ? model.TrackOffset + SpotifyApi.PageSize : 0;
        var page = source switch
        {
            TrackSource.SavedLibrary => await catalog.SavedTracksAsync(offset, ct),
            TrackSource.Search => await catalog.SearchAsync(query, offset, ct),
            TrackSource.Playlist => await catalog.PlaylistTracksAsync(query, offset, ct),
            _ => throw new ArgumentOutOfRangeException(nameof(source))
        };

        ct.ThrowIfCancellationRequested();
        ApplyPage(model.Tracks, page.Items, append);
        model.TrackOffset = page.Offset;
        model.HasMoreTracks = page.HasMore && model.Tracks.Count < CatalogModel.ItemLimit;
        if (source == TrackSource.Search)
        {
            model.LastSearchQuery = query;
        }
    }

    public async Task LoadPlaylistsAsync(bool append, CancellationToken ct)
    {
        if (append && model.Playlists.Count >= CatalogModel.ItemLimit)
        {
            model.HasMorePlaylists = false;
            return;
        }

        var offset = append ? model.PlaylistOffset + SpotifyApi.PageSize : 0;
        var page = await catalog.PlaylistsAsync(offset, ct);
        ct.ThrowIfCancellationRequested();
        ApplyPage(model.Playlists, page.Items, append);
        model.PlaylistOffset = page.Offset;
        model.HasMorePlaylists = page.HasMore && model.Playlists.Count < CatalogModel.ItemLimit;
    }

    /// <summary>Returns the first <see cref="PinnedPlaylistCount"/> playlists without changing the model.</summary>
    public async Task<IReadOnlyList<Playlist>> LoadPinnedPlaylistsAsync(CancellationToken ct)
    {
        var page = await catalog.PlaylistsAsync(0, ct);
        ct.ThrowIfCancellationRequested();
        return page.Items.Take(PinnedPlaylistCount).ToArray();
    }

    public void ResetTracks()
    {
        model.Tracks.Clear();
        model.TrackOffset = 0;
        model.HasMoreTracks = false;
        model.LastSearchQuery = "";
    }

    public void Reset()
    {
        ResetTracks();
        model.Playlists.Clear();
        model.PlaylistOffset = 0;
        model.HasMorePlaylists = false;
    }

    private static void ApplyPage<T>(ObservableCollection<T> target, IReadOnlyList<T> items, bool append)
    {
        if (!append)
        {
            target.Clear();
        }

        var available = CatalogModel.ItemLimit - target.Count;
        for (var index = 0; index < items.Count && index < available; index++)
        {
            target.Add(items[index]);
        }
    }
}
