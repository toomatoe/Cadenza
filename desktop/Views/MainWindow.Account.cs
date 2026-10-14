using Cadenza.Core.Services.Spotify;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Cadenza.Desktop.Views;

public sealed partial class MainWindow
{
    private const int DefaultArtistSpacing = 2;
    private const int MaxArtistSpacing = 10;

    private async void Connect_Click(object sender, RoutedEventArgs e)
    {
        if (_signingIn || _playbackBusy)
            return;

        await ResetPlaybackAsync();
        SetSigningIn(true);
        Message("Complete sign-in in your browser, then return here.");
        await RunAsync(async ct =>
        {
            var clientId = SpotifyAuth.ValidateClientId(ClientIdBox.Text);

            // Prevent orphaned credentials when switching developer applications.
            if (_preferences.ClientId.Length > 0 && _preferences.ClientId != clientId)
                await _auth.DisconnectAsync(ct);

            var redirect = SpotifyAuth.ValidateRedirectUri(RedirectUriBox.Text).AbsoluteUri;
            await _auth.ConnectAsync(clientId, redirect, ct);
            _preferences = _preferences with { ClientId = clientId, RedirectUri = redirect };
            _services.Preferences.Save(_preferences);
            ConnectionLabel.Text = "Spotify connected";
            Message("Spotify sign-in saved. You can now browse your library.", InfoBarSeverity.Success);
        });
        SetSigningIn(false);
    }

    private void SetSigningIn(bool signingIn)
    {
        _signingIn = signingIn;
        ConnectButton.IsEnabled = !signingIn;
        DisconnectButton.IsEnabled = !signingIn;
    }

    private async void Disconnect_Click(object sender, RoutedEventArgs e)
    {
        if (_signingIn)
            return;

        await RunAsync(async ct =>
        {
            await ResetPlaybackAsync();
            await _auth.DisconnectAsync(ct);
            await _playbackAuth.RestoreAsync(SpotifyAuth.PlaybackClientId, ct);
            await _playbackAuth.DisconnectAsync(ct);

            _catalogController.Reset();
            _queue.Forget();
            UpdateQueueLabel();
            PinnedPlaylists.Clear();
            UpdatePinnedPlaylists();
            _likedTrackIds.Clear();
            _librarySnapshot.Clear();
            ForgetRecommendations();
            _selectedLibraryTrack = null;
            RefreshLibrary();
            RefreshPlaylistDeck();

            ConnectionLabel.Text = "Spotify disconnected";
            _hasMore = false;
            MoreButton.Visibility = Visibility.Collapsed;
            UpdateEmpty();
            Message("Disconnected. Saved sign-in and session data were removed.", InfoBarSeverity.Success);
        });
    }

    private void SavePreferences_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var spacing = double.IsNaN(SpacingBox.Value)
                ? DefaultArtistSpacing
                : (int)Math.Clamp(SpacingBox.Value, 0, MaxArtistSpacing);
            _preferences = _preferences with { ArtistSpacing = spacing };
            _services.Preferences.Save(_preferences);
            Message("Queue preferences saved.", InfoBarSeverity.Success);
        }
        catch (IOException)
        {
            Message("Couldn't save preferences. Check your local application data folder.", InfoBarSeverity.Error);
        }
    }
}
