using System.Collections.ObjectModel;
using Cadenza.Core.Interfaces;
using Cadenza.Core.Models;
using Cadenza.Core.Services.Spotify;

namespace Cadenza.Core.Services.Catalog;

/// <summary>
/// Coordinates catalog requests and commits results to the <see cref="CatalogModel"/>. A result is
/// committed only if its request was not cancelled, even when the provider ignores cancellation.
/// </summary>
public sealed class CatalogController
{
    /// <summary>Playlists shown in the sidebar and the library's playlist fan.</summary>
    public const int PinnedPlaylistCount = 6;
    private readonly IMusicCatalog _catalog;
    private readonly CatalogModel _model;

    public CatalogController(IMusicCatalog catalog, CatalogModel model)
    {
        _catalog = catalog;
        _model = model;
    }

    /// <summary>Loads tracks from <paramref name="source"/>, replacing or appending to the current list.</summary>
    /// <param name="queryOrPlaylistId">The search text, or the playlist ID; ignored for the saved library.</param>
    public async Task LoadTracksAsync(TrackSource source, string queryOrPlaylistId, bool append, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(queryOrPlaylistId);
        if (append && _model.Tracks.Count >= CatalogModel.ItemLimit)
        {
            _model.HasMoreTracks = false;
            return;
        }

        var query = queryOrPlaylistId.Trim();
        if (source == TrackSource.Search && query.Length == 0)
            return;

        var offset = append ? _model.TrackOffset + SpotifyApi.PageSize : 0;
        var page = source switch
        {
            TrackSource.SavedLibrary => await _catalog.SavedTracksAsync(offset, cancellationToken),
            TrackSource.Search => await _catalog.SearchAsync(query, offset, cancellationToken),
            TrackSource.Playlist => await _catalog.PlaylistTracksAsync(query, offset, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(source))
        };

        cancellationToken.ThrowIfCancellationRequested();
        ApplyPage(_model.Tracks, page.Items, append);
        _model.TrackOffset = page.Offset;
        _model.HasMoreTracks = page.HasMore && _model.Tracks.Count < CatalogModel.ItemLimit;
        if (source == TrackSource.Search)
            _model.LastSearchQuery = query;
    }

    public async Task LoadPlaylistsAsync(bool append, CancellationToken cancellationToken)
    {
        if (append && _model.Playlists.Count >= CatalogModel.ItemLimit)
        {
            _model.HasMorePlaylists = false;
            return;
        }

        var offset = append ? _model.PlaylistOffset + SpotifyApi.PageSize : 0;
        var page = await _catalog.PlaylistsAsync(offset, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        ApplyPage(_model.Playlists, page.Items, append);
        _model.PlaylistOffset = page.Offset;
        _model.HasMorePlaylists = page.HasMore && _model.Playlists.Count < CatalogModel.ItemLimit;
    }

    /// <summary>Returns the first <see cref="PinnedPlaylistCount"/> playlists without changing the model.</summary>
    public async Task<IReadOnlyList<Playlist>> LoadPinnedPlaylistsAsync(CancellationToken cancellationToken)
    {
        var page = await _catalog.PlaylistsAsync(0, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        return page.Items.Take(PinnedPlaylistCount).ToArray();
    }

    public void ResetTracks()
    {
        _model.Tracks.Clear();
        _model.TrackOffset = 0;
        _model.HasMoreTracks = false;
        _model.LastSearchQuery = "";
    }

    public void Reset()
    {
        ResetTracks();
        _model.Playlists.Clear();
        _model.PlaylistOffset = 0;
        _model.HasMorePlaylists = false;
    }

    private static void ApplyPage<T>(ObservableCollection<T> target, IReadOnlyList<T> items, bool append)
    {
        if (!append)
            target.Clear();

        var available = CatalogModel.ItemLimit - target.Count;
        for (var index = 0; index < items.Count && index < available; index++)
        {
            target.Add(items[index]);
        }
    }
}
