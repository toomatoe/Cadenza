using System.Collections.ObjectModel;
using System.Globalization;
using Cadenza.Core.Exceptions;
using Cadenza.Core.Interfaces;
using Cadenza.Core.Models;
using Cadenza.Core.Models.Social;
using Cadenza.Core.Services.Social;
using Cadenza.Desktop.Models;

namespace Cadenza.Desktop.ViewModels;

/// <summary>
/// Cadenza Friends: joining, friends and requests, the open conversation, sharing and live events.
/// Nothing is created until you join (or a saved account is restored); then one idle WebSocket is the
/// only standing cost. Server errors become notices — Friends never blocks music.
/// </summary>
public sealed class FriendsViewModel : ObservableObject, IAsyncDisposable
{
    private const int MaxCachedSharedTracks = 200;
    private const string PickAFriend = "Pick a friend to start chatting";
    private const string SignedOut = "Your Cadenza Friends sign-in ended. Join again with a new invite.";

    private readonly Func<Uri, ISocialClient> _createClient;
    private readonly Func<Uri, string, ISocialConnection> _createConnection;
    private readonly IFriendsTokenStore _tokens;
    private readonly Func<string, CancellationToken, Task<Track?>> _findTrack;
    private readonly CancellationToken _lifetime;
    private readonly Dictionary<string, Track> _sharedTracks = [];
    private ISocialConnection? _live;
    private SocialUser? _me;
    private SocialUser? _openFriend;
    private bool _isJoined;
    private string _meName = "";
    private string _status = "";
    private int _unread;

    /// <param name="createClient">Creates a server client for an address.</param>
    /// <param name="createConnection">Creates the live socket for an address and device token.</param>
    /// <param name="tokens">Where the device token is kept.</param>
    /// <param name="findTrack">Looks up a shared Spotify track by ID (null when unavailable).</param>
    /// <param name="lifetime">Cancelled when the window closes.</param>
    public FriendsViewModel(
        Func<Uri, ISocialClient> createClient,
        Func<Uri, string, ISocialConnection> createConnection,
        IFriendsTokenStore tokens,
        Func<string, CancellationToken, Task<Track?>> findTrack,
        CancellationToken lifetime)
    {
        _createClient = createClient;
        _createConnection = createConnection;
        _tokens = tokens;
        _findTrack = findTrack;
        _lifetime = lifetime;
    }

    /// <summary>A view model message for the notice bar.</summary>
    public event EventHandler<Notice>? NoticeRaised;

    /// <summary>A friend sent a track for your queue and auto-queue is on: add it.</summary>
    public event EventHandler<Track>? GiftToQueue;

    /// <summary>A message was added to the open conversation (the view scrolls to it).</summary>
    public event EventHandler? ConversationChanged;

    /// <summary>The joined account's client, or null; also used by the shared music library.</summary>
    public ISocialClient? Client { get; private set; }

    public ObservableCollection<SocialUser> Friends { get; } = [];

    public ObservableCollection<SocialUser> Requests { get; } = [];

    public ObservableCollection<ChatItem> Messages { get; } = [];

    /// <summary>Set by the window while the Friends page is visible.</summary>
    public bool IsPageVisible { get; set; }

    /// <summary>Add friends' queue gifts straight to Up next instead of asking (from preferences).</summary>
    public bool AutoQueueGifts { get; set; }

    public bool IsJoined
    {
        get => _isJoined;
        private set => SetProperty(ref _isJoined, value);
    }

    /// <summary>"Display name · @username" for the signed-in account.</summary>
    public string MeName
    {
        get => _meName;
        private set => SetProperty(ref _meName, value);
    }

    /// <summary>Online state, friend count and pending requests.</summary>
    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    public int Unread
    {
        get => _unread;
        private set
        {
            if (SetProperty(ref _unread, value))
            {
                OnPropertyChanged(nameof(HasUnread));
                OnPropertyChanged(nameof(UnreadText));
            }
        }
    }

    public bool HasUnread => _unread > 0;

    public string UnreadText => _unread.ToString(CultureInfo.CurrentCulture);

    /// <summary>The friend whose conversation is open, or null.</summary>
    public SocialUser? OpenFriend
    {
        get => _openFriend;
        private set
        {
            if (SetProperty(ref _openFriend, value))
            {
                OnPropertyChanged(nameof(ChatTitle));
                OnPropertyChanged(nameof(CanCompose));
            }
        }
    }

    public string ChatTitle => _openFriend?.DisplayName ?? PickAFriend;

    public bool CanCompose => _openFriend is not null;

    /// <summary>Reconnects at startup if you joined on this PC before.</summary>
    /// <param name="server">The saved server address (from preferences).</param>
    public async Task RestoreAsync(string server)
    {
        if (server.Length == 0 || !Uri.TryCreate(server, UriKind.Absolute, out var address))
            return;

        string? token;
        try
        {
            token = _tokens.Load();
        }
        catch (InvalidOperationException)
        {
            return;
        }

        if (token is null)
            return;

        Client = _createClient(address);
        Client.Token = token;
        IsJoined = true;
        try
        {
            SetMe(await Client.MeAsync(_lifetime));
        }
        catch (SocialException error) when (error.IsSignedOut)
        {
            Forget();
            return;
        }
        catch (SocialException)
        {
            // Offline: keep the account; the live connection retries in the background.
        }

        StartLive(address, token);
    }

    /// <summary>Joins a server with an invite code.</summary>
    /// <returns>The normalised server address to save in preferences, or null if joining failed.</returns>
    public async Task<string?> JoinAsync(string serverText, string inviteCode, string username, string displayName)
    {
        if (!Uri.TryCreate(serverText.Trim(), UriKind.Absolute, out var address) ||
            (address.Scheme != Uri.UriSchemeHttps && !address.IsLoopback))
        {
            Raise("Enter the server's https:// address.", NoticeLevel.Warning);
            return null;
        }

        address = new Uri(address.GetLeftPart(UriPartial.Path).TrimEnd('/') + "/");
        var client = _createClient(address);
        string? joined = null;
        await RunAsync(async cancellationToken =>
        {
            var created = await client.CreateAccountAsync(new CreateAccountRequest(inviteCode, username.Trim(), displayName), cancellationToken);
            _tokens.Save(created.Token);
            client.Token = created.Token;
            Client = client;
            IsJoined = true;
            SetMe(created.User);
            StartLive(address, created.Token);
            Raise($"Welcome, {created.User.DisplayName}! Whoever invited you is already on your friends list.", NoticeLevel.Success);
            joined = address.ToString();
            await OpenAsync();
        });

        return joined;
    }

    /// <summary>The Friends page opened: clears the unread count and loads friends.</summary>
    public async Task OpenAsync()
    {
        if (Client is null)
            return;

        Unread = 0;
        await RunAsync(async cancellationToken =>
        {
            if (_me is null)
                SetMe(await Client.MeAsync(cancellationToken));
            Apply(await Client.FriendsAsync(cancellationToken));
        });
    }

    /// <summary>Creates a single-use invite.</summary>
    /// <returns>A ready-to-send invite text (server and code), or null if it failed.</returns>
    public async Task<string?> CreateInviteAsync()
    {
        string? text = null;
        await RunAsync(async cancellationToken =>
        {
            var invite = await Client!.CreateInviteAsync(cancellationToken);
            text = $"Join me on Cadenza Friends. Server: {Client.Server}  Invite code: {invite.Code}";
            Raise($"Invite code {invite.Code} copied, with the server address. It works once, for 7 days.", NoticeLevel.Success);
        });

        return text;
    }

    /// <returns>Whether the request went through (the view clears the box).</returns>
    public async Task<bool> AddFriendAsync(string username)
    {
        username = username.Trim();
        if (!SocialRules.IsValidUsername(username))
        {
            Raise("Usernames are 3–20 lowercase letters, digits or underscores.", NoticeLevel.Warning);
            return false;
        }

        var added = false;
        await RunAsync(async cancellationToken =>
        {
            var list = await Client!.AddFriendAsync(username, cancellationToken);
            Apply(list);
            added = true;
            Raise(list.Friends.Any(friend => friend.Username == username) ? $"You and @{username} are now friends." : $"Friend request sent to @{username}.");
        });

        return added;
    }

    public Task AcceptAsync(SocialUser user) => RunAsync(async cancellationToken => Apply(await Client!.AcceptAsync(user.Id, cancellationToken)));

    public Task DeclineAsync(SocialUser user) => RunAsync(async cancellationToken => Apply(await Client!.RemoveAsync(user.Id, cancellationToken)));

    /// <summary>Opens a conversation and loads its latest messages.</summary>
    public async Task OpenConversationAsync(SocialUser friend)
    {
        if (friend.Id == _openFriend?.Id)
            return;

        OpenFriend = friend;
        Unread = 0;
        Messages.Clear();
        await RunAsync(async cancellationToken =>
        {
            foreach (var message in await Client!.MessagesAsync(friend.Id, null, cancellationToken))
                AddMessage(message);
            ConversationChanged?.Invoke(this, EventArgs.Empty);
        });
    }

    /// <returns>Whether it was sent (the view clears the composer).</returns>
    public Task<bool> SendTextAsync(string text) =>
        string.IsNullOrWhiteSpace(text) ? Task.FromResult(false) : SendAsync(new SendMessageRequest(ChatMessageKind.Text, text, null));

    /// <summary>Shares the playing track, or sends it to the friend's queue.</summary>
    /// <param name="playing">The playing track, or null.</param>
    /// <param name="kind"><see cref="ChatMessageKind.Share"/> or <see cref="ChatMessageKind.Gift"/>.</param>
    /// <param name="note">Text to send with a share.</param>
    /// <returns>Whether a share with the note was sent (the view clears the composer).</returns>
    public async Task<bool> SendTrackAsync(Track? playing, ChatMessageKind kind, string note)
    {
        if (playing is null)
        {
            Raise("Play something first, then share it.");
            return false;
        }

        // Library songs have no Spotify ID, so friends get a message pointing them to search instead.
        if (!playing.IsSpotify)
        {
            await SendAsync(new SendMessageRequest(ChatMessageKind.Text, $"Listen to {playing.Name} by {playing.Artist}. It's in our Cadenza library; search for it.", null));
            return false;
        }

        Remember(playing);
        var sent = await SendAsync(new SendMessageRequest(kind, kind == ChatMessageKind.Share ? note.Trim() : "", playing.Id));

        return sent && kind == ChatMessageKind.Share;
    }

    public async Task ReportAsync(ChatItem item)
    {
        if (item.Mine)
        {
            Raise("You can report messages your friends send you.");
            return;
        }

        await RunAsync(async cancellationToken =>
        {
            await Client!.ReportAsync(item.Message.Id, "Reported from Cadenza", cancellationToken);
            Raise("Thanks. The server owner will review it. You can also block this person from the ⋯ menu.", NoticeLevel.Success);
        });
    }

    public Task UnfriendAsync() =>
        _openFriend is { } friend ? RunAsync(async cancellationToken => Apply(await Client!.RemoveAsync(friend.Id, cancellationToken))) : Task.CompletedTask;

    public Task BlockAsync()
    {
        if (_openFriend is not { } friend)
            return Task.CompletedTask;

        return RunAsync(async cancellationToken =>
        {
            Apply(await Client!.BlockAsync(friend.Id, cancellationToken));
            Raise($"{friend.DisplayName} is blocked. They can't message you or find you.");
        });
    }

    /// <summary>Deletes the account on the server and forgets it here.</summary>
    /// <returns>Whether it was deleted.</returns>
    public async Task<bool> LeaveAsync()
    {
        var left = false;
        await RunAsync(async cancellationToken =>
        {
            await Client!.DeleteAccountAsync(cancellationToken);
            Forget();
            left = true;
            Raise("Your Cadenza Friends account was deleted.", NoticeLevel.Success);
        });

        return left;
    }

    /// <summary>Forgets the account on this PC (token, live connection and lists).</summary>
    public void Forget()
    {
        if (_live is { } live)
        {
            _live = null;
            _ = live.DisposeAsync().AsTask();
        }

        try
        {
            _tokens.Delete();
        }
        catch (InvalidOperationException)
        {
            // Nothing saved, or Windows refused; the token is useless once the account is gone.
        }

        Client = null;
        _me = null;
        IsJoined = false;
        MeName = "";
        Friends.Clear();
        Requests.Clear();
        CloseConversation();
        Unread = 0;
    }

    public async ValueTask DisposeAsync()
    {
        if (_live is not null)
            await _live.DisposeAsync();
    }

    private void StartLive(Uri address, string token)
    {
        _live = _createConnection(address, token);
        _live.EventReceived += OnEvent;
        _live.ConnectionChanged += (_, connected) => Status = connected ? "Online" : "Offline. Retrying…";
        _live.Start();
    }

    private async void OnEvent(object? sender, SocialEvent socialEvent)
    {
        switch (socialEvent.Type)
        {
            case SocialEvent.MessageType when socialEvent.Message is { } message:
                await ReceiveAsync(message);
                break;
            case SocialEvent.FriendRequestType when socialEvent.User is { } user:
                Raise($"{user.DisplayName} (@{user.Username}) wants to be friends. Open Friends to accept.");
                await RefreshIfVisibleAsync();
                break;
            case SocialEvent.FriendAcceptedType or SocialEvent.FriendRemovedType:
                await RefreshIfVisibleAsync();
                break;
        }
    }

    private async Task ReceiveAsync(ChatMessage message)
    {
        var mine = message.FromId == _me?.Id;
        var otherId = mine ? message.ToId : message.FromId;
        if (IsPageVisible && otherId == _openFriend?.Id)
        {
            AddMessage(message);
            ConversationChanged?.Invoke(this, EventArgs.Empty);
        }
        else if (!mine)
            Unread++;

        if (message.Kind != ChatMessageKind.Gift || mine || message.TrackId is not { } trackId)
            return;

        var from = Friends.FirstOrDefault(friend => friend.Id == message.FromId)?.DisplayName ?? "A friend";
        if (AutoQueueGifts && await FindTrackAsync(trackId) is { } track)
        {
            GiftToQueue?.Invoke(this, track);
            Raise($"{from} added {track.Name} to your Up next.");
        }
        else
            Raise($"{from} sent you a track for your queue. Open Friends to add it.");
    }

    private async Task RefreshIfVisibleAsync()
    {
        if (IsPageVisible && Client is not null)
            await RunAsync(async cancellationToken => Apply(await Client.FriendsAsync(cancellationToken)));
    }

    /// <summary>Sends to the open conversation. The server also echoes it over the WebSocket; the duplicate is ignored.</summary>
    private async Task<bool> SendAsync(SendMessageRequest request)
    {
        if (_openFriend is not { } friend || Client is null)
            return false;

        var sent = false;
        await RunAsync(async cancellationToken =>
        {
            AddMessage(await Client.SendAsync(friend.Id, request, cancellationToken));
            ConversationChanged?.Invoke(this, EventArgs.Empty);
            sent = true;
        });

        return sent;
    }

    private void Apply(FriendList list)
    {
        Requests.Clear();
        foreach (var request in list.Incoming)
            Requests.Add(request);

        Friends.Clear();
        foreach (var friend in list.Friends)
            Friends.Add(friend);

        var pending = list.Outgoing.Count == 0 ? "" : $" · waiting on {string.Join(", ", list.Outgoing.Select(user => "@" + user.Username))}";
        Status = (Friends.Count == 1 ? "1 friend" : $"{Friends.Count} friends") + pending;
        if (_openFriend is { } open && list.Friends.All(friend => friend.Id != open.Id))
            CloseConversation();
    }

    private void AddMessage(ChatMessage message)
    {
        if (Messages.Any(item => item.Message.Id == message.Id))
            return;

        var item = new ChatItem(message, message.FromId == _me?.Id);
        Messages.Add(item);
        if (message.TrackId is { } trackId)
            _ = HydrateAsync(item, trackId);
    }

    private async Task HydrateAsync(ChatItem item, string trackId) => item.Track = await FindTrackAsync(trackId);

    /// <summary>Looks up a shared track through your own Spotify session; cached for the session.</summary>
    private async Task<Track?> FindTrackAsync(string trackId)
    {
        if (_sharedTracks.TryGetValue(trackId, out var cached))
            return cached;

        var track = await _findTrack(trackId, _lifetime);
        if (track is not null)
            Remember(track);

        return track;
    }

    private void Remember(Track track)
    {
        if (_sharedTracks.Count >= MaxCachedSharedTracks)
            _sharedTracks.Clear();

        _sharedTracks[track.Id] = track;
    }

    private void SetMe(SocialUser me)
    {
        _me = me;
        MeName = $"{me.DisplayName} · @{me.Username}";
    }

    private void CloseConversation()
    {
        OpenFriend = null;
        Messages.Clear();
    }

    private void Raise(string text, NoticeLevel level = NoticeLevel.Info) => NoticeRaised?.Invoke(this, new Notice(text, level));

    /// <summary>Runs Friends work and turns server errors into notices.</summary>
    private async Task RunAsync(Func<CancellationToken, Task> work)
    {
        try
        {
            await work(_lifetime);
        }
        catch (SocialException error) when (error.IsSignedOut)
        {
            Forget();
            Raise(SignedOut, NoticeLevel.Warning);
        }
        catch (SocialException error)
        {
            Raise(error.Message, NoticeLevel.Warning);
        }
        catch (InvalidOperationException error)
        {
            Raise(error.Message, NoticeLevel.Error);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            // The window is closing.
        }
    }
}
