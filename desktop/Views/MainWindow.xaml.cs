using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Cadenza.Core.Interfaces;
using Cadenza.Core.Models;
using Cadenza.Core.Services.Catalog;
using Cadenza.Core.Services.Playback;
using Cadenza.Core.Services.Social;
using Cadenza.Desktop.Models;
using Cadenza.Desktop.Services;
using Cadenza.Desktop.ViewModels;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using Windows.UI.Core;

namespace Cadenza.Desktop.Views;

/// <summary>
/// The single application window. This file holds composition and lifecycle; the other
/// MainWindow.*.cs partials group catalog, library, queue, playback, account and appearance behavior.
/// </summary>
[SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable",
    Justification = "A WinUI Window is not IDisposable; owned resources are released in OnClosed.")]
public sealed partial class MainWindow : Window
{
    private readonly ServiceConfiguration _services;
    private readonly CatalogModel _catalog;
    private readonly HttpClient _http;
    private readonly ListeningQueue _queue;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly ISpotifyAuth _auth;
    private readonly ISpotifyAuth _playbackAuth;
    private readonly ISpotifyApi _spotifyApi;
    private readonly CatalogController _catalogController;

    private Preferences _preferences;
    private NativeEngine? _engine;
    private bool _playbackBusy;
    private bool _nativeConnected;
    private bool _playingFile;
    private bool _adjustingPosition;
    private bool _advanceAfterOperation;
    private Track? _playingTrack;
    private uint _playingStamp;
    private uint _endedStamp;
    private bool _awaitingTrackLoad;
    private PlaybackState _playbackState = PlaybackState.Stopped;
    private CancellationTokenSource? _playbackOperation;
    private CancellationTokenSource? _active;
    private AppView _view = AppView.Library;
    private bool _hasMore;
    private bool _signingIn;
    private bool _closing;
    private int _generation;
    private Playlist? _selectedPlaylist;

    public MainWindow()
    {
        _preferences = PreferencesStore.Load();
        _services = new ServiceConfiguration();
        _http = _services.Http;
        _auth = _services.Auth;
        _playbackAuth = _services.PlaybackAuth;
        _spotifyApi = _services.SpotifyApi;
        _catalog = _services.Catalog;
        _catalogController = _services.CatalogController;
        _queue = _services.Queue;
        Recommendations = new RecommendationsViewModel(() => _services.Recommendations);
        FriendsPage = new FriendsViewModel(
            server => new SocialClient(_http, server),
            (server, token) => new SocialConnection(server, token),
            new WindowsFriendsTokenStore(),
            FindSharedTrackAsync,
            _lifetime.Token);
        InitializeFriends();

        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        // Caption button foreground colors follow the theme and are set in ApplyAppearance.
        AppWindow.TitleBar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
        AppWindow.TitleBar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1440, 900));

        ClientIdBox.Text = _preferences.ClientId;
        RedirectUriBox.Text = _preferences.RedirectUri;
        SpacingBox.Value = _preferences.ArtistSpacing;
        DiscoverySlider.Value = Math.Round(_preferences.Discovery * 100);
        InitializeAppearance();
        InitializeProfile();
        UpdateViewSurfaces();
        UpdateQueueLabel();
        RefreshLibrary();
        RefreshPlaylistDeck();
        PlaylistFan.PointerEntered += (_, _) => SetPlaylistFanExpanded(true);
        PlaylistFan.PointerExited += (_, _) => SetPlaylistFanExpanded(false);

        Root.Loaded += Root_Loaded;
        Closed += OnClosed;
    }

    public ObservableCollection<Track> Tracks => _catalog.Tracks;

    public ObservableCollection<Playlist> Playlists => _catalog.Playlists;

    public ObservableCollection<Playlist> PinnedPlaylists { get; } = [];

    private static AppView ParseView(object tag) => Enum.Parse<AppView>((string)tag);

    private async void Root_Loaded(object sender, RoutedEventArgs e)
    {
        _ = RestoreFriendsAsync();
        if (_preferences.ClientId.Length == 0)
            return;

        await RunAsync(async ct =>
        {
            await _auth.RestoreAsync(_preferences.ClientId, ct);
            ConnectionLabel.Text = _auth.IsConnected ? "Spotify sign-in saved" : "Spotify disconnected";
            if (_auth.IsConnected)
            {
                await LoadTracksAsync(false, ct);
                await LoadPinnedPlaylistsAsync(ct);
            }
        });
    }

    /// <summary>Looks up a track friends shared, through your own Spotify session; null when unavailable.</summary>
    private async Task<Track?> FindSharedTrackAsync(string trackId, CancellationToken cancellationToken)
    {
        if (!_auth.IsConnected)
            return null;

        try
        {
            return await _spotifyApi.TrackAsync(trackId, cancellationToken);
        }
        catch (Exception error) when (error is HttpRequestException or InvalidOperationException or ArgumentException or OperationCanceledException)
        {
            return null;
        }
    }

    private void Message(string text, InfoBarSeverity severity = InfoBarSeverity.Informational)
    {
        if (_closing)
            return;

        Notice.Message = text;
        Notice.Severity = severity;
        Notice.IsOpen = true;
    }

    /// <summary>Runs view work, cancelling the previous request and reporting only the latest failure.</summary>
    private async Task RunAsync(Func<CancellationToken, Task> work)
    {
        if (_closing)
            return;

        _active?.Cancel();
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _active = operation;
        var ticket = ++_generation;
        Loading.IsActive = true;
        try
        {
            await work(operation.Token);
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested)
        {
        }
        catch (OperationCanceledException)
        {
            if (ticket == _generation)
                Message("The request timed out. Please retry.", InfoBarSeverity.Warning);
        }
        catch (Exception error)
        {
            if (ticket == _generation && !_closing)
            {
                Message(
                    error is HttpRequestException ? "Couldn't reach Spotify. Check your connection and retry." : error.Message,
                    InfoBarSeverity.Error);
            }
        }
        finally
        {
            if (ticket == _generation && !_closing)
            {
                Loading.IsActive = false;
                _active = null;
            }
        }
    }

    private void CancelViewWork()
    {
        _active?.Cancel();
        _active = null;
        _generation++;
        Loading.IsActive = false;
    }

    private void OpenInBrowser(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception error) when (error is Win32Exception or InvalidOperationException)
        {
            Message("Windows couldn't open the Spotify link.", InfoBarSeverity.Warning);
        }
    }

    private void Root_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        var control = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control);
        if (e.Key == VirtualKey.K && control.HasFlag(CoreVirtualKeyStates.Down))
        {
            SearchBox.Focus(FocusState.Keyboard);
            e.Handled = true;
        }
    }

    private async void OnClosed(object sender, WindowEventArgs args)
    {
        _closing = true;
        _lifetime.Cancel();
        _active?.Cancel();
        DisposeAppearance();
        _queue.Forget();
        Tracks.Clear();
        Playlists.Clear();
        try
        {
            await ResetPlaybackAsync();
        }
        finally
        {
            await FriendsPage.DisposeAsync();

            _services.Dispose();
            _lifetime.Dispose();
        }
    }
}
