using System.Net;
using System.Text;
using System.Text.Json;
using Cadenza.Core;

var passed = 0;
void Check(bool value, string message) { if (!value) throw new Exception(message); passed++; }
const string clientId = "0123456789abcdef0123456789abcdef";
Check(Pkce.Challenge("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk") == "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM", "RFC 7636 PKCE vector");
Check(Pkce.RandomValue().Length == 43, "PKCE verifier length");
Check(SpotifyAuth.ValidateRedirectUri("http://127.0.0.1:9000/signin").Port == 9000, "Custom redirect port and path");
foreach (var redirect in new[] { "http://localhost:8888/callback", "https://example.com/callback", "http://127.0.0.1/callback?x=1" })
{
    try { SpotifyAuth.ValidateRedirectUri(redirect); throw new Exception("Invalid redirect accepted"); }
    catch (ArgumentException) { passed++; }
}
Check(Pkce.ReadCode(new Uri("http://127.0.0.1:8888/callback?state=abc&code=hello"), "abc") == "hello", "OAuth code parsing");
foreach (var uri in new[] { "?state=wrong&code=x", "?state=abc&error=access_denied", "?state=abc&code=x&code=y", "?code=x" })
{
    try { Pkce.ReadCode(new Uri("http://127.0.0.1:8888/callback" + uri), "abc"); throw new Exception("Invalid OAuth callback accepted"); }
    catch (InvalidOperationException) { passed++; }
}
Track Song(string id, string artist) => new(id, id, artist, artist, "Album", 65000, "https://open.spotify.com/track/" + id, false);
var queue = new ListeningQueue();
queue.Add(Song("1", "a")); queue.Add(Song("2", "a")); queue.Add(Song("3", "b")); queue.Add(Song("4", "b"));
queue.Move(0, 3); Check(queue.Tracks[^1].Id == "1", "Queue move");
queue.Undo(); Check(queue.Tracks[0].Id == "1", "Queue undo");
queue.Shuffle(1, new Random(4));
Check(queue.Tracks.Select(t => t.Id).Order().SequenceEqual(new[] { "1", "2", "3", "4" }), "Shuffle retains all tracks");
Check(queue.Tracks.Zip(queue.Tracks.Skip(1)).All(pair => pair.First.ArtistId != pair.Second.ArtistId), "Artist spacing when possible");
queue.Forget(); Check(!queue.CanUndo && queue.Tracks.Count == 0, "Disconnect clears queue history");
for (var i = 0; i < ListeningQueue.Capacity; i++) queue.Add(Song(i.ToString(), "a"));
try { queue.Add(Song("overflow", "a")); throw new Exception("Queue limit not enforced"); }
catch (InvalidOperationException) { passed++; }

Check(!PlaybackErrors.RequiresReconnect(9) && !PlaybackErrors.RequiresReconnect(10), "Unavailable track and audio-key refusal preserve a valid session");
Check(PlaybackErrors.RequiresReconnect(6) && PlaybackErrors.RequiresReconnect(8), "Disconnected session and failed audio device are recreated");
Check(PlaybackErrors.Describe(10).Contains("audio key") && !PlaybackErrors.Describe(10).Contains("Reconnect"), "Audio-key rejection does not suggest ineffective OAuth reconnect");
Check(PlaybackErrors.Describe(11) != PlaybackErrors.Describe(13), "Metadata and decoder failures have distinct messages");
var tokenStore = new MemoryTokenStore(new Tokens("old", "refresh", DateTimeOffset.UtcNow.AddMinutes(5), clientId));
var handler = new FakeHandler();
using var http = new HttpClient(handler);
var auth = new SpotifyAuth(http, tokenStore);
await auth.RestoreAsync(clientId, CancellationToken.None);
var api = new SpotifyApi(http, auth);
handler.Responses.Enqueue(new HttpResponseMessage(HttpStatusCode.Unauthorized));
handler.Responses.Enqueue(JsonResponse("{\"access_token\":\"new\",\"expires_in\":3600}"));
handler.Responses.Enqueue(JsonResponse("{\"tracks\":{\"items\":[{\"type\":\"track\",\"id\":\"abc\",\"name\":\"Song\",\"artists\":[{\"id\":\"artist\",\"name\":\"Artist\"}],\"album\":{\"name\":\"Album\"},\"duration_ms\":65000,\"explicit\":false}],\"next\":null}}"));
var search = await api.SearchAsync("AC/DC & friends", 0, CancellationToken.None);
Check(search.Items.Count == 1 && search.Items[0].Duration == "1:05", "Parse search response");
Check(tokenStore.Value?.RefreshToken == "refresh", "Retain refresh token when omitted");
Check(handler.Requests[0].Contains("AC%2FDC%20%26%20friends"), "Search query encoding");
Check(handler.Bearers[^1] == "new", "Retry 401 with refreshed token");
handler.Responses.Enqueue(JsonResponse("{\"items\":[{\"item\":{\"type\":\"track\",\"id\":\"abc\",\"name\":\"Song\",\"artists\":[],\"album\":{\"name\":\"Album\"},\"duration_ms\":65000}},{\"item\":null}],\"next\":\"next page\"}"));
var playlist = await api.PlaylistTracksAsync("1234567890123456789012", 10, CancellationToken.None);
Check(playlist.HasMore && playlist.Items.Count == 1 && playlist.Offset == 10, "2026 playlist item shape and null filtering");
handler.Responses.Enqueue(JsonResponse("""
{"items":[{"id":"1234567890123456789012","name":"Evening records","tracks":{"total":12},"owner":{"display_name":"Listener"},"images":[{"url":"https://i.scdn.co/large","width":640},{"url":"https://i.scdn.co/cover","width":300},{"url":"https://i.scdn.co/small","width":64}]},{"id":"empty","name":"No artwork","images":null},{"id":"unknown","name":"Unknown size","images":[{"url":"http://example.com/insecure","width":300},{"url":"https://i.scdn.co/unknown","width":null}]}],"next":null}
"""));
var covers = await api.PlaylistsAsync(0, CancellationToken.None);
Check(covers.Items[0].ArtworkUrl == "https://i.scdn.co/cover" && covers.Items[0].Subtitle == "12 tracks · Listener", "Playlist covers prefer bounded artwork and retain metadata");
Check(covers.Items[1].ArtworkUrl == "" && covers.Items[1].Count == 0, "Missing playlist images and counts have safe fallbacks");
Check(covers.Items[2].ArtworkUrl == "https://i.scdn.co/unknown", "Nullable artwork sizes and insecure URLs handled safely");
handler.Responses.Enqueue(JsonResponse("""
{"items":[{"track":{"type":"track","id":"abc","name":"Song","artists":[],"album":{"name":"Album","images":[{"url":"https://i.scdn.co/album","width":300}]},"duration_ms":65000}}],"next":null}
"""));
var albumCover = await api.SavedTracksAsync(0, CancellationToken.None);
Check(albumCover.Items[0].ArtworkUrl == "https://i.scdn.co/album", "Track album artwork retained for library and player");
Check(search.Items[0].ArtworkUrl == "", "Tracks without artwork retain fallback");
handler.Responses.Enqueue(new HttpResponseMessage(HttpStatusCode.Forbidden));
try { await api.SavedTracksAsync(0, CancellationToken.None); throw new Exception("403 accepted"); }
catch (SpotifyApiException error) { Check(error.StatusCode == 403, "Actionable API access error"); }
var rateLimited = new HttpResponseMessage((HttpStatusCode)429);
rateLimited.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(90));
handler.Responses.Enqueue(rateLimited);
try { await api.SavedTracksAsync(0, CancellationToken.None); throw new Exception("Long rate limit ignored"); }
catch (SpotifyApiException error) { Check(error.StatusCode == 429, "Respect long rate limit without retry storm"); }
await auth.DisconnectAsync(CancellationToken.None);
Check(tokenStore.Value is null && !auth.IsConnected, "Disconnect deletes saved credentials");

if (args.Contains("--native"))
{
    await using var engine = new NativeEngine();
    Check(await engine.CheckBridgeAsync(CancellationToken.None) == 2, "C#/Rust diagnostic round trip");
    try { await engine.RequestPlaybackAsync(CancellationToken.None); throw new Exception("Unavailable playback reported success"); }
    catch (NotSupportedException) { passed++; }
    try { await engine.ResumeAsync(CancellationToken.None); throw new Exception("Playback without a session accepted"); }
    catch (InvalidOperationException) { passed++; }
    try { await engine.LoadTrackAsync("invalid", CancellationToken.None); throw new Exception("Malformed track accepted"); }
    catch (ArgumentException) { passed++; }
    try { await engine.SetVolumeAsync(101, CancellationToken.None); throw new Exception("Unbounded volume accepted"); }
    catch (ArgumentOutOfRangeException) { passed++; }
    using (var canceled = new CancellationTokenSource())
    {
        canceled.Cancel();
        try { await engine.CheckBridgeAsync(canceled.Token); throw new Exception("Canceled request accepted"); }
        catch (OperationCanceledException) { passed++; }
    }
    var tasks = Enumerable.Range(0, 16).Select(_ => engine.CheckBridgeAsync(CancellationToken.None)).ToArray();
    Check((await Task.WhenAll(tasks)).All(v => v == 2), "Concurrent native completions");
    await engine.DisposeAsync();
    try { await engine.CheckBridgeAsync(CancellationToken.None); throw new Exception("Disposed engine accepted command"); }
    catch (ObjectDisposedException) { passed++; }
}
Console.WriteLine($"PASS: {passed} checks");
return;

static HttpResponseMessage JsonResponse(string value) => new(HttpStatusCode.OK) { Content = new StringContent(value, Encoding.UTF8, "application/json") };
sealed class MemoryTokenStore(Tokens? initial) : ITokenStore
{
    public Tokens? Value = initial;
    public Task<Tokens?> LoadAsync(string clientId, CancellationToken ct) => Task.FromResult(Value);
    public Task SaveAsync(Tokens value, CancellationToken ct) { Value = value; return Task.CompletedTask; }
    public Task DeleteAsync(string clientId, CancellationToken ct) { Value = null; return Task.CompletedTask; }
}
sealed class FakeHandler : HttpMessageHandler
{
    public readonly Queue<HttpResponseMessage> Responses = new();
    public readonly List<string> Requests = [];
    public readonly List<string?> Bearers = [];
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested(); Requests.Add(request.RequestUri!.AbsoluteUri); Bearers.Add(request.Headers.Authorization?.Parameter);
        return Task.FromResult(Responses.Dequeue());
    }
}
