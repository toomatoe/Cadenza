using System.Net;
using Cadenza.Core.Exceptions;
using Cadenza.Core.Models.Social;
using Cadenza.Core.Services.Social;
using Cadenza.Server;
using Cadenza.Server.Models;
using Cadenza.UnitTests.Fixtures;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Cadenza.UnitTests.Social;

/// <summary>
/// Runs the real Friends server on a random local port with a temporary database and drives it with the real
/// client. The server is shared by the class; each test creates its own people (invited by the owner) so the
/// tests don't depend on each other's order. Account creation is rate limited per address, so keep the number
/// of accounts these tests create well under <see cref="RateLimitPolicies.SlowActionsPerHour"/>.
/// </summary>
[TestClass]
public sealed class SocialServerTests
{
    private const string OwnerInvite = "OWNER-CODE1";
    private const string OwnerName = "owner";
    private const string TrackId = "4uLU6hMCjMI75M1A2tKUQC";

    private static readonly TimeSpan _liveTimeout = TimeSpan.FromSeconds(10);

    private static DirectoryInfo _folder = null!;
    private static WebApplication _app = null!;
    private static HttpClient _http = null!;
    private static Uri _server = null!;
    private static SocialClient _owner = null!;

    [ClassInitialize]
    public static async Task StartServerAsync(TestContext context)
    {
        var cancellationToken = context.CancellationTokenSource.Token;
        _folder = Directory.CreateTempSubdirectory("cadenza-social-");
        _app = ServerApp.Build([], builder =>
        {
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Configuration["Cadenza:Database"] = Path.Combine(_folder.FullName, "test.db");
            builder.Configuration["Cadenza:BootstrapInvite"] = OwnerInvite;
            builder.Configuration["Cadenza:MusicFolder"] = Path.Combine(_folder.FullName, "music");
            builder.Logging.ClearProviders();
        });
        await _app.StartAsync(cancellationToken);
        _server = new Uri(_app.Urls.First() + "/");
        _http = new HttpClient();

        // The owner joins with the bootstrap invite, typed in lower case.
        var owner = await Client().CreateAccountAsync(new CreateAccountRequest(OwnerInvite.ToLowerInvariant(), OwnerName, "Owner"), cancellationToken);
        _owner = Client(owner.Token);
    }

    [ClassCleanup]
    public static async Task StopServerAsync()
    {
        _http.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
        SqliteConnection.ClearAllPools();
        _folder.Delete(recursive: true);
    }

    public TestContext TestContext { get; set; } = null!;

    private CancellationToken Token => TestContext.CancellationTokenSource.Token;

    [TestMethod]
    public async Task OwnerJoinsWithTheBootstrapInviteCaseInsensitively()
    {
        var me = await _owner.MeAsync(Token);

        Assert.AreEqual(OwnerName, me.Username, "The owner joins with the bootstrap invite (case-insensitive)");
    }

    [TestMethod]
    public async Task WrongInviteCodeIsRefused() =>
        await Assert.ThrowsAsync<SocialException>(
            () => Client().CreateAccountAsync(new CreateAccountRequest("WRONG-CODE0", "wrong_code", "Wrong"), Token),
            "A wrong invite code is refused");

    [TestMethod]
    public async Task InvalidUsernameIsRefused()
    {
        var invite = await _owner.CreateInviteAsync(Token);

        await Assert.ThrowsAsync<SocialException>(
            () => Client().CreateAccountAsync(new CreateAccountRequest(invite.Code, "Bad Name", "Bad"), Token),
            "An invalid username is refused");
    }

    [TestMethod]
    public async Task InvitesAreSingleUse() =>
        await Assert.ThrowsAsync<SocialException>(
            () => Client().CreateAccountAsync(new CreateAccountRequest(OwnerInvite, "again", "Again"), Token),
            "Invites are single use");

    [TestMethod]
    public async Task JoiningByInviteBefriendsTheInviter()
    {
        var (dana, _) = await JoinAsync("dana");

        var friends = await dana.FriendsAsync(Token);

        Assert.Contains(friend => friend.Username == OwnerName, friends.Friends, "Joining by invite befriends the inviter");
    }

    [TestMethod]
    public async Task UnknownTokenIsSignedOut()
    {
        var signedOut = await CaptureAsync(() => Client("not-a-token").FriendsAsync(Token));

        Assert.IsTrue(signedOut?.IsSignedOut, "An unknown token is signed out");
    }

    /// <summary>Two people invited by the owner start as strangers, become friends, then chat live and page through history.</summary>
    [TestMethod]
    public async Task FriendsChatLiveAndKeepTheirHistory()
    {
        var (bob, bobJoin) = await JoinAsync("bob");
        var (carol, carolJoin) = await JoinAsync("carol");

        await Assert.ThrowsAsync<SocialException>(
            () => bob.SendAsync(carolJoin.User.Id, Text("hi"), Token), "Strangers can't message each other");
        await Assert.ThrowsAsync<SocialException>(() => bob.MessagesAsync(carolJoin.User.Id, null, Token), "Strangers can't read a conversation");

        var outgoing = await bob.AddFriendAsync("carol", Token);
        Assert.Contains(user => user.Id == carolJoin.User.Id, outgoing.Outgoing, "A friend request shows as outgoing");
        Assert.Contains(user => user.Id == bobJoin.User.Id, (await carol.FriendsAsync(Token)).Incoming, "…and as incoming for the other person");
        Assert.Contains(user => user.Id == bobJoin.User.Id, (await carol.AcceptAsync(bobJoin.User.Id, Token)).Friends, "Accepting makes you friends");

        var received = new TaskCompletionSource<SocialEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        var connected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using (var live = new SocialConnection(_server, carolJoin.Token))
        {
            live.ConnectionChanged += (_, up) =>
            {
                if (up)
                    connected.TrySetResult();
            };
            live.EventReceived += (_, socialEvent) => received.TrySetResult(socialEvent);
            live.Start();
            await connected.Task.WaitAsync(_liveTimeout, Token);
            var sent = await bob.SendAsync(carolJoin.User.Id, Text(" hello carol "), Token);
            var pushed = await received.Task.WaitAsync(_liveTimeout, Token);
            Assert.IsTrue(
                pushed.Type == SocialEvent.MessageType && pushed.Message?.Id == sent.Id && pushed.Message.Text == "hello carol",
                "New messages arrive live over the WebSocket, trimmed");
        }

        await Assert.ThrowsAsync<SocialException>(
            () => bob.SendAsync(carolJoin.User.Id, new SendMessageRequest(ChatMessageKind.Share, "", "bad"), Token), "Bad track IDs are refused");
        var gift = await bob.SendAsync(carolJoin.User.Id, new SendMessageRequest(ChatMessageKind.Gift, "", TrackId), Token);
        var history = await carol.MessagesAsync(bobJoin.User.Id, null, Token);
        Assert.IsTrue(history.Count == 2 && history[^1].Id == gift.Id && history[^1].TrackId == TrackId, "History is oldest first and keeps only the track ID");
        Assert.IsTrue((await carol.MessagesAsync(bobJoin.User.Id, gift.Id, Token)).All(message => message.Id < gift.Id), "History pages backwards");
    }

    [TestMethod]
    public async Task YouCanOnlyReportMessagesSentToYou()
    {
        var (erin, _, frank, frankJoin) = await MakeFriendsAsync("erin", "frank");
        var gift = await erin.SendAsync(frankJoin.User.Id, new SendMessageRequest(ChatMessageKind.Gift, "", TrackId), Token);

        await frank.ReportAsync(gift.Id, "testing reports", Token);

        await Assert.ThrowsAsync<SocialException>(() => erin.ReportAsync(gift.Id, "my own", Token), "You can only report messages sent to you");
    }

    [TestMethod]
    public async Task BlockedPeopleCannotMessageYouAndTheBlockLooksLikeAMissingUsername()
    {
        var (gina, _, hugo, hugoJoin) = await MakeFriendsAsync("gina", "hugo");
        var ginaId = (await gina.MeAsync(Token)).Id;

        await hugo.BlockAsync(ginaId, Token);

        await Assert.ThrowsAsync<SocialException>(
            () => gina.SendAsync(hugoJoin.User.Id, Text("hey"), Token), "Blocked people can't message you");
        var hidden = await CaptureAsync(() => gina.AddFriendAsync("hugo", Token));
        var missing = await CaptureAsync(() => gina.AddFriendAsync("nobody", Token));
        Assert.AreEqual(404, hidden?.StatusCode, "A block looks like a username that doesn't exist");
        Assert.AreEqual(missing?.Message, hidden?.Message, "A block looks like a username that doesn't exist");
    }

    [TestMethod]
    public async Task DeletingYourAccountRevokesItsTokenAndRemovesYouFromFriendsLists()
    {
        var (ivan, _) = await JoinAsync("ivan");

        await ivan.DeleteAccountAsync(Token);

        Assert.IsTrue((await CaptureAsync(() => ivan.MeAsync(Token)))?.IsSignedOut, "Deleting your account revokes its token");
        Assert.DoesNotContain(friend => friend.Username == "ivan", (await _owner.FriendsAsync(Token)).Friends, "…and removes you from friends lists");
    }

    [TestMethod]
    public async Task SongsUploadToTheSharedLibraryAndCanBeFoundDownloadedAndRemoved()
    {
        var (judy, _) = await JoinAsync("judy");
        var bytes = MusicSamples.Mp3("Night Drive", "Neon Owls");
        var song = Path.Combine(_folder.FullName, "night.mp3");
        await File.WriteAllBytesAsync(song, bytes, Token);

        var uploaded = await _owner.UploadMusicAsync(song, new UploadMetadata("Night Drive", "Neon Owls", "", 26_122), Token);
        Assert.IsTrue(uploaded is { Title: "Night Drive", Format: "mp3", UploadedBy: OwnerName }, "Songs upload to the shared library");

        var again = await judy.UploadMusicAsync(song, new UploadMetadata("Copy", "Someone", "", 0), Token);
        Assert.AreEqual(uploaded.Id, again.Id, "The same file uploaded twice is stored once");

        var found = await judy.SearchMusicAsync("neon night", Token);
        Assert.HasCount(1, found, "Anyone on the server can find a song by title and artist");
        Assert.AreEqual(uploaded.Id, found[0].Id, "Anyone on the server can find a song by title and artist");

        var copy = Path.Combine(_folder.FullName, "copy.mp3");
        await judy.DownloadMusicAsync(uploaded.Id, copy, Token);
        Assert.IsTrue((await File.ReadAllBytesAsync(copy, Token)).SequenceEqual(bytes), "Songs download byte for byte");

        using var range = new HttpRequestMessage(HttpMethod.Get, new Uri(_server, $"api/music/{uploaded.Id}/file"));
        range.Headers.Authorization = new("Bearer", judy.Token);
        range.Headers.Range = new(0, 9);
        using var partial = await _http.SendAsync(range, Token);
        Assert.AreEqual(HttpStatusCode.PartialContent, partial.StatusCode, "Song files support range requests for seeking");
        Assert.HasCount(10, await partial.Content.ReadAsByteArrayAsync(Token), "Song files support range requests for seeking");

        Assert.AreEqual(403, (await CaptureAsync(() => judy.DeleteMusicAsync(uploaded.Id, Token)))?.StatusCode, "Only the uploader can remove a song");
        await _owner.DeleteMusicAsync(uploaded.Id, Token);
        Assert.IsEmpty(await judy.SearchMusicAsync("neon", Token), "Removed songs leave the library");
    }

    [TestMethod]
    public async Task LibrarySearchTreatsPercentLiterally() =>
        Assert.IsEmpty(await _owner.SearchMusicAsync("100%", Token), "Search treats % literally");

    [TestMethod]
    public async Task FilesThatAreNotMusicAreRefused()
    {
        var fake = Path.Combine(_folder.FullName, "fake.mp3");
        await File.WriteAllTextAsync(fake, "<html>not music</html>", Token);

        var refused = await CaptureAsync(() => _owner.UploadMusicAsync(fake, new UploadMetadata("Fake", "Nobody", "", 0), Token));

        Assert.AreEqual(415, refused?.StatusCode, "Files that aren't music are refused");
    }

    private static SocialClient Client(string? token = null) => new(_http, _server) { Token = token };

    private static SendMessageRequest Text(string text) => new(ChatMessageKind.Text, text, null);

    private static async Task<SocialException?> CaptureAsync(Func<Task> action)
    {
        try
        {
            await action();

            return null;
        }
        catch (SocialException error)
        {
            return error;
        }
    }

    /// <summary>Creates an account with a fresh invite from the owner, which also makes the new person the owner's friend.</summary>
    private async Task<(SocialClient Client, AccountCreated Account)> JoinAsync(string username)
    {
        var invite = await _owner.CreateInviteAsync(Token);
        var account = await Client().CreateAccountAsync(new CreateAccountRequest(invite.Code, username, username), Token);

        return (Client(account.Token), account);
    }

    private async Task<(SocialClient First, AccountCreated FirstAccount, SocialClient Second, AccountCreated SecondAccount)> MakeFriendsAsync(
        string first,
        string second)
    {
        var (firstClient, firstAccount) = await JoinAsync(first);
        var (secondClient, secondAccount) = await JoinAsync(second);
        await firstClient.AddFriendAsync(second, Token);
        await secondClient.AcceptAsync(firstAccount.User.Id, Token);

        return (firstClient, firstAccount, secondClient, secondAccount);
    }
}
