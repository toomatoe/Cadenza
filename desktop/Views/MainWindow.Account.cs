using System.Collections.ObjectModel;
using System.Diagnostics;
using Cadenza.Core.Services.Spotify;
using Cadenza.Desktop.Services;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml;
using Windows.System;

namespace Cadenza.Desktop.Views;

public partial class MainWindow
{
    private async void Connect_Click(object sender, RoutedEventArgs e)
    {
        if (signingIn || playbackBusy) return;
        await ResetPlaybackAsync();
        signingIn = true; ConnectButton.IsEnabled = false; DisconnectButton.IsEnabled = false;
        Message("Complete sign-in in your browser, then return here.");
        await RunAsync(async ct =>
        {
            var id = SpotifyAuth.ValidateClientId(ClientIdBox.Text);
            // Prevent orphaned credentials when switching developer applications.
            if (preferences.ClientId.Length > 0 && preferences.ClientId != id) await auth.DisconnectAsync(ct);
            var redirect = SpotifyAuth.ValidateRedirectUri(RedirectUriBox.Text).AbsoluteUri;
            await auth.ConnectAsync(id, ct, redirect);
            preferences = preferences with { ClientId = id, RedirectUri = redirect }; PreferencesStore.Save(preferences);
            ConnectionLabel.Text = "Spotify connected";
            Message("Spotify sign-in saved. You can now browse your library.", InfoBarSeverity.Success);
        });
        signingIn = false; ConnectButton.IsEnabled = true; DisconnectButton.IsEnabled = true;
    }
    private async void Disconnect_Click(object sender, RoutedEventArgs e)
    {
        if (signingIn) return;
        await RunAsync(async ct =>
        {
            await ResetPlaybackAsync();
            await auth.DisconnectAsync(ct);
            await playbackAuth.RestoreAsync(SpotifyAuth.PlaybackClientId, ct);
            await playbackAuth.DisconnectAsync(ct);
            catalogController.Reset();
            queue.Forget();
            UpdateQueueLabel(); PinnedPlaylists.Clear(); UpdatePinnedPlaylists();
            likedTrackIds.Clear(); librarySnapshot.Clear(); selectedLibraryTrack = null; RefreshLibrary(); RefreshPlaylistDeck();
            ConnectionLabel.Text = "Spotify disconnected"; hasMore = false;
            MoreButton.Visibility = Visibility.Collapsed; UpdateEmpty();
            Message("Disconnected. Saved sign-in and session data were removed.", InfoBarSeverity.Success);
        });
    }
    private void SavePreferences_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var spacing = double.IsNaN(SpacingBox.Value) ? 2 : (int)Math.Clamp(SpacingBox.Value, 0, 10);
            preferences = preferences with { ArtistSpacing = spacing }; PreferencesStore.Save(preferences);
            Message("Queue preferences saved.", InfoBarSeverity.Success);
        }
        catch (IOException) { Message("Couldn't save preferences. Check your local application data folder.", InfoBarSeverity.Error); }
    }
}
