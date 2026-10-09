using System.Collections.ObjectModel;
using System.Diagnostics;
using Cadenza.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace Cadenza.Desktop;

public sealed partial class MainWindow : Window
{
    public ObservableCollection<Track> Tracks { get; } = [];
    private readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private readonly ListeningQueue queue = new();
    private readonly CancellationTokenSource lifetime = new();
    private readonly SpotifyAuth auth;
    private readonly SpotifyApi api;
    private Preferences preferences;
    private NativeEngine? engine;
    private bool playbackBusy;
    private bool nativeConnected;
    private bool adjustingPosition;
    private bool advanceAfterOperation;
    private Track? playingTrack;
    private uint playingStamp;
    private PlaybackState playbackState = PlaybackState.Stopped;
    private CancellationTokenSource? playbackOperation;
    private CancellationTokenSource? active;
    private string view = "Library";
    private string lastQuery = "";
    private int offset;
    private bool hasMore;
    private bool signingIn;
    private bool closing;
    private int generation;
    private Page<Playlist>? playlistPage;
    public ObservableCollection<Playlist> Playlists { get; } = [];
    private ObservableCollection<Playlist> playlists => Playlists;
    private Playlist? selectedPlaylist;
    public ObservableCollection<Track> UpNext { get; } = [];
    public ObservableCollection<Playlist> PinnedPlaylists { get; } = [];
    private Track? heroTrack;
    private static readonly double[] FanAngles = [-14, -4, 5, 15];
    private static readonly double[] FanOffsets = [120, 40, -40, -120];

    public MainWindow()
    {
        preferences = Preferences.Load();
        auth = new SpotifyAuth(http, new WindowsTokenStore());
        api = new SpotifyApi(http, auth);
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.TitleBar.ButtonForegroundColor = Windows.UI.Color.FromArgb(255, 16, 36, 61);
        AppWindow.TitleBar.ButtonInactiveForegroundColor = Windows.UI.Color.FromArgb(255, 110, 126, 148);
        AppWindow.TitleBar.ButtonHoverBackgroundColor = Windows.UI.Color.FromArgb(120, 255, 255, 255);
        AppWindow.TitleBar.ButtonHoverForegroundColor = Windows.UI.Color.FromArgb(255, 16, 36, 61);
        AppWindow.TitleBar.ButtonBackgroundColor = Windows.UI.Color.FromArgb(0, 0, 0, 0);
        AppWindow.TitleBar.ButtonInactiveBackgroundColor = Windows.UI.Color.FromArgb(0, 0, 0, 0);
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1320, 880));
        ClientIdBox.Text = preferences.ClientId;
        RedirectUriBox.Text = preferences.RedirectUri;
        SpacingBox.Value = preferences.ArtistSpacing;
        UpdateNav();

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
    private async void Navigate_Click(object sender, RoutedEventArgs e)
    {
        if (signingIn) return;
        await NavigateAsync((string)((Button)sender).Tag);
    }
    private async Task NavigateAsync(string destination)
    {
        CancelViewWork();
        view = destination; selectedPlaylist = null; offset = 0; hasMore = false; Tracks.Clear();
        SettingsPanel.Visibility = view == "Settings" ? Visibility.Visible : Visibility.Collapsed;
        UpdateNav();
        UpdateViewSurfaces();
        QueueTools.Visibility = view == "Queue" ? Visibility.Visible : Visibility.Collapsed;
        MoreButton.Visibility = Visibility.Collapsed;
        PageTitle.Text = view switch { "Library" => "Your library", "Queue" => "Listening queue", _ => view };
        EmptyTitle.Text = view == "Queue" ? "A fresh listening session" : "Make room for your music";
        EmptyDescription.Text = view == "Queue" ? "Add tracks from your library or search. Queue edits can be undone." : "Connect Spotify in Settings to browse your library and playlists.";
        if (view == "Settings") { EmptyState.Visibility = Visibility.Collapsed; return; }
        if (view == "Queue") { RenderQueue(); return; }
        UpdateEmpty();
        if (!auth.IsConnected) return;
        if (view == "Search" && string.IsNullOrWhiteSpace(SearchBox.Text))
        {
            EmptyDescription.Text = "Search for a track or artist above."; return;
        }
        await RunAsync(async ct =>
        {
            if (view == "Playlists")
            {
                playlists.Clear(); selectedPlaylist = null;
                playlistPage = await api.PlaylistsAsync(0, ct);
                ct.ThrowIfCancellationRequested();
                foreach (var item in playlistPage.Items) playlists.Add(item);
                hasMore = playlistPage.HasMore;
                MoreButton.Visibility = hasMore ? Visibility.Visible : Visibility.Collapsed;
                EmptyTitle.Text = "Your playlist collection";
                EmptyDescription.Text = "Your playlists will appear here as cover records.";
                UpdateEmpty();
            }
            else
            {
                await LoadTracksAsync(false, ct);
                if (view == "Library" && PinnedPlaylists.Count == 0) await LoadPinnedPlaylistsAsync(ct);
            }
        });
    }
    private async Task LoadPinnedPlaylistsAsync(CancellationToken ct)
    {
        var page = await api.PlaylistsAsync(0, ct);
        ct.ThrowIfCancellationRequested();
        PinnedPlaylists.Clear();
        foreach (var item in page.Items.Take(6)) PinnedPlaylists.Add(item);
        UpdateFan();
        if (view == "Library") ScrollLibraryToTop();
    }
    private void UpdateFan()
    {
        var cards = new[] { Fan0, Fan1, Fan2, Fan3 };
        var names = new[] { FanName0, FanName1, FanName2, FanName3 };
        var images = new[] { FanImg0, FanImg1, FanImg2, FanImg3 };
        for (var i = 0; i < cards.Length; i++)
        {
            var item = i < PinnedPlaylists.Count ? PinnedPlaylists[i] : null;
            cards[i].Visibility = item is null ? Visibility.Collapsed : Visibility.Visible;
            cards[i].Tag = item;
            names[i].Text = item?.Name ?? "";
            images[i].Source = item is null ? null : Artwork(item.ArtworkUrl);
            if (item is not null) AutomationProperties.SetName(cards[i], $"Open {item.Name}");
        }
        PlaylistFan.Visibility = PinnedPlaylists.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        SidebarPlaylists.Visibility = PlaylistFan.Visibility;
    }
    private static ImageSource? Artwork(string url) =>
        new ArtworkConverter().Convert(url, typeof(ImageSource), null!, "") as ImageSource;
    private void AnimateFan(bool spread)
    {
        var transforms = new[] { FanX0, FanX1, FanX2, FanX3 };
        var storyboard = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
        var ease = new Microsoft.UI.Xaml.Media.Animation.CubicEase { EasingMode = Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseOut };
        for (var i = 0; i < transforms.Length; i++)
        {
            foreach (var (property, target) in new[] { ("Rotation", spread ? 0 : FanAngles[i]), ("TranslateX", spread ? 0 : FanOffsets[i]) })
            {
                var animation = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation { To = target, Duration = TimeSpan.FromMilliseconds(450), EasingFunction = ease };
                Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(animation, transforms[i]);
                Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(animation, property);
                storyboard.Children.Add(animation);
            }
        }
        storyboard.Begin();
    }
    private void FanHost_PointerEntered(object sender, PointerRoutedEventArgs e) => AnimateFan(true);
    private void FanHost_PointerExited(object sender, PointerRoutedEventArgs e) => AnimateFan(false);
    private void FanHost_GotFocus(object sender, RoutedEventArgs e) => AnimateFan(true);
    private void FanHost_LostFocus(object sender, RoutedEventArgs e) => AnimateFan(false);
    private async void Fan_Click(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).Tag is Playlist playlist) await OpenPlaylistAsync(playlist);
    }
    private async void PinnedPlaylist_Click(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).Tag is Playlist playlist) await OpenPlaylistAsync(playlist);
    }
    private ScrollViewer? libraryScroller;
    private static ScrollViewer? FindScrollViewer(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is ScrollViewer found) return found;
            if (FindScrollViewer(child) is { } nested) return nested;
        }
        return null;
    }
    private void ScrollLibraryToTop()
    {
        libraryScroller ??= FindScrollViewer(LibraryCards);
        if (libraryScroller is null) return;
        // The header grows after items load; anchoring would otherwise hide it above the fold.
        libraryScroller.VerticalAnchorRatio = double.NaN;
        libraryScroller.ChangeView(null, 0, null, true);
    }
    private void UpdateHero()
    {
        if (playingTrack is not null) heroTrack = playingTrack;
        else if (view == "Library" && Tracks.Count > 0 && (heroTrack is null || !Tracks.Contains(heroTrack))) heroTrack = Tracks[0];
        if (heroTrack is not { } track) { HeroCard.Visibility = Visibility.Collapsed; return; }
        var playing = ReferenceEquals(track, playingTrack) && playbackState is PlaybackState.Playing or PlaybackState.Loading;
        HeroCard.Visibility = Visibility.Visible;
        HeroEyebrow.Text = playing ? "NOW PLAYING" : "JUMP BACK IN";
        HeroTitle.Text = track.Name;
        HeroSubtitle.Text = $"{track.Artist} · {track.Album}";
        HeroImage.Source = Artwork(track.ArtworkUrl);
        HeroAddButton.Tag = track; HeroOpenLink.Tag = track;
        HeroPlayGlyph.Glyph = playing ? "" : "";
        HeroPlayText.Text = playing ? "Pause" : "Play";
    }
    private async void HeroPlay_Click(object sender, RoutedEventArgs e)
    {
        if (heroTrack is not { } track) return;
        if (ReferenceEquals(track, playingTrack)) PlayPause_Click(sender, e);
        else await PlaybackWorkAsync(ct => PlayTrackAsync(track, ct));
    }
    private void ShuffleQueue_Click(object sender, RoutedEventArgs e)
    {
        if (queue.Tracks.Count < 2) { Message("Add a few tracks to your queue to shuffle them."); return; }
        queue.Shuffle(preferences.ArtistSpacing, Random.Shared);
        UpdateQueueLabel(); if (view == "Queue") RenderQueue();
        Message("Queue shuffled using your artist-spacing preference.");
    }
    private async Task LoadTracksAsync(bool append, CancellationToken ct)
    {
        var nextOffset = append ? offset + 10 : 0;
        Page<Track> page;
        switch (view)
        {
            case "Search":
                var query = SearchBox.Text.Trim();
                if (query.Length == 0) return;
                page = await api.SearchAsync(query, nextOffset, ct); lastQuery = query; break;
            case "Playlists" when selectedPlaylist is Playlist selected:
                page = await api.PlaylistTracksAsync(selected.Id, nextOffset, ct); break;
            case "Library": page = await api.SavedTracksAsync(nextOffset, ct); break;
            default: return;
        }
        ct.ThrowIfCancellationRequested();
        if (!append) Tracks.Clear();
        foreach (var track in page.Items) Tracks.Add(track);
        offset = page.Offset; hasMore = page.HasMore;
        hasMore = hasMore && Tracks.Count < 500;
        MoreButton.Visibility = hasMore ? Visibility.Visible : Visibility.Collapsed;
        if (Tracks.Count >= 500) Message("Showing the first 500 tracks. Refine your search or choose another playlist to keep memory use bounded.");
        EmptyTitle.Text = "Nothing here yet";
        EmptyDescription.Text = "No tracks were returned. Try another search or playlist.";
        ConnectionLabel.Text = "Spotify connected";
        UpdateEmpty(); UpdateHero();
        if (!append && view == "Library") ScrollLibraryToTop();
    }
    private void UpdateNav()
    {
        var selected = (Brush)Application.Current.Resources["NavSelectedBrush"];
        var idle = (Brush)Application.Current.Resources["NavIdleBrush"];
        foreach (var button in new[] { NavLibrary, NavSearch, NavPlaylists, NavQueue, NavSettings })
        {
            var current = (string)button.Tag == view;
            button.Background = current ? selected : idle;
            button.BorderBrush = current ? selected : idle;
            button.FontWeight = current ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal;
        }
    }
    private void ShowPlayGlyph(bool playing)
    {
        PlayPauseGlyph.Glyph = playing ? "" : "";
        AutomationProperties.SetName(PlayPauseButton, playing ? "Pause" : "Play");
    }
    private void UpdateViewSurfaces()
    {
        LibraryCards.Visibility = view == "Library" ? Visibility.Visible : Visibility.Collapsed;
        PlaylistCards.Visibility = view == "Playlists" && selectedPlaylist is null ? Visibility.Visible : Visibility.Collapsed;
        PlaylistDetail.Visibility = view == "Playlists" && selectedPlaylist is not null ? Visibility.Visible : Visibility.Collapsed;
        TrackPanel.Visibility = view == "Search" || view == "Queue" || (view == "Playlists" && selectedPlaylist is not null) ? Visibility.Visible : Visibility.Collapsed;
        SearchPanel.Visibility = view == "Settings" || view == "Playlists" ? Visibility.Collapsed : Visibility.Visible;
    }
    private void UpdateEmpty()
    {
        var empty = view == "Playlists" && selectedPlaylist is null ? playlists.Count == 0 : Tracks.Count == 0;
        EmptyState.Visibility = view != "Settings" && empty ? Visibility.Visible : Visibility.Collapsed;
    }
    private async void PlaylistCard_Click(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is Playlist selected) await OpenPlaylistAsync(selected);
    }
    private async Task OpenPlaylistAsync(Playlist selected)
    {
        if (signingIn) return;
        if (view != "Playlists")
        {
            CancelViewWork(); view = "Playlists";
            SettingsPanel.Visibility = Visibility.Collapsed; QueueTools.Visibility = Visibility.Collapsed;
            UpdateNav();
        }
        selectedPlaylist = selected; Tracks.Clear(); offset = 0; hasMore = false;
        MoreButton.Visibility = Visibility.Collapsed;
        PlaylistHeroTitle.Text = selected.Name; PlaylistHeroSubtitle.Text = selected.Subtitle;
        PlaylistHeroImage.Source = new ArtworkConverter().Convert(selected.ArtworkUrl, typeof(Microsoft.UI.Xaml.Media.ImageSource), null!, "") as Microsoft.UI.Xaml.Media.ImageSource;
        PageTitle.Text = "In the collection";
        UpdateViewSurfaces(); UpdateEmpty();
        await RunAsync(ct => LoadTracksAsync(false, ct));
    }
    private async void BackToPlaylists_Click(object sender, RoutedEventArgs e) => await NavigateAsync("Playlists");
    private void LibraryCard_Click(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is Track track) LibraryCards.SelectedItem = track;
    }
    private async void Search_Click(object sender, RoutedEventArgs e)
    {
        if (signingIn) return;
        await NavigateAsync("Search");
    }
    private async void Search_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter || signingIn) return;
        e.Handled = true; await NavigateAsync("Search");
    }
    private async void Search_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (view != "Search" || !auth.IsConnected || signingIn) return;
        await RunAsync(async ct =>
        {
            await Task.Delay(350, ct);
            if (string.IsNullOrWhiteSpace(SearchBox.Text)) { Tracks.Clear(); hasMore = false; MoreButton.Visibility = Visibility.Collapsed; UpdateEmpty(); return; }
            await LoadTracksAsync(false, ct);
        });
    }
    private async void More_Click(object sender, RoutedEventArgs e)
    {
        if (signingIn || !hasMore) return;
        await RunAsync(async ct =>
        {
            if (view == "Playlists" && selectedPlaylist is null && playlistPage is not null)
            {
                var page = await api.PlaylistsAsync(playlistPage.Offset + 10, ct);
                ct.ThrowIfCancellationRequested(); playlistPage = page;
                foreach (var item in page.Items) playlists.Add(item);
                hasMore = page.HasMore && playlists.Count < 500; MoreButton.Visibility = hasMore ? Visibility.Visible : Visibility.Collapsed;
                UpdateEmpty();
            }
            else await LoadTracksAsync(view != "Search" || lastQuery == SearchBox.Text.Trim(), ct);
        });
    }
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
            preferences = preferences with { ClientId = id, RedirectUri = redirect }; preferences.Save();
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
            Tracks.Clear(); playlists.Clear(); playlistPage = null; queue.Forget();
            UpdateQueueLabel(); PinnedPlaylists.Clear(); UpdateFan(); heroTrack = null; UpdateHero();
            ConnectionLabel.Text = "Spotify disconnected"; hasMore = false; offset = 0;
            MoreButton.Visibility = Visibility.Collapsed; UpdateEmpty();
            Message("Disconnected. Saved sign-in and session data were removed.", InfoBarSeverity.Success);
        });
    }
    private void SavePreferences_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var spacing = double.IsNaN(SpacingBox.Value) ? 2 : (int)Math.Clamp(SpacingBox.Value, 0, 10);
            preferences = preferences with { ArtistSpacing = spacing }; preferences.Save();
            Message("Queue preferences saved.", InfoBarSeverity.Success);
        }
        catch (IOException) { Message("Couldn't save preferences. Check your local application data folder.", InfoBarSeverity.Error); }
    }
    private async void CheckBridge_Click(object sender, RoutedEventArgs e)
    {
        if (signingIn) return;
        await RunAsync(async ct =>
        {
            EnsureEngine();
            var version = await engine!.CheckBridgeAsync(ct);
            Message($"Native bridge ABI {version} responded. Playback still needs a live account and Windows audio test.", InfoBarSeverity.Success);
        });
    }
    private void EnsureEngine()
    {
        if (engine is not null) return;
        var created = new NativeEngine();
        engine = created;
        created.PlaybackChanged += update => DispatcherQueue.TryEnqueue(() =>
        {
            if (!closing && ReferenceEquals(engine, created)) ApplyPlayback(update);
        });
    }
    private async Task PlaybackWorkAsync(Func<CancellationToken, Task> action)
    {
        if (closing || signingIn || playbackBusy) return;
        playbackBusy = true; PlayPauseButton.IsEnabled = false;
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        playbackOperation = operation;
        try { await action(operation.Token); }
        catch (OperationCanceledException) when (operation.IsCancellationRequested) { }
        catch (Exception error)
        {
            if (!closing) { PlaybackLabel.Text = "Playback failed"; Message(error.Message, InfoBarSeverity.Error); }
        }
        finally
        {
            playbackBusy = false;
            if (!closing) PlayPauseButton.IsEnabled = true;
            if (ReferenceEquals(playbackOperation, operation)) playbackOperation = null;
            if (advanceAfterOperation && !closing)
            {
                advanceAfterOperation = false;
                _ = PlaybackWorkAsync(PlayNextAsync);
            }
        }
    }
    private async Task PlayTrackAsync(Track track, CancellationToken ct)
    {
        if (!auth.IsConnected) throw new InvalidOperationException("Connect Spotify in Settings first.");
        EnsureEngine();
        var currentEngine = engine!;
        if (!nativeConnected)
        {
            PlaybackLabel.Text = "Connecting…";
            var token = await auth.GetAccessTokenAsync(ct);
            await currentEngine.AuthenticateAsync(auth.ClientId, token, ct);
            ct.ThrowIfCancellationRequested(); nativeConnected = true;
            await currentEngine.SetVolumeAsync((uint)VolumeSlider.Value, ct);
        }
        var stamp = await currentEngine.LoadTrackAsync(track.Id, ct);
        ct.ThrowIfCancellationRequested();
        playingStamp = stamp; playingTrack = track; playbackState = PlaybackState.Loading;
        NowPlayingLabel.Text = $"{track.Name} · {track.Artist}";
        NowPlayingImage.Source = new ArtworkConverter().Convert(track.ArtworkUrl, typeof(Microsoft.UI.Xaml.Media.ImageSource), null!, "") as Microsoft.UI.Xaml.Media.ImageSource;
        PositionSlider.Maximum = Math.Max(1, track.DurationMs); PositionSlider.Value = 0; PositionSlider.IsEnabled = true;
        PlaybackLabel.Text = "Loading…"; ShowPlayGlyph(true); UpdateHero();
        if (currentEngine.LatestPlayback is { } update) ApplyPlayback(update);
    }
    private void ApplyPlayback(PlaybackUpdate update)
    {
        if (update.State == PlaybackState.Failed)
        {
            if (update.TrackStamp != 0 && update.TrackStamp != playingStamp) return;
            if (PlaybackErrors.RequiresReconnect(update.ErrorCode)) nativeConnected = false;
            playbackState = PlaybackState.Failed;
            PlaybackLabel.Text = "Playback failed"; ShowPlayGlyph(false);
            Message(PlaybackErrors.Describe(update.ErrorCode), InfoBarSeverity.Error);
            return;
        }
        if (update.TrackStamp != playingStamp || playingTrack is null) return;
        playbackState = update.State;
        if (!adjustingPosition) PositionSlider.Value = Math.Min(update.PositionMs, PositionSlider.Maximum);
        ShowPlayGlyph(update.State is PlaybackState.Playing or PlaybackState.Loading); UpdateHero();
        PlaybackLabel.Text = update.State switch
        {
            PlaybackState.Loading => "Loading…", PlaybackState.Paused => "Paused", PlaybackState.Stopped => "Stopped", PlaybackState.Ended => "Finished",
            _ => $"{TimeSpan.FromMilliseconds(update.PositionMs):m\\:ss} / {playingTrack.Duration}"
        };
        // Explicit queue progression, only after the native end-of-track event.
        if (update.State == PlaybackState.Ended)
        {
            if (playbackBusy) advanceAfterOperation = true;
            else _ = PlaybackWorkAsync(PlayNextAsync);
        }
    }
    private async void PlayTrack_Click(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).Tag is Track track) { TrackList.SelectedItem = track; LibraryCards.SelectedItem = track; await PlaybackWorkAsync(ct => PlayTrackAsync(track, ct)); }
    }
    private async void PlayPause_Click(object sender, RoutedEventArgs e)
    {
        await PlaybackWorkAsync(async ct =>
        {
            if (playingTrack is null)
            {
                var selected = (view == "Library" ? LibraryCards.SelectedItem as Track : TrackList.SelectedItem as Track) ?? queue.Tracks.FirstOrDefault();
                if (selected is null) throw new InvalidOperationException("Choose a track or add one to your queue.");
                await PlayTrackAsync(selected, ct);
            }
            else if (!nativeConnected || playbackState is PlaybackState.Ended or PlaybackState.Failed or PlaybackState.Stopped) await PlayTrackAsync(playingTrack, ct);
            else if (playbackState is PlaybackState.Playing or PlaybackState.Loading) await engine!.PauseAsync(ct);
            else await engine!.ResumeAsync(ct);
        });
    }
    private async Task PlayNextAsync(CancellationToken ct)
    {
        if (queue.Tracks.Count == 0) { Message("Your listening queue is empty."); return; }
        // Duplicates are allowed. Consume one occurrence after loading, preserving undo.
        var next = queue.Tracks[0];
        await PlayTrackAsync(next, ct);
        queue.RemoveAt(0); UpdateQueueLabel(); if (view == "Queue") RenderQueue();
    }
    private async void Next_Click(object sender, RoutedEventArgs e) => await PlaybackWorkAsync(PlayNextAsync);
    private async void Stop_Click(object sender, RoutedEventArgs e) => await PlaybackWorkAsync(async ct => { if (engine is not null && nativeConnected) await engine.StopAsync(ct); });
    private void Position_Pressed(object sender, PointerRoutedEventArgs e) => adjustingPosition = true;
    private void Position_KeyDown(object sender, KeyRoutedEventArgs e) => adjustingPosition = true;
    private async Task SeekAsync()
    {
        var position = (uint)PositionSlider.Value; adjustingPosition = false;
        await PlaybackWorkAsync(async ct => { if (engine is not null && nativeConnected && playingTrack is not null) await engine.SeekAsync(position, ct); });
    }
    private async void Position_Released(object sender, PointerRoutedEventArgs e) => await SeekAsync();
    private async void Position_KeyUp(object sender, KeyRoutedEventArgs e) => await SeekAsync();
    private async Task VolumeAsync() => await PlaybackWorkAsync(async ct => { if (engine is not null && nativeConnected) await engine.SetVolumeAsync((uint)VolumeSlider.Value, ct); });
    private async void Volume_Released(object sender, PointerRoutedEventArgs e) => await VolumeAsync();
    private async void Volume_KeyUp(object sender, KeyRoutedEventArgs e) => await VolumeAsync();
    private async Task ResetPlaybackAsync()
    {
        playbackOperation?.Cancel(); advanceAfterOperation = false;
        var old = engine; engine = null; nativeConnected = false; playingTrack = null; playingStamp = 0;
        playbackState = PlaybackState.Stopped;
        if (old is not null) await old.DisposeAsync();
        if (!closing)
        {
            NowPlayingLabel.Text = "Choose a track to play"; NowPlayingImage.Source = null; PlaybackLabel.Text = "Not playing";
            ShowPlayGlyph(false); PositionSlider.IsEnabled = false; PositionSlider.Value = 0; UpdateHero();
        }
    }
    private void Add_Click(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).Tag is not Track track) return;
        try { queue.Add(track); UpdateQueueLabel(); if (view == "Queue") RenderQueue(); }
        catch (InvalidOperationException error) { Message(error.Message, InfoBarSeverity.Warning); }
    }
    private void OpenTrack_Click(object sender, RoutedEventArgs e)
    {
        if (((HyperlinkButton)sender).Tag is not Track track) return;
        try { Process.Start(new ProcessStartInfo(track.Url) { UseShellExecute = true }); }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException)
        { Message("Windows couldn't open the Spotify link.", InfoBarSeverity.Warning); }
    }
    private void UpdateQueueLabel()
    {
        QueueLabel.Text = $"{queue.Tracks.Count} tracks";
        UpNext.Clear(); foreach (var track in queue.Tracks.Take(3)) UpNext.Add(track);
        UpNextEmpty.Visibility = UpNext.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }
    private void RenderQueue(int selection = -1)
    {
        Tracks.Clear(); foreach (var track in queue.Tracks) Tracks.Add(track);
        if (selection >= 0 && selection < Tracks.Count) TrackList.SelectedIndex = selection;
        UpdateQueueLabel(); UpdateEmpty();
    }
    private void Up_Click(object sender, RoutedEventArgs e) { var i = TrackList.SelectedIndex; queue.Move(i, i - 1); RenderQueue(Math.Max(0, i - 1)); }
    private void Down_Click(object sender, RoutedEventArgs e) { var i = TrackList.SelectedIndex; queue.Move(i, i + 1); RenderQueue(Math.Min(Tracks.Count - 1, i + 1)); }
    private void Remove_Click(object sender, RoutedEventArgs e) { var i = TrackList.SelectedIndex; queue.RemoveAt(i); RenderQueue(Math.Min(i, queue.Tracks.Count - 1)); }
    private void Undo_Click(object sender, RoutedEventArgs e) { queue.Undo(); RenderQueue(); }
    private void Clear_Click(object sender, RoutedEventArgs e) { queue.Clear(); RenderQueue(); }
    private void Shuffle_Click(object sender, RoutedEventArgs e) { queue.Shuffle(preferences.ArtistSpacing, Random.Shared); RenderQueue(); Message("Queue shuffled using your artist-spacing preference."); }
    private void Root_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        var control = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control);
        if (e.Key == VirtualKey.K && control.HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down))
        { SearchBox.Focus(FocusState.Keyboard); e.Handled = true; }
    }
    private async void OnClosed(object sender, WindowEventArgs args)
    {
        closing = true; lifetime.Cancel(); active?.Cancel();
        queue.Forget(); Tracks.Clear(); playlists.Clear();
        try { await ResetPlaybackAsync(); }
        finally { http.Dispose(); lifetime.Dispose(); }
    }
}
