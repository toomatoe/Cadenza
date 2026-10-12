using System.Collections.ObjectModel;
using Cadenza.Core.Models;
using Cadenza.Core.Services.Contracts;
using Cadenza.Core.Services.Spotify;

namespace Cadenza.Core.Controllers;

/// <summary>Coordinates catalog requests and commits model changes after cancellation checks.</summary>
public class CatalogController(IMusicCatalog catalog, CatalogModel model)
{
    public async Task LoadTracksAsync(TrackSource source, string queryOrPlaylistId, bool append, CancellationToken ct)
    {
        if (append && model.Tracks.Count >= CatalogModel.ItemLimit)
        {
            model.HasMoreTracks = false;
            return;
        }

        var query = queryOrPlaylistId.Trim();
        if (source == TrackSource.Search && query.Length == 0) return;
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
        if (source == TrackSource.Search) model.LastSearchQuery = query;
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

    public async Task<IReadOnlyList<Playlist>> LoadPinnedPlaylistsAsync(CancellationToken ct)
    {
        var page = await catalog.PlaylistsAsync(0, ct);
        ct.ThrowIfCancellationRequested();
        return page.Items.Take(6).ToArray();
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
        if (!append) target.Clear();
        var available = CatalogModel.ItemLimit - target.Count;
        for (var index = 0; index < items.Count && index < available; index++) target.Add(items[index]);
    }
}
