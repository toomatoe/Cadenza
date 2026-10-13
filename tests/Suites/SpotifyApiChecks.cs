using System.Net;
using Cadenza.Core.Exceptions;
using Cadenza.Core.Models;
using Cadenza.Core.Services.Spotify;
using Cadenza.Tests.Fixtures;
using Cadenza.Tests.Infrastructure;
using static Cadenza.Tests.Fixtures.TestData;

namespace Cadenza.Tests.Suites;

internal static class SpotifyApiChecks
{
    public static async Task RunAsync(CheckRunner checks)
    {
        var tokenStore = new MemoryTokenStore(new Tokens("old", "refresh", DateTimeOffset.UtcNow.AddMinutes(5), ClientId));
        var handler = new FakeHandler();
        using var http = new HttpClient(handler);
        var auth = new SpotifyAuth(http, tokenStore);
        await auth.RestoreAsync(ClientId, CancellationToken.None);
        var api = new SpotifyApi(http, auth);
        handler.Responses.Enqueue(new HttpResponseMessage(HttpStatusCode.Unauthorized));
        handler.Responses.Enqueue(JsonResponse("{\"access_token\":\"new\",\"expires_in\":3600}"));
        handler.Responses.Enqueue(JsonResponse("{\"tracks\":{\"items\":[{\"type\":\"track\",\"id\":\"abc\",\"name\":\"Song\",\"artists\":[{\"id\":\"artist\",\"name\":\"Artist\"}],\"album\":{\"name\":\"Album\"},\"duration_ms\":65000,\"explicit\":false}],\"next\":null}}"));
        var search = await api.SearchAsync("AC/DC & friends", 0, CancellationToken.None);
        checks.Check(search.Items.Count == 1 && search.Items[0].Duration == "1:05", "Parse search response");
        checks.Check(tokenStore.Value?.RefreshToken == "refresh", "Retain refresh token when omitted");
        checks.Check(handler.Requests[0].Contains("AC%2FDC%20%26%20friends"), "Search query encoding");
        checks.Check(handler.Bearers[^1] == "new", "Retry 401 with refreshed token");
        handler.Responses.Enqueue(JsonResponse("{\"items\":[{\"item\":{\"type\":\"track\",\"id\":\"abc\",\"name\":\"Song\",\"artists\":[],\"album\":{\"name\":\"Album\"},\"duration_ms\":65000}},{\"item\":null}],\"next\":\"next page\"}"));
        var playlist = await api.PlaylistTracksAsync("1234567890123456789012", 10, CancellationToken.None);
        checks.Check(playlist.HasMore && playlist.Items.Count == 1 && playlist.Offset == 10, "2026 playlist item shape and null filtering");
        handler.Responses.Enqueue(JsonResponse("""
        {"items":[{"id":"1234567890123456789012","name":"Evening records","tracks":{"total":12},"owner":{"display_name":"Listener"},"images":[{"url":"https://i.scdn.co/large","width":640},{"url":"https://i.scdn.co/cover","width":300},{"url":"https://i.scdn.co/small","width":64}]},{"id":"empty","name":"No artwork","images":null},{"id":"unknown","name":"Unknown size","images":[{"url":"http://example.com/insecure","width":300},{"url":"https://i.scdn.co/unknown","width":null}]}],"next":null}
        """));
        var covers = await api.PlaylistsAsync(0, CancellationToken.None);
        checks.Check(covers.Items[0].ArtworkUrl == "https://i.scdn.co/cover" && covers.Items[0].Subtitle == "12 tracks · Listener", "Playlist covers prefer bounded artwork and retain metadata");
        checks.Check(covers.Items[1].ArtworkUrl == "" && covers.Items[1].Count == 0, "Missing playlist images and counts have safe fallbacks");
        checks.Check(covers.Items[2].ArtworkUrl == "https://i.scdn.co/unknown", "Nullable artwork sizes and insecure URLs handled safely");
        handler.Responses.Enqueue(JsonResponse("""
        {"items":[{"track":{"type":"track","id":"abc","name":"Song","artists":[],"album":{"name":"Album","images":[{"url":"https://i.scdn.co/album","width":300}]},"duration_ms":65000}}],"next":null}
        """));
        var albumCover = await api.SavedTracksAsync(0, CancellationToken.None);
        checks.Check(albumCover.Items[0].ArtworkUrl == "https://i.scdn.co/album", "Track album artwork retained for library and player");
        checks.Check(search.Items[0].ArtworkUrl == "", "Tracks without artwork retain fallback");
        handler.Responses.Enqueue(new HttpResponseMessage(HttpStatusCode.Forbidden));
        try
        { await api.SavedTracksAsync(0, CancellationToken.None); throw new InvalidOperationException("403 accepted"); }
        catch (SpotifyApiException error) { checks.Check(error.StatusCode == 403, "Actionable API access error"); }
        var rateLimited = new HttpResponseMessage((HttpStatusCode)429);
        rateLimited.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(90));
        handler.Responses.Enqueue(rateLimited);
        try
        { await api.SavedTracksAsync(0, CancellationToken.None); throw new InvalidOperationException("Long rate limit ignored"); }
        catch (SpotifyApiException error) { checks.Check(error.StatusCode == 429, "Respect long rate limit without retry storm"); }
        await auth.DisconnectAsync(CancellationToken.None);
        checks.Check(tokenStore.Value is null && !auth.IsConnected, "Disconnect deletes saved credentials");

    }
}
