using Cadenza.Core.Controllers;
using Cadenza.Core.Models;
using Cadenza.Core.Services.Spotify;
using Cadenza.Tests.Fixtures;
using Cadenza.Tests.Infrastructure;
using static Cadenza.Tests.Fixtures.TestData;

namespace Cadenza.Tests.Suites;

internal static class CatalogControllerChecks
{
    public static async Task RunAsync(CheckRunner checks)
    {
        var catalog = new FakeCatalog();
        var model = new CatalogModel();
        var controller = new CatalogController(catalog, model);
        catalog.Tracks = [Song("saved", "artist")];
        await controller.LoadTracksAsync(TrackSource.SavedLibrary, "", false, CancellationToken.None);
        checks.Check(model.Tracks[0].Id == "saved" && catalog.LastSource == TrackSource.SavedLibrary, "Controller loads saved library");

        catalog.Tracks = [Song("search", "artist")];
        await controller.LoadTracksAsync(TrackSource.Search, "  query  ", false, CancellationToken.None);
        checks.Check(model.Tracks.Count == 1 && model.Tracks[0].Id == "search" && model.LastSearchQuery == "query", "New search replaces the previous collection and trims query");
        await controller.LoadTracksAsync(TrackSource.Search, "query", true, CancellationToken.None);
        checks.Check(catalog.LastOffset == SpotifyApi.PageSize && model.Tracks.Count == 2, "Controller appends the next page");

        await controller.LoadTracksAsync(TrackSource.Playlist, "playlist-id", false, CancellationToken.None);
        checks.Check(catalog.LastSource == TrackSource.Playlist && catalog.LastValue == "playlist-id" && catalog.LastOffset == 0, "Playlist selection starts at the first page");
        var calls = catalog.Calls;
        await controller.LoadTracksAsync(TrackSource.Search, "   ", false, CancellationToken.None);
        checks.Check(catalog.Calls == calls, "Blank search does not request metadata");

        using (var canceled = new CancellationTokenSource())
        {
            catalog.BeforeReturn = canceled.Cancel;
            await checks.ThrowsAsync<OperationCanceledException>(
                () => controller.LoadTracksAsync(TrackSource.Search, "stale", false, canceled.Token),
                "Canceled metadata result accepted");
            checks.Check(model.Tracks.Count == 1 && model.LastSearchQuery == "query", "Canceled result leaves the visible collection and query unchanged");
            catalog.BeforeReturn = null;
        }

        model.Tracks.Clear();
        for (var index = 0; index < CatalogModel.ItemLimit - 1; index++) model.Tracks.Add(Song(index.ToString(), "artist"));
        catalog.Tracks = Enumerable.Range(0, SpotifyApi.PageSize).Select(index => Song($"extra-{index}", "artist")).ToArray();
        await controller.LoadTracksAsync(TrackSource.SavedLibrary, "", true, CancellationToken.None);
        checks.Check(model.Tracks.Count == CatalogModel.ItemLimit && !model.HasMoreTracks, "Partial final page respects the exact track bound");
        calls = catalog.Calls;
        await controller.LoadTracksAsync(TrackSource.SavedLibrary, "", true, CancellationToken.None);
        checks.Check(catalog.Calls == calls, "Bounded collection does not issue another request");

        catalog.Playlists = [new Playlist("list", "Playlist", 10)];
        await controller.LoadPlaylistsAsync(false, CancellationToken.None);
        await controller.LoadPlaylistsAsync(true, CancellationToken.None);
        checks.Check(model.Playlists.Count == 2 && model.PlaylistOffset == SpotifyApi.PageSize, "Playlist pagination uses the shared page size");
        using (var canceled = new CancellationTokenSource())
        {
            catalog.BeforeReturn = canceled.Cancel;
            await checks.ThrowsAsync<OperationCanceledException>(() => controller.LoadPlaylistsAsync(false, canceled.Token), "Canceled playlist result accepted");
            checks.Check(model.Playlists.Count == 2 && model.PlaylistOffset == SpotifyApi.PageSize, "Canceled playlist load preserves committed state");
            catalog.BeforeReturn = null;
        }

        catalog.Playlists = Enumerable.Range(0, 8).Select(index => new Playlist(index.ToString(), "Pinned", 1)).ToArray();
        checks.Check((await controller.LoadPinnedPlaylistsAsync(CancellationToken.None)).Count == 6, "Pinned playlist preview remains bounded");
        controller.Reset();
        checks.Check(model.Tracks.Count == 0 && model.Playlists.Count == 0 && !model.HasMoreTracks && !model.HasMorePlaylists && model.TrackOffset == 0 && model.PlaylistOffset == 0 && model.LastSearchQuery.Length == 0, "Disconnect resets catalog data and paging state");
    }

}
