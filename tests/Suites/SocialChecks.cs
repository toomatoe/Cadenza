using Cadenza.Core.Social;
using Cadenza.Server;
using Cadenza.Tests.Fixtures;
using Cadenza.Tests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;

namespace Cadenza.Tests.Suites;

/// <summary>Runs the real Friends server on a random local port and drives it with the real client.</summary>
internal static class SocialChecks
{
    private const string OwnerInvite = "OWNER-CODE1";
    private const string TrackId = "4uLU6hMCjMI75M1A2tKUQC";

    public static async Task RunAsync(CheckRunner checks)
    {
        CheckRules(checks);

        var folder = Directory.CreateTempSubdirectory("cadenza-social-");
        var app = ServerApp.Build([], builder =>
        {
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Configuration["Cadenza:Database"] = Path.Combine(folder.FullName, "test.db");
            builder.Configuration["Cadenza:BootstrapInvite"] = OwnerInvite;
            builder.Configuration["Cadenza:MusicFolder"] = Path.Combine(folder.FullName, "music");
            builder.Logging.ClearProviders();
        });
        await app.StartAsync();
        try
        {
            var address = app.Urls.First();
            await CheckServerAsync(checks, new Uri(address + "/"));
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            folder.Delete(recursive: true);
        }
    }

    private static void CheckRules(CheckRunner checks)
    {
        checks.Check(SocialRules.IsValidUsername("mochi_42") && !SocialRules.IsValidUsername("Mochi") && !SocialRules.IsValidUsername("ab"),
            "Usernames are 3–20 lowercase letters, digits or underscores");
        checks.Check(SocialRules.ValidateMessage(new SendMessageRequest(ChatMessageKind.Text, "  ", null)) is not null, "Blank messages are rejected");
        checks.Check(SocialRules.ValidateMessage(new SendMessageRequest(ChatMessageKind.Gift, "", "not-an-id")) is not null, "Gifts need a Spotify track ID");
        checks.Check(SocialRules.ValidateMessage(new SendMessageRequest(ChatMessageKind.Text, new string('a', SocialRules.MaxMessageLength + 1), null)) is not null,
            "Long messages are rejected");
        checks.Check(TokenHasher.Hash("a") != TokenHasher.Hash("b") && TokenHasher.NewToken() != TokenHasher.NewToken(), "Tokens are random and hashed");
    }

    private static async Task CheckServerAsync(CheckRunner checks, Uri server)
    {
        using var http = new HttpClient();
        var ct = CancellationToken.None;
        SocialClient Client(string? token = null) => new(http, server) { Token = token };

        await checks.ThrowsAsync<SocialException>(
            () => Client().CreateAccountAsync(new CreateAccountRequest("WRONG-CODE0", "owner", "Owner"), ct), "A wrong invite code is refused");
        await checks.ThrowsAsync<SocialException>(
            () => Client().CreateAccountAsync(new CreateAccountRequest(OwnerInvite, "Bad Name", "Owner"), ct), "An invalid username is refused");

        var owner = Client((await Client().CreateAccountAsync(new CreateAccountRequest(OwnerInvite.ToLowerInvariant(), "owner", "Owner"), ct)).Token);
        checks.Check((await owner.MeAsync(ct)).Username == "owner", "The owner joins with the bootstrap invite (case-insensitive)");
        await checks.ThrowsAsync<SocialException>(
            () => Client().CreateAccountAsync(new CreateAccountRequest(OwnerInvite, "again", "Again"), ct), "Invites are single use");

        var bobJoin = await Client().CreateAccountAsync(new CreateAccountRequest((await owner.CreateInviteAsync(ct)).Code, "bob", "Bob"), ct);
        var bob = Client(bobJoin.Token);
        checks.Check((await bob.FriendsAsync(ct)).Friends.Any(friend => friend.Username == "owner"), "Joining by invite befriends the inviter");

        var carolJoin = await Client().CreateAccountAsync(new CreateAccountRequest((await owner.CreateInviteAsync(ct)).Code, "carol", "Carol"), ct);
        var carol = Client(carolJoin.Token);
        await checks.ThrowsAsync<SocialException>(
            () => bob.SendAsync(carolJoin.User.Id, new SendMessageRequest(ChatMessageKind.Text, "hi", null), ct), "Strangers can't message each other");
        await checks.ThrowsAsync<SocialException>(() => bob.MessagesAsync(carolJoin.User.Id, null, ct), "Strangers can't read a conversation");

        var outgoing = await bob.AddFriendAsync("carol", ct);
        checks.Check(outgoing.Outgoing.Any(user => user.Id == carolJoin.User.Id), "A friend request shows as outgoing");
        checks.Check((await carol.FriendsAsync(ct)).Incoming.Any(user => user.Id == bobJoin.User.Id), "…and as incoming for the other person");
        checks.Check((await carol.AcceptAsync(bobJoin.User.Id, ct)).Friends.Any(user => user.Id == bobJoin.User.Id), "Accepting makes you friends");

        var received = new TaskCompletionSource<SocialEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        var connected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using (var live = new SocialConnection(server, carolJoin.Token))
        {
            live.ConnectionChanged += (_, up) => { if (up) connected.TrySetResult(); };
            live.EventReceived += (_, socialEvent) => received.TrySetResult(socialEvent);
            live.Start();
            await connected.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var sent = await bob.SendAsync(carolJoin.User.Id, new SendMessageRequest(ChatMessageKind.Text, " hello carol ", null), ct);
            var pushed = await received.Task.WaitAsync(TimeSpan.FromSeconds(10));
            checks.Check(pushed.Type == SocialEvent.MessageType && pushed.Message?.Id == sent.Id && pushed.Message.Text == "hello carol",
                "New messages arrive live over the WebSocket, trimmed");
        }

        await checks.ThrowsAsync<SocialException>(
            () => bob.SendAsync(carolJoin.User.Id, new SendMessageRequest(ChatMessageKind.Share, "", "bad"), ct), "Bad track IDs are refused");
        var gift = await bob.SendAsync(carolJoin.User.Id, new SendMessageRequest(ChatMessageKind.Gift, "", TrackId), ct);
        var history = await carol.MessagesAsync(bobJoin.User.Id, null, ct);
        checks.Check(history.Count == 2 && history[^1].Id == gift.Id && history[^1].TrackId == TrackId, "History is oldest first and keeps only the track ID");
        checks.Check((await carol.MessagesAsync(bobJoin.User.Id, gift.Id, ct)).All(message => message.Id < gift.Id), "History pages backwards");

        await CheckLibraryAsync(checks, http, server, owner, carol);

        await carol.ReportAsync(gift.Id, "testing reports", ct);
        await checks.ThrowsAsync<SocialException>(() => bob.ReportAsync(gift.Id, "my own", ct), "You can only report messages sent to you");

        await carol.BlockAsync(bobJoin.User.Id, ct);
        await checks.ThrowsAsync<SocialException>(
            () => bob.SendAsync(carolJoin.User.Id, new SendMessageRequest(ChatMessageKind.Text, "hey", null), ct), "Blocked people can't message you");
        var hidden = await CaptureAsync(() => bob.AddFriendAsync("carol", ct));
        var missing = await CaptureAsync(() => bob.AddFriendAsync("nobody", ct));
        checks.Check(hidden?.StatusCode == 404 && hidden.Message == missing?.Message, "A block looks like a username that doesn't exist");

        var signedOut = await CaptureAsync(() => Client("not-a-token").FriendsAsync(ct));
        checks.Check(signedOut?.IsSignedOut == true, "An unknown token is signed out");

        await bob.DeleteAccountAsync(ct);
        checks.Check((await CaptureAsync(() => bob.MeAsync(ct)))?.IsSignedOut == true, "Deleting your account revokes its token");
        checks.Check((await owner.FriendsAsync(ct)).Friends.All(friend => friend.Username != "bob"), "…and removes you from friends lists");
    }

    private static async Task CheckLibraryAsync(CheckRunner checks, HttpClient http, Uri server, SocialClient owner, SocialClient carol)
    {
        var ct = CancellationToken.None;
        var folder = Directory.CreateTempSubdirectory("cadenza-upload-");
        try
        {
            var bytes = MusicSamples.Mp3("Night Drive", "Neon Owls");
            var song = Path.Combine(folder.FullName, "night.mp3");
            await File.WriteAllBytesAsync(song, bytes, ct);
            var uploaded = await owner.UploadMusicAsync(song, new UploadMetadata("Night Drive", "Neon Owls", "", 26_122), ct);
            checks.Check(uploaded is { Title: "Night Drive", Format: "mp3", UploadedBy: "owner" }, "Songs upload to the shared library");

            var again = await carol.UploadMusicAsync(song, new UploadMetadata("Copy", "Someone", "", 0), ct);
            checks.Check(again.Id == uploaded.Id, "The same file uploaded twice is stored once");

            var found = await carol.SearchMusicAsync("neon night", ct);
            checks.Check(found.Count == 1 && found[0].Id == uploaded.Id, "Anyone on the server can find a song by title and artist");
            checks.Check((await carol.SearchMusicAsync("100%", ct)).Count == 0, "Search treats % literally");

            var copy = Path.Combine(folder.FullName, "copy.mp3");
            await carol.DownloadMusicAsync(uploaded.Id, copy, ct);
            checks.Check((await File.ReadAllBytesAsync(copy, ct)).SequenceEqual(bytes), "Songs download byte for byte");

            using var range = new HttpRequestMessage(HttpMethod.Get, new Uri(server, $"api/music/{uploaded.Id}/file"));
            range.Headers.Authorization = new("Bearer", carol.Token);
            range.Headers.Range = new(0, 9);
            using var partial = await http.SendAsync(range, ct);
            checks.Check(partial.StatusCode == System.Net.HttpStatusCode.PartialContent && (await partial.Content.ReadAsByteArrayAsync(ct)).Length == 10,
                "Song files support range requests for seeking");

            var fake = Path.Combine(folder.FullName, "fake.mp3");
            await File.WriteAllTextAsync(fake, "<html>not music</html>", ct);
            var refused = await CaptureAsync(() => owner.UploadMusicAsync(fake, new UploadMetadata("Fake", "Nobody", "", 0), ct));
            checks.Check(refused?.StatusCode == 415, "Files that aren't music are refused");

            checks.Check((await CaptureAsync(() => carol.DeleteMusicAsync(uploaded.Id, ct)))?.StatusCode == 403, "Only the uploader can remove a song");
            await owner.DeleteMusicAsync(uploaded.Id, ct);
            checks.Check((await carol.SearchMusicAsync("neon", ct)).Count == 0, "Removed songs leave the library");
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

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
}
