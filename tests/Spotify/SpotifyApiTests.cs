using System.Net;
using System.Net.Http.Headers;
using Cadenza.Core.Exceptions;
using Cadenza.Core.Models;
using Cadenza.Core.Services.Spotify;
using Cadenza.UnitTests.Fakes;
using static Cadenza.UnitTests.Fixtures.TestData;

namespace Cadenza.UnitTests.Spotify;

[TestClass]
public sealed class SpotifyApiTests : IDisposable
{
    private const string SearchResponse = """
        {"tracks":{"items":[{"type":"track","id":"abc","name":"Song","artists":[{"id":"artist","name":"Artist"}],"album":{"name":"Album"},"duration_ms":65000,"explicit":false}],"next":null}}
        """;

    private const string PlaylistsResponse = """
        {"items":[{"id":"1234567890123456789012","name":"Evening records","tracks":{"total":12},"owner":{"display_name":"Listener"},"images":[{"url":"https://i.scdn.co/large","width":640},{"url":"https://i.scdn.co/cover","width":300},{"url":"https://i.scdn.co/small","width":64}]},{"id":"empty","name":"No artwork","images":null},{"id":"unknown","name":"Unknown size","images":[{"url":"http://example.com/insecure","width":300},{"url":"https://i.scdn.co/unknown","width":null}]}],"next":null}
        """;

    private readonly MemoryTokenStore _tokenStore = new(new Tokens("old", "refresh", DateTimeOffset.UtcNow.AddMinutes(5), ClientId));
    private readonly FakeHandler _handler = new();
    private readonly HttpClient _http;
    private readonly SpotifyAuth _auth;
    private readonly SpotifyApi _api;

    public SpotifyApiTests()
    {
        _http = new HttpClient(_handler);
        _auth = new SpotifyAuth(_http, _tokenStore);
        _api = new SpotifyApi(_http, _auth);
    }

    [TestInitialize]
    public async Task RestoreSessionAsync() => await _auth.RestoreAsync(ClientId, CancellationToken.None);

    public void Dispose() => _http.Dispose();

    [TestMethod]
    public async Task SearchResponseIsParsed()
    {
        var search = await SearchAfterExpiredTokenAsync();

        Assert.HasCount(1, search.Items, "Parse search response");
        Assert.AreEqual("1:05", search.Items[0].Duration, "Parse search response");
    }

    [TestMethod]
    public async Task RefreshTokenIsRetainedWhenTheResponseOmitsIt()
    {
        await SearchAfterExpiredTokenAsync();

        Assert.AreEqual("refresh", _tokenStore.Value?.RefreshToken, "Retain refresh token when omitted");
    }

    [TestMethod]
    public async Task SearchQueryIsEncoded()
    {
        await SearchAfterExpiredTokenAsync();

        Assert.Contains("AC%2FDC%20%26%20friends", _handler.Requests[0], StringComparison.Ordinal, "Search query encoding");
    }

    [TestMethod]
    public async Task UnauthorizedRequestIsRetriedWithTheRefreshedToken()
    {
        await SearchAfterExpiredTokenAsync();

        Assert.AreEqual("new", _handler.Bearers[^1], "Retry 401 with refreshed token");
    }

    [TestMethod]
    public async Task TracksWithoutArtworkRetainTheFallback()
    {
        var search = await SearchAfterExpiredTokenAsync();

        Assert.AreEqual("", search.Items[0].ArtworkUrl, "Tracks without artwork retain fallback");
    }

    [TestMethod]
    public async Task PlaylistItemsUseThe2026ShapeAndSkipNullItems()
    {
        _handler.Responses.Enqueue(JsonResponse("""
            {"items":[{"item":{"type":"track","id":"abc","name":"Song","artists":[],"album":{"name":"Album"},"duration_ms":65000}},{"item":null}],"next":"next page"}
            """));

        var playlist = await _api.PlaylistTracksAsync("1234567890123456789012", 10, CancellationToken.None);

        Assert.IsTrue(playlist.HasMore, "2026 playlist item shape reports more pages");
        Assert.HasCount(1, playlist.Items, "2026 playlist item shape and null filtering");
        Assert.AreEqual(10, playlist.Offset, "2026 playlist item shape keeps the offset");
    }

    [TestMethod]
    public async Task PlaylistCoversPreferBoundedArtworkAndRetainMetadata()
    {
        _handler.Responses.Enqueue(JsonResponse(PlaylistsResponse));

        var covers = await _api.PlaylistsAsync(0, CancellationToken.None);

        Assert.AreEqual("https://i.scdn.co/cover", covers.Items[0].ArtworkUrl, "Playlist covers prefer bounded artwork");
        Assert.AreEqual("12 tracks · Listener", covers.Items[0].Subtitle, "Playlist covers retain metadata");
    }

    [TestMethod]
    public async Task MissingPlaylistImagesAndCountsHaveSafeFallbacks()
    {
        _handler.Responses.Enqueue(JsonResponse(PlaylistsResponse));

        var covers = await _api.PlaylistsAsync(0, CancellationToken.None);

        Assert.AreEqual("", covers.Items[1].ArtworkUrl, "Missing playlist images have a safe fallback");
        Assert.AreEqual(0, covers.Items[1].Count, "Missing playlist counts have a safe fallback");
    }

    [TestMethod]
    public async Task NullableArtworkSizesAndInsecureUrlsAreHandledSafely()
    {
        _handler.Responses.Enqueue(JsonResponse(PlaylistsResponse));

        var covers = await _api.PlaylistsAsync(0, CancellationToken.None);

        Assert.AreEqual("https://i.scdn.co/unknown", covers.Items[2].ArtworkUrl, "Nullable artwork sizes and insecure URLs handled safely");
    }

    [TestMethod]
    public async Task TrackAlbumArtworkIsRetainedForLibraryAndPlayer()
    {
        _handler.Responses.Enqueue(JsonResponse("""
            {"items":[{"track":{"type":"track","id":"abc","name":"Song","artists":[],"album":{"name":"Album","images":[{"url":"https://i.scdn.co/album","width":300}]},"duration_ms":65000}}],"next":null}
            """));

        var saved = await _api.SavedTracksAsync(0, CancellationToken.None);

        Assert.AreEqual("https://i.scdn.co/album", saved.Items[0].ArtworkUrl, "Track album artwork retained for library and player");
    }

    [TestMethod]
    public async Task ForbiddenResponseIsAnActionableApiAccessError()
    {
        _handler.Responses.Enqueue(new HttpResponseMessage(HttpStatusCode.Forbidden));

        var error = await Assert.ThrowsAsync<SpotifyApiException>(() => _api.SavedTracksAsync(0, CancellationToken.None), "403 accepted");

        Assert.AreEqual(403, error.StatusCode, "Actionable API access error");
    }

    [TestMethod]
    public async Task LongRateLimitIsRespectedWithoutARetryStorm()
    {
        var rateLimited = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        rateLimited.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(90));
        _handler.Responses.Enqueue(rateLimited);

        var error = await Assert.ThrowsAsync<SpotifyApiException>(() => _api.SavedTracksAsync(0, CancellationToken.None), "Long rate limit ignored");

        Assert.AreEqual(429, error.StatusCode, "Respect long rate limit without retry storm");
    }

    [TestMethod]
    public async Task DisconnectDeletesSavedCredentials()
    {
        await _auth.DisconnectAsync(CancellationToken.None);

        Assert.IsNull(_tokenStore.Value, "Disconnect deletes saved credentials");
        Assert.IsFalse(_auth.IsConnected, "Disconnect deletes saved credentials");
    }

    /// <summary>Searches while the saved access token is rejected, so the client refreshes it and retries.</summary>
    private async Task<Page<Track>> SearchAfterExpiredTokenAsync()
    {
        _handler.Responses.Enqueue(new HttpResponseMessage(HttpStatusCode.Unauthorized));
        _handler.Responses.Enqueue(JsonResponse("{\"access_token\":\"new\",\"expires_in\":3600}"));
        _handler.Responses.Enqueue(JsonResponse(SearchResponse));

        return await _api.SearchAsync("AC/DC & friends", 0, CancellationToken.None);
    }
}
