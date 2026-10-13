using System.Collections.ObjectModel;
using Cadenza.Core.Models;
using Cadenza.Core.Social;
using Cadenza.Desktop.Models;
using Cadenza.Desktop.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;

namespace Cadenza.Desktop.Views;

public sealed partial class MainWindow
{
    private const int MaxCachedSharedTracks = 200;

    // Nothing here exists until you join; then one idle WebSocket is the only standing cost.
    private SocialClient? _social;
    private SocialConnection? _socialLive;
    private SocialUser? _socialMe;
    private SocialUser? _chatFriend;
    private int _unreadMessages;
    private readonly Dictionary<string, Track> _sharedTracks = [];

    public ObservableCollection<SocialUser> FriendsPeople { get; } = [];

    public ObservableCollection<SocialUser> FriendRequests { get; } = [];

    public ObservableCollection<ChatItem> ChatItems { get; } = [];

    /// <summary>Reconnects to Cadenza Friends at startup if you joined on this PC before.</summary>
    private async Task RestoreFriendsAsync()
    {
        if (_preferences.FriendsServer.Length == 0 || !Uri.TryCreate(_preferences.FriendsServer, UriKind.Absolute, out var server))
        {
            return;
        }

        string? token;
        try
        {
            token = WindowsTokenStore.LoadFriendsToken();
        }
        catch (InvalidOperationException)
        {
            return;
        }

        if (token is null)
        {
            return;
        }

        _social = new SocialClient(_http, server) { Token = token };
        try
        {
            _socialMe = await _social.MeAsync(_lifetime.Token);
        }
        catch (SocialException error) when (error.IsSignedOut)
        {
            ForgetFriendsAccount();
            return;
        }
        catch (SocialException)
        {
            // Offline: keep the account; the live connection retries in the background.
        }

        StartLiveConnection(server, token);
    }

    private void StartLiveConnection(Uri server, string token)
    {
        _socialLive = new SocialConnection(server, token);
        _socialLive.EventReceived += OnSocialEvent;
        _socialLive.ConnectionChanged += (_, connected) => FriendsStatus.Text = connected ? "Online" : "Offline. Retrying…";
        _socialLive.Start();
    }

    private async Task ShowFriendsAsync()
    {
        var joined = _social?.Token is not null;
        FriendsJoin.Visibility = ToVisibility(!joined);
        FriendsMain.Visibility = ToVisibility(joined);
        AutoQueueGiftsSwitch.IsOn = _preferences.AutoQueueGifts;
        if (!joined)
        {
            FriendsServerBox.Text = _preferences.FriendsServer;
            return;
        }

        _unreadMessages = 0;
        UpdateFriendsBadge();
        await FriendsWorkAsync(async ct =>
        {
            _socialMe ??= await _social!.MeAsync(ct);
            FriendsMeName.Text = $"{_socialMe.DisplayName} · @{_socialMe.Username}";
            ApplyFriendList(await _social!.FriendsAsync(ct));
        });
    }

    private async void JoinFriends_Click(object sender, RoutedEventArgs e)
    {
        if (!Uri.TryCreate(FriendsServerBox.Text.Trim(), UriKind.Absolute, out var server) ||
            (server.Scheme != Uri.UriSchemeHttps && !server.IsLoopback))
        {
            Message("Enter the server's https:// address.", InfoBarSeverity.Warning);
            return;
        }

        server = new Uri(server.GetLeftPart(UriPartial.Path).TrimEnd('/') + "/");
        var client = new SocialClient(_http, server);
        await FriendsWorkAsync(async ct =>
        {
            var created = await client.CreateAccountAsync(
                new CreateAccountRequest(InviteCodeBox.Text, FriendsUsernameBox.Text.Trim(), FriendsDisplayNameBox.Text), ct);
            WindowsTokenStore.SaveFriendsToken(created.Token);
            client.Token = created.Token;
            _social = client;
            _socialMe = created.User;
            _preferences = _preferences with { FriendsServer = server.ToString() };
            PreferencesStore.Save(_preferences);
            StartLiveConnection(server, created.Token);
            InviteCodeBox.Text = "";
            Message($"Welcome, {created.User.DisplayName}! Whoever invited you is already on your friends list.", InfoBarSeverity.Success);
            await ShowFriendsAsync();
        });
    }

    private async void CreateInvite_Click(object sender, RoutedEventArgs e) => await FriendsWorkAsync(async ct =>
    {
        var invite = await _social!.CreateInviteAsync(ct);
        var package = new DataPackage();
        package.SetText($"Join me on Cadenza Friends. Server: {_social.Server}  Invite code: {invite.Code}");
        Clipboard.SetContent(package);
        Message($"Invite code {invite.Code} copied, with the server address. It works once, for 7 days.", InfoBarSeverity.Success);
    });

    private async void AddFriend_Click(object sender, RoutedEventArgs e) => await AddFriendAsync();

    private async void AddFriend_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            e.Handled = true;
            await AddFriendAsync();
        }
    }

    private async Task AddFriendAsync()
    {
        var username = AddFriendBox.Text.Trim();
        if (!SocialRules.IsValidUsername(username))
        {
            Message("Usernames are 3–20 lowercase letters, digits or underscores.", InfoBarSeverity.Warning);
            return;
        }

        await FriendsWorkAsync(async ct =>
        {
            var list = await _social!.AddFriendAsync(username, ct);
            ApplyFriendList(list);
            AddFriendBox.Text = "";
            Message(list.Friends.Any(friend => friend.Username == username) ? $"You and @{username} are now friends." : $"Friend request sent to @{username}.");
        });
    }

    private async void AcceptFriend_Click(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).Tag is SocialUser user)
        {
            await FriendsWorkAsync(async ct => ApplyFriendList(await _social!.AcceptAsync(user.Id, ct)));
        }
    }

    private async void DeclineFriend_Click(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).Tag is SocialUser user)
        {
            await FriendsWorkAsync(async ct => ApplyFriendList(await _social!.RemoveAsync(user.Id, ct)));
        }
    }

    private async void FriendsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (FriendsList.SelectedItem is not SocialUser friend || friend.Id == _chatFriend?.Id)
        {
            return;
        }

        _chatFriend = friend;
        _unreadMessages = 0;
        UpdateFriendsBadge();
        ChatTitle.Text = friend.DisplayName;
        Composer.IsHitTestVisible = true;
        Composer.Opacity = 1;
        ChatItems.Clear();
        await FriendsWorkAsync(async ct =>
        {
            foreach (var message in await _social!.MessagesAsync(friend.Id, null, ct))
            {
                AddChatItem(message);
            }

            ScrollChatToEnd();
        });
    }

    private async void SendChat_Click(object sender, RoutedEventArgs e) => await SendChatAsync();

    private async void Composer_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            e.Handled = true;
            await SendChatAsync();
        }
    }

    private async Task SendChatAsync()
    {
        var text = ComposerBox.Text;
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        if (await SendToFriendAsync(new SendMessageRequest(ChatMessageKind.Text, text, null)))
        {
            ComposerBox.Text = "";
        }
    }

    private async void ShareNowPlaying_Click(object sender, RoutedEventArgs e) => await SendTrackAsync(ChatMessageKind.Share);

    private async void GiftNowPlaying_Click(object sender, RoutedEventArgs e) => await SendTrackAsync(ChatMessageKind.Gift);

    private async Task SendTrackAsync(ChatMessageKind kind)
    {
        if (_playingTrack is null)
        {
            Message("Play something first, then share it.");
            return;
        }

        RememberSharedTrack(_playingTrack);
        if (await SendToFriendAsync(new SendMessageRequest(kind, kind == ChatMessageKind.Share ? ComposerBox.Text.Trim() : "", _playingTrack.Id)) &&
            kind == ChatMessageKind.Share)
        {
            ComposerBox.Text = "";
        }
    }

    /// <summary>Sends to the open conversation. The server also echoes it over the WebSocket; the duplicate is ignored.</summary>
    private async Task<bool> SendToFriendAsync(SendMessageRequest request)
    {
        if (_chatFriend is not { } friend || _social is null)
        {
            return false;
        }

        var sent = false;
        await FriendsWorkAsync(async ct =>
        {
            var message = await _social.SendAsync(friend.Id, request, ct);
            AddChatItem(message);
            sent = true;
        });
        return sent;
    }

    private async void PlayShared_Click(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).Tag is ChatItem { Track: { } track })
        {
            await PlaybackWorkAsync(ct => PlayTrackAsync(track, ct));
        }
    }

    private void QueueShared_Click(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).Tag is ChatItem { Track: { } track })
        {
            AddToQueue(track);
        }
    }

    private async void ReportMessage_Click(object sender, RoutedEventArgs e)
    {
        if (((MenuFlyoutItem)sender).Tag is not ChatItem { Mine: false } item)
        {
            Message("You can report messages your friends send you.");
            return;
        }

        await FriendsWorkAsync(async ct =>
        {
            await _social!.ReportAsync(item.Message.Id, "Reported from Cadenza", ct);
            Message("Thanks. The server owner will review it. You can also block this person from the ⋯ menu.", InfoBarSeverity.Success);
        });
    }

    private async void Unfriend_Click(object sender, RoutedEventArgs e)
    {
        if (_chatFriend is { } friend)
        {
            await FriendsWorkAsync(async ct => ApplyFriendList(await _social!.RemoveAsync(friend.Id, ct)));
        }
    }

    private async void BlockFriend_Click(object sender, RoutedEventArgs e)
    {
        if (_chatFriend is { } friend)
        {
            await FriendsWorkAsync(async ct =>
            {
                ApplyFriendList(await _social!.BlockAsync(friend.Id, ct));
                Message($"{friend.DisplayName} is blocked. They can't message you or find you.");
            });
        }
    }

    private async void LeaveFriends_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = "Leave Cadenza Friends?",
            Content = "This deletes your account, friends and messages from the server. Your music and library aren't affected.",
            PrimaryButtonText = "Delete my account",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        await FriendsWorkAsync(async ct =>
        {
            await _social!.DeleteAccountAsync(ct);
            ForgetFriendsAccount();
            Message("Your Cadenza Friends account was deleted.", InfoBarSeverity.Success);
            await ShowFriendsAsync();
        });
    }

    private void AutoQueueGifts_Toggled(object sender, RoutedEventArgs e)
    {
        if (_preferences.AutoQueueGifts != AutoQueueGiftsSwitch.IsOn)
        {
            _preferences = _preferences with { AutoQueueGifts = AutoQueueGiftsSwitch.IsOn };
            PreferencesStore.Save(_preferences);
        }
    }

    private async void OnSocialEvent(object? sender, SocialEvent socialEvent)
    {
        switch (socialEvent.Type)
        {
            case SocialEvent.MessageType when socialEvent.Message is { } message:
                await ReceiveMessageAsync(message);
                break;
            case SocialEvent.FriendRequestType when socialEvent.User is { } user:
                Message($"{user.DisplayName} (@{user.Username}) wants to be friends. Open Friends to accept.");
                await RefreshFriendsIfVisibleAsync();
                break;
            case SocialEvent.FriendAcceptedType or SocialEvent.FriendRemovedType:
                await RefreshFriendsIfVisibleAsync();
                break;
        }
    }

    private async Task ReceiveMessageAsync(ChatMessage message)
    {
        var otherId = message.FromId == _socialMe?.Id ? message.ToId : message.FromId;
        if (_view == AppView.Friends && otherId == _chatFriend?.Id)
        {
            AddChatItem(message);
            ScrollChatToEnd();
        }
        else if (message.FromId != _socialMe?.Id)
        {
            _unreadMessages++;
            UpdateFriendsBadge();
        }

        if (message.Kind == ChatMessageKind.Gift && message.FromId != _socialMe?.Id && message.TrackId is { } trackId)
        {
            var from = FriendsPeople.FirstOrDefault(friend => friend.Id == message.FromId)?.DisplayName ?? "A friend";
            if (_preferences.AutoQueueGifts && await FetchSharedTrackAsync(trackId) is { } track)
            {
                AddToQueue(track);
                Message($"{from} added {track.Name} to your Up next.");
            }
            else
            {
                Message($"{from} sent you a track for your queue. Open Friends to add it.");
            }
        }
    }

    private async Task RefreshFriendsIfVisibleAsync()
    {
        if (_view == AppView.Friends && _social is not null)
        {
            await FriendsWorkAsync(async ct => ApplyFriendList(await _social.FriendsAsync(ct)));
        }
    }

    private void ApplyFriendList(FriendList list)
    {
        FriendRequests.Clear();
        foreach (var request in list.Incoming)
        {
            FriendRequests.Add(request);
        }

        FriendsPeople.Clear();
        foreach (var friend in list.Friends)
        {
            FriendsPeople.Add(friend);
        }

        var pending = list.Outgoing.Count == 0 ? "" : $" · waiting on {string.Join(", ", list.Outgoing.Select(user => "@" + user.Username))}";
        FriendsStatus.Text = (FriendsPeople.Count == 1 ? "1 friend" : $"{FriendsPeople.Count} friends") + pending;
        if (_chatFriend is { } open && list.Friends.All(friend => friend.Id != open.Id))
        {
            CloseConversation();
        }
        else if (_chatFriend is { } current)
        {
            FriendsList.SelectedItem = FriendsPeople.FirstOrDefault(friend => friend.Id == current.Id);
        }
    }

    private void AddChatItem(ChatMessage message)
    {
        if (ChatItems.Any(item => item.Message.Id == message.Id))
        {
            return;
        }

        var item = new ChatItem(message, message.FromId == _socialMe?.Id);
        ChatItems.Add(item);
        if (message.TrackId is { } trackId)
        {
            _ = HydrateAsync(item, trackId);
        }
    }

    private async Task HydrateAsync(ChatItem item, string trackId) => item.Track = await FetchSharedTrackAsync(trackId);

    /// <summary>Looks up a shared track through your own Spotify session; cached for the session.</summary>
    private async Task<Track?> FetchSharedTrackAsync(string trackId)
    {
        if (_sharedTracks.TryGetValue(trackId, out var cached))
        {
            return cached;
        }

        if (!_auth.IsConnected)
        {
            return null;
        }

        try
        {
            var track = await _spotifyApi.TrackAsync(trackId, _lifetime.Token);
            RememberSharedTrack(track);
            return track;
        }
        catch (Exception error) when (error is HttpRequestException or InvalidOperationException or ArgumentException or OperationCanceledException)
        {
            return null;
        }
    }

    private void RememberSharedTrack(Track track)
    {
        if (_sharedTracks.Count >= MaxCachedSharedTracks)
        {
            _sharedTracks.Clear();
        }

        _sharedTracks[track.Id] = track;
    }

    private void ScrollChatToEnd()
    {
        ChatScroll.UpdateLayout();
        ChatScroll.ChangeView(null, ChatScroll.ScrollableHeight, null, true);
    }

    private void CloseConversation()
    {
        _chatFriend = null;
        ChatItems.Clear();
        ChatTitle.Text = "Pick a friend to start chatting";
        Composer.IsHitTestVisible = false;
        Composer.Opacity = 0.5;
    }

    private void UpdateFriendsBadge()
    {
        FriendsBadge.Visibility = ToVisibility(_unreadMessages > 0);
        FriendsBadgeCount.Text = _unreadMessages.ToString(System.Globalization.CultureInfo.CurrentCulture);
    }

    private void ForgetFriendsAccount()
    {
        if (_socialLive is { } live)
        {
            _socialLive = null;
            _ = live.DisposeAsync().AsTask();
        }

        try
        {
            WindowsTokenStore.DeleteFriendsToken();
        }
        catch (InvalidOperationException)
        {
            // Nothing saved, or Windows refused; the token is useless once the account is gone.
        }

        _social = null;
        _socialMe = null;
        FriendsPeople.Clear();
        FriendRequests.Clear();
        CloseConversation();
        _unreadMessages = 0;
        UpdateFriendsBadge();
    }

    /// <summary>Runs Friends work and shows server errors as notices; Friends never blocks music.</summary>
    private async Task FriendsWorkAsync(Func<CancellationToken, Task> work)
    {
        try
        {
            await work(_lifetime.Token);
        }
        catch (SocialException error) when (error.IsSignedOut)
        {
            ForgetFriendsAccount();
            Message("Your Cadenza Friends sign-in ended. Join again with a new invite.", InfoBarSeverity.Warning);
            if (_view == AppView.Friends)
            {
                FriendsJoin.Visibility = Visibility.Visible;
                FriendsMain.Visibility = Visibility.Collapsed;
            }
        }
        catch (SocialException error)
        {
            Message(error.Message, InfoBarSeverity.Warning);
        }
        catch (InvalidOperationException error)
        {
            Message(error.Message, InfoBarSeverity.Error);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
    }
}
