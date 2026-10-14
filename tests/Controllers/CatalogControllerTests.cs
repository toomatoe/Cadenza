using System.Globalization;
using Cadenza.Core.Models;
using Cadenza.Core.Services.Catalog;
using Cadenza.Core.Services.Spotify;
using Cadenza.UnitTests.Fakes;
using static Cadenza.UnitTests.Fixtures.TestData;

namespace Cadenza.UnitTests.Controllers;

[TestClass]
public sealed class CatalogControllerTests
{
    private const string Query = "query";
    private const string Artist = "artist";

    private readonly FakeCatalog _catalog = new();
    private readonly CatalogModel _model = new();
    private readonly CatalogController _controller;

    public CatalogControllerTests()
    {
        _controller = new CatalogController(_catalog, _model);
    }

    [TestMethod]
    public async Task ControllerLoadsTheSavedLibrary()
    {
        _catalog.Tracks = [Song("saved", Artist)];

        await _controller.LoadTracksAsync(TrackSource.SavedLibrary, "", false, CancellationToken.None);

        Assert.AreEqual("saved", _model.Tracks[0].Id, "Controller loads saved library");
        Assert.AreEqual(TrackSource.SavedLibrary, _catalog.LastSource, "Controller loads saved library");
    }

    [TestMethod]
    public async Task NewSearchReplacesThePreviousCollectionAndTrimsTheQuery()
    {
        _catalog.Tracks = [Song("saved", Artist)];
        await _controller.LoadTracksAsync(TrackSource.SavedLibrary, "", false, CancellationToken.None);
        _catalog.Tracks = [Song("search", Artist)];

        await _controller.LoadTracksAsync(TrackSource.Search, "  query  ", false, CancellationToken.None);

        Assert.HasCount(1, _model.Tracks, "New search replaces the previous collection");
        Assert.AreEqual("search", _model.Tracks[0].Id, "New search replaces the previous collection");
        Assert.AreEqual(Query, _model.LastSearchQuery, "New search trims the query");
    }

    [TestMethod]
    public async Task ControllerAppendsTheNextPage()
    {
        _catalog.Tracks = [Song("search", Artist)];
        await _controller.LoadTracksAsync(TrackSource.Search, Query, false, CancellationToken.None);

        await _controller.LoadTracksAsync(TrackSource.Search, Query, true, CancellationToken.None);

        Assert.AreEqual(SpotifyApi.PageSize, _catalog.LastOffset, "Controller appends the next page");
        Assert.HasCount(2, _model.Tracks, "Controller appends the next page");
    }

    [TestMethod]
    public async Task PlaylistSelectionStartsAtTheFirstPage()
    {
        _catalog.Tracks = [Song("search", Artist)];
        await _controller.LoadTracksAsync(TrackSource.Search, Query, false, CancellationToken.None);
        await _controller.LoadTracksAsync(TrackSource.Search, Query, true, CancellationToken.None);

        await _controller.LoadTracksAsync(TrackSource.Playlist, "playlist-id", false, CancellationToken.None);

        Assert.AreEqual(TrackSource.Playlist, _catalog.LastSource, "Playlist selection loads the playlist");
        Assert.AreEqual("playlist-id", _catalog.LastValue, "Playlist selection loads the playlist");
        Assert.AreEqual(0, _catalog.LastOffset, "Playlist selection starts at the first page");
    }

    [TestMethod]
    public async Task BlankSearchDoesNotRequestMetadata()
    {
        var calls = _catalog.Calls;

        await _controller.LoadTracksAsync(TrackSource.Search, "   ", false, CancellationToken.None);

        Assert.AreEqual(calls, _catalog.Calls, "Blank search does not request metadata");
    }

    [TestMethod]
    public async Task CanceledSearchResultLeavesTheVisibleCollectionAndQueryUnchanged()
    {
        _catalog.Tracks = [Song("search", Artist)];
        await _controller.LoadTracksAsync(TrackSource.Search, Query, false, CancellationToken.None);
        using var canceled = new CancellationTokenSource();
        _catalog.BeforeReturn = canceled.Cancel;

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => _controller.LoadTracksAsync(TrackSource.Search, "stale", false, canceled.Token),
            "Canceled metadata result accepted");

        Assert.HasCount(1, _model.Tracks, "Canceled result leaves the visible collection unchanged");
        Assert.AreEqual(Query, _model.LastSearchQuery, "Canceled result leaves the query unchanged");
    }

    [TestMethod]
    public async Task PartialFinalPageRespectsTheExactTrackBound()
    {
        FillToOneBelowTheLimit();

        await _controller.LoadTracksAsync(TrackSource.SavedLibrary, "", true, CancellationToken.None);

        Assert.HasCount(CatalogModel.ItemLimit, _model.Tracks, "Partial final page respects the exact track bound");
        Assert.IsFalse(_model.HasMoreTracks, "Partial final page respects the exact track bound");
    }

    [TestMethod]
    public async Task BoundedCollectionDoesNotIssueAnotherRequest()
    {
        FillToOneBelowTheLimit();
        await _controller.LoadTracksAsync(TrackSource.SavedLibrary, "", true, CancellationToken.None);
        var calls = _catalog.Calls;

        await _controller.LoadTracksAsync(TrackSource.SavedLibrary, "", true, CancellationToken.None);

        Assert.AreEqual(calls, _catalog.Calls, "Bounded collection does not issue another request");
    }

    [TestMethod]
    public async Task PlaylistPaginationUsesTheSharedPageSize()
    {
        _catalog.Playlists = [new Playlist("list", "Playlist", 10)];

        await _controller.LoadPlaylistsAsync(false, CancellationToken.None);
        await _controller.LoadPlaylistsAsync(true, CancellationToken.None);

        Assert.HasCount(2, _model.Playlists, "Playlist pagination appends the next page");
        Assert.AreEqual(SpotifyApi.PageSize, _model.PlaylistOffset, "Playlist pagination uses the shared page size");
    }

    [TestMethod]
    public async Task CanceledPlaylistLoadPreservesCommittedState()
    {
        _catalog.Playlists = [new Playlist("list", "Playlist", 10)];
        await _controller.LoadPlaylistsAsync(false, CancellationToken.None);
        await _controller.LoadPlaylistsAsync(true, CancellationToken.None);
        using var canceled = new CancellationTokenSource();
        _catalog.BeforeReturn = canceled.Cancel;

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => _controller.LoadPlaylistsAsync(false, canceled.Token),
            "Canceled playlist result accepted");

        Assert.HasCount(2, _model.Playlists, "Canceled playlist load preserves committed state");
        Assert.AreEqual(SpotifyApi.PageSize, _model.PlaylistOffset, "Canceled playlist load preserves committed state");
    }

    [TestMethod]
    public async Task PinnedPlaylistPreviewRemainsBounded()
    {
        _catalog.Playlists = Enumerable.Range(0, 8)
            .Select(index => new Playlist(index.ToString(CultureInfo.InvariantCulture), "Pinned", 1))
            .ToArray();

        var pinned = await _controller.LoadPinnedPlaylistsAsync(CancellationToken.None);

        Assert.HasCount(6, pinned, "Pinned playlist preview remains bounded");
    }

    [TestMethod]
    public async Task DisconnectResetsCatalogDataAndPagingState()
    {
        _catalog.Tracks = [Song("search", Artist)];
        _catalog.Playlists = [new Playlist("list", "Playlist", 10)];
        await _controller.LoadTracksAsync(TrackSource.Search, Query, false, CancellationToken.None);
        await _controller.LoadTracksAsync(TrackSource.Search, Query, true, CancellationToken.None);
        await _controller.LoadPlaylistsAsync(false, CancellationToken.None);
        await _controller.LoadPlaylistsAsync(true, CancellationToken.None);

        _controller.Reset();

        Assert.IsEmpty(_model.Tracks, "Disconnect resets catalog tracks");
        Assert.IsEmpty(_model.Playlists, "Disconnect resets catalog playlists");
        Assert.IsFalse(_model.HasMoreTracks, "Disconnect resets track paging");
        Assert.IsFalse(_model.HasMorePlaylists, "Disconnect resets playlist paging");
        Assert.AreEqual(0, _model.TrackOffset, "Disconnect resets the track offset");
        Assert.AreEqual(0, _model.PlaylistOffset, "Disconnect resets the playlist offset");
        Assert.AreEqual(0, _model.LastSearchQuery.Length, "Disconnect resets the last search query");
    }

    private void FillToOneBelowTheLimit()
    {
        for (var index = 0; index < CatalogModel.ItemLimit - 1; index++)
            _model.Tracks.Add(Song(index, Artist));

        _catalog.Tracks = Enumerable.Range(0, SpotifyApi.PageSize).Select(index => Song($"extra-{index}", Artist)).ToArray();
    }
}
