using System.Collections.ObjectModel;
using System.Diagnostics;
using Cadenza.Core.Models;
using Cadenza.Desktop.Converters;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml;
using Windows.System;

namespace Cadenza.Desktop.Views;

public partial class MainWindow
{
    private async void Navigate_Click(object sender, RoutedEventArgs e)
    {
        if (signingIn) return;
        await NavigateAsync((string)((Button)sender).Tag);
    }
    private async Task NavigateAsync(string destination)
    {
        CancelViewWork();
        view = destination;
        selectedPlaylist = null;
        hasMore = false;
        catalogController.ResetTracks();
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
                selectedPlaylist = null;
                await catalogController.LoadPlaylistsAsync(false, ct);
                hasMore = catalog.HasMorePlaylists;
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
        var pinned = await catalogController.LoadPinnedPlaylistsAsync(ct);
        PinnedPlaylists.Clear();
        foreach (var item in pinned) PinnedPlaylists.Add(item);
        UpdatePinnedPlaylists();
        RefreshPlaylistDeck();
        if (view == "Library") ScrollLibraryToTop();
    }
    private void UpdatePinnedPlaylists() =>
        SidebarPlaylists.Visibility = PinnedPlaylists.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
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
        // Page replacement changes the scroll extent. Commit layout once before
        // resetting the offset so recycled containers cannot keep an old page position.
        LibraryScroll.UpdateLayout();
        libraryScroller ??= LibraryScroll;
        if (libraryScroller is null) return;
        // A newly selected library starts at its first album.
        libraryScroller.VerticalAnchorRatio = double.NaN;
        libraryScroller.ChangeView(null, 0, null, true);
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
        TrackSource source;
        var queryOrPlaylistId = "";
        switch (view)
        {
            case "Search":
                queryOrPlaylistId = SearchBox.Text.Trim();
                if (queryOrPlaylistId.Length == 0) return;
                source = TrackSource.Search;
                break;
            case "Playlists" when selectedPlaylist is Playlist selected:
                source = TrackSource.Playlist;
                queryOrPlaylistId = selected.Id;
                break;
            case "Library": source = TrackSource.SavedLibrary; break;
            default: return;
        }
        await catalogController.LoadTracksAsync(source, queryOrPlaylistId, append, ct);
        hasMore = catalog.HasMoreTracks;
        MoreButton.Visibility = hasMore ? Visibility.Visible : Visibility.Collapsed;
        if (Tracks.Count >= CatalogModel.ItemLimit) Message("Showing the first 500 tracks. Refine your search or choose another playlist to keep memory use bounded.");
        EmptyTitle.Text = "Nothing here yet";
        EmptyDescription.Text = "No tracks were returned. Try another search or playlist.";
        ConnectionLabel.Text = "Spotify connected";
        UpdateEmpty();
        UpdateQueueControls();
        if (source == TrackSource.SavedLibrary) RefreshLibrary(true);
        if (!append && view == "Library") ScrollLibraryToTop();
    }
    private void UpdateNav()
    {
        var selected = AppearanceBrush("NavSelectedBrush");
        var idle = AppearanceBrush("NavIdleBrush");
        foreach (var button in new[] { NavLibrary, NavSearch, NavPlaylists, NavQueue, NavSettings })
        {
            var current = (string)button.Tag == view;
            button.Background = current ? selected : idle;
            button.BorderBrush = current ? selected : idle;
            // Selection remains visible even when contrast colors make surfaces identical.
            button.Foreground = AppearanceBrush(current ? "Accent" : "Ink");
            button.FontWeight = current ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal;
        }
    }
    private void ShowPlayGlyph(bool playing)
    {
        PlayPauseGlyph.Glyph = playing ? "" : "";
        AutomationProperties.SetName(PlayPauseButton, playing ? "Pause" : "Play");
        HeroPlayGlyph.Glyph = PlayPauseGlyph.Glyph;
        HeroPlayLabel.Text = playing ? "Pause" : "Play";
        RefreshHero();
    }
    private void UpdateViewSurfaces()
    {
        LibraryScroll.Visibility = view == "Library" ? Visibility.Visible : Visibility.Collapsed;
        CatalogTools.Visibility = view == "Library" ? Visibility.Collapsed : Visibility.Visible;
        PlaylistCards.Visibility = view == "Playlists" && selectedPlaylist is null ? Visibility.Visible : Visibility.Collapsed;
        PlaylistDetail.Visibility = view == "Playlists" && selectedPlaylist is not null ? Visibility.Visible : Visibility.Collapsed;
        TrackPanel.Visibility = view == "Search" || view == "Queue" || (view == "Playlists" && selectedPlaylist is not null) ? Visibility.Visible : Visibility.Collapsed;
        UpdateQueueControls();
        SearchPanel.Visibility = view == "Settings" || view == "Playlists" ? Visibility.Collapsed : Visibility.Visible;
    }
    private void UpdateEmpty()
    {
        EmptyAction.Content = view == "Queue" ? "Browse library" : "Connect Spotify";
        EmptyAction.Tag = view == "Queue" ? "Library" : "Settings";
        EmptyAction.Visibility = view == "Queue" || !auth.IsConnected ? Visibility.Visible : Visibility.Collapsed;
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
        selectedPlaylist = selected;
        catalogController.ResetTracks();
        hasMore = false;
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
        if (e.ClickedItem is Track track) selectedLibraryTrack = track;
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
            if (view == "Playlists" && selectedPlaylist is null)
            {
                await catalogController.LoadPlaylistsAsync(true, ct);
                hasMore = catalog.HasMorePlaylists;
                MoreButton.Visibility = hasMore ? Visibility.Visible : Visibility.Collapsed;
                UpdateEmpty();
            }
            else await LoadTracksAsync(view != "Search" || catalog.LastSearchQuery == SearchBox.Text.Trim(), ct);
        });
    }
}
