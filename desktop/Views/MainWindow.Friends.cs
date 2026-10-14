using Cadenza.Core.Models.Social;
using Cadenza.Desktop.Models;
using Cadenza.Desktop.Services;
using Cadenza.Desktop.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;

namespace Cadenza.Desktop.Views;

/// <summary>The Friends page: thin glue between the controls and <see cref="FriendsViewModel"/>.</summary>
public sealed partial class MainWindow
{
    private const string LeaveTitle = "Leave Cadenza Friends?";
    private const string LeaveText = "This deletes your account, friends and messages from the server. Songs you added stay in the shared library, and your music and Spotify library aren't affected.";

    public FriendsViewModel FriendsPage { get; }

    private void InitializeFriends()
    {
        FriendsPage.AutoQueueGifts = _preferences.AutoQueueGifts;
        FriendsPage.NoticeRaised += (_, notice) => Message(notice);
        FriendsPage.GiftToQueue += (_, track) => AddToQueue(track);
        FriendsPage.ConversationChanged += (_, _) => ScrollChatToEnd();
    }

    private Task RestoreFriendsAsync() => FriendsPage.RestoreAsync(_preferences.FriendsServer);

    private async Task ShowFriendsAsync()
    {
        AutoQueueGiftsSwitch.IsOn = _preferences.AutoQueueGifts;
        if (!FriendsPage.IsJoined)
            FriendsServerBox.Text = _preferences.FriendsServer;
        await FriendsPage.OpenAsync();
    }

    private async void JoinFriends_Click(object sender, RoutedEventArgs e)
    {
        var server = await FriendsPage.JoinAsync(FriendsServerBox.Text, InviteCodeBox.Text, FriendsUsernameBox.Text, FriendsDisplayNameBox.Text);
        if (server is null)
            return;

        _preferences = _preferences with { FriendsServer = server };
        PreferencesStore.Save(_preferences);
        InviteCodeBox.Text = "";
    }

    private async void CreateInvite_Click(object sender, RoutedEventArgs e)
    {
        if (await FriendsPage.CreateInviteAsync() is not { } invite)
            return;

        var package = new DataPackage();
        package.SetText(invite);
        Clipboard.SetContent(package);
    }

    private async void AddFriend_Click(object sender, RoutedEventArgs e) => await AddFriendAsync();

    private async void AddFriend_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter)
            return;

        e.Handled = true;
        await AddFriendAsync();
    }

    private async Task AddFriendAsync()
    {
        if (await FriendsPage.AddFriendAsync(AddFriendBox.Text))
            AddFriendBox.Text = "";
    }

    private async void AcceptFriend_Click(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).Tag is SocialUser user)
            await FriendsPage.AcceptAsync(user);
    }

    private async void DeclineFriend_Click(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).Tag is SocialUser user)
            await FriendsPage.DeclineAsync(user);
    }

    private async void FriendsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (FriendsList.SelectedItem is SocialUser friend)
            await FriendsPage.OpenConversationAsync(friend);
    }

    private async void SendChat_Click(object sender, RoutedEventArgs e) => await SendChatAsync();

    private async void Composer_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter)
            return;

        e.Handled = true;
        await SendChatAsync();
    }

    private async Task SendChatAsync()
    {
        if (await FriendsPage.SendTextAsync(ComposerBox.Text))
            ComposerBox.Text = "";
    }

    private async void ShareNowPlaying_Click(object sender, RoutedEventArgs e)
    {
        if (await FriendsPage.SendTrackAsync(_playingTrack, ChatMessageKind.Share, ComposerBox.Text))
            ComposerBox.Text = "";
    }

    private async void GiftNowPlaying_Click(object sender, RoutedEventArgs e) =>
        await FriendsPage.SendTrackAsync(_playingTrack, ChatMessageKind.Gift, "");

    private async void PlayShared_Click(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).Tag is ChatItem { Track: { } track })
            await PlaybackWorkAsync(ct => PlayTrackAsync(track, ct));
    }

    private void QueueShared_Click(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).Tag is ChatItem { Track: { } track })
            AddToQueue(track);
    }

    private async void ReportMessage_Click(object sender, RoutedEventArgs e)
    {
        if (((MenuFlyoutItem)sender).Tag is ChatItem item)
            await FriendsPage.ReportAsync(item);
    }

    private async void Unfriend_Click(object sender, RoutedEventArgs e) => await FriendsPage.UnfriendAsync();

    private async void BlockFriend_Click(object sender, RoutedEventArgs e) => await FriendsPage.BlockAsync();

    private async void LeaveFriends_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = LeaveTitle,
            Content = LeaveText,
            PrimaryButtonText = "Delete my account",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            return;

        if (await FriendsPage.LeaveAsync())
        {
            SongCache.Clear();
            await ShowFriendsAsync();
        }
    }

    private void AutoQueueGifts_Toggled(object sender, RoutedEventArgs e)
    {
        FriendsPage.AutoQueueGifts = AutoQueueGiftsSwitch.IsOn;
        if (_preferences.AutoQueueGifts == AutoQueueGiftsSwitch.IsOn)
            return;

        _preferences = _preferences with { AutoQueueGifts = AutoQueueGiftsSwitch.IsOn };
        PreferencesStore.Save(_preferences);
    }

    private void ScrollChatToEnd()
    {
        ChatScroll.UpdateLayout();
        ChatScroll.ChangeView(null, ChatScroll.ScrollableHeight, null, true);
    }

    private void Message(Notice notice) => Message(notice.Text, notice.Level switch
    {
        NoticeLevel.Success => InfoBarSeverity.Success,
        NoticeLevel.Warning => InfoBarSeverity.Warning,
        NoticeLevel.Error => InfoBarSeverity.Error,
        _ => InfoBarSeverity.Informational,
    });
}
