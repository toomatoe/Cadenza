using System.Collections.ObjectModel;
using System.Diagnostics;
using Cadenza.Core.Controllers;
using Cadenza.Core.Models;
using Cadenza.Core.Services.Playback;
using Cadenza.Core.Services.Spotify;
using Cadenza.Desktop.Models;
using Cadenza.Desktop.Services;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml;
using Windows.System;

namespace Cadenza.Desktop.Views;

public sealed partial class MainWindow : Window
{
    private readonly CatalogModel catalog = new();
    private readonly CatalogController catalogController;
    public ObservableCollection<Track> Tracks => catalog.Tracks;
    private readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private readonly ListeningQueue queue = new();
    private readonly CancellationTokenSource lifetime = new();
    private readonly SpotifyAuth auth;
    private readonly SpotifyAuth playbackAuth;
    private Preferences preferences;
    private NativeEngine? engine;
    private bool playbackBusy;
    private bool nativeConnected;
    private bool adjustingPosition;
    private bool advanceAfterOperation;
    private Track? playingTrack;
    private uint playingStamp;
    private uint endedStamp;
    private bool awaitingTrackLoad;
    private PlaybackState playbackState = PlaybackState.Stopped;
    private CancellationTokenSource? playbackOperation;
    private CancellationTokenSource? active;
    private string view = "Library";
    private bool hasMore;
    private bool signingIn;
    private bool closing;
    private int generation;
    public ObservableCollection<Playlist> Playlists => catalog.Playlists;
    private ObservableCollection<Playlist> playlists => Playlists;
    private Playlist? selectedPlaylist;
    public ObservableCollection<Playlist> PinnedPlaylists { get; } = [];

    public MainWindow()
    {
        preferences = PreferencesStore.Load();
        auth = new SpotifyAuth(http, new WindowsTokenStore());
        playbackAuth = new SpotifyAuth(http, new WindowsTokenStore(), SpotifyAuth.PlaybackScope);
        catalogController = new CatalogController(new SpotifyApi(http, auth), catalog);
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.TitleBar.ButtonForegroundColor = Windows.UI.Color.FromArgb(255, 16, 36, 61);
        AppWindow.TitleBar.ButtonInactiveForegroundColor = Windows.UI.Color.FromArgb(255, 110, 126, 148);
        AppWindow.TitleBar.ButtonHoverBackgroundColor = Windows.UI.Color.FromArgb(120, 255, 255, 255);
        AppWindow.TitleBar.ButtonHoverForegroundColor = Windows.UI.Color.FromArgb(255, 16, 36, 61);
        AppWindow.TitleBar.ButtonBackgroundColor = Windows.UI.Color.FromArgb(0, 0, 0, 0);
        AppWindow.TitleBar.ButtonInactiveBackgroundColor = Windows.UI.Color.FromArgb(0, 0, 0, 0);
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1440, 900));
        ClientIdBox.Text = preferences.ClientId;
        RedirectUriBox.Text = preferences.RedirectUri;
        SpacingBox.Value = preferences.ArtistSpacing;
        InitializeAppearance();
        UpdateViewSurfaces();
        UpdateQueueLabel();
        RefreshLibrary();
        RefreshPlaylistDeck();
        PlaylistFan.PointerEntered += (_, _) => SetPlaylistFanExpanded(true);
        PlaylistFan.PointerExited += (_, _) => SetPlaylistFanExpanded(false);

        Root.Loaded += async (_, _) =>
        {
            if (preferences.ClientId.Length == 0) return;
            await RunAsync(async ct =>
            {
                await auth.RestoreAsync(preferences.ClientId, ct);
                ConnectionLabel.Text = auth.IsConnected ? "Spotify sign-in saved" : "Spotify disconnected";
                if (auth.IsConnected) { await LoadTracksAsync(false, ct); await LoadPinnedPlaylistsAsync(ct); }
            });
        };
        Closed += OnClosed;
    }

    private void Message(string text, InfoBarSeverity severity = InfoBarSeverity.Informational)
    {
        if (closing) return;
        Notice.Message = text; Notice.Severity = severity; Notice.IsOpen = true;
    }
    private async Task RunAsync(Func<CancellationToken, Task> work)
    {
        if (closing) return;
        active?.Cancel();
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        active = operation;
        var ticket = ++generation;
        Loading.IsActive = true;
        try { await work(operation.Token); }
        catch (OperationCanceledException) when (operation.IsCancellationRequested) { }
        catch (OperationCanceledException) { if (ticket == generation) Message("The request timed out. Please retry.", InfoBarSeverity.Warning); }
        catch (Exception error)
        {
            if (ticket == generation && !closing)
                Message(error is HttpRequestException ? "Couldn't reach Spotify. Check your connection and retry." : error.Message, InfoBarSeverity.Error);
        }
        finally
        {
            if (ticket == generation && !closing) { Loading.IsActive = false; active = null; }
        }
    }
    private void CancelViewWork()
    {
        active?.Cancel(); active = null; generation++; Loading.IsActive = false;
    }
    private void Root_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        var control = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control);
        if (e.Key == VirtualKey.K && control.HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down))
        { SearchBox.Focus(FocusState.Keyboard); e.Handled = true; }
    }
    private async void OnClosed(object sender, WindowEventArgs args)
    {
        closing = true; lifetime.Cancel(); active?.Cancel();
        DisposeAppearance();
        queue.Forget(); Tracks.Clear(); playlists.Clear();
        try { await ResetPlaybackAsync(); }
        finally { http.Dispose(); lifetime.Dispose(); }
    }
}
