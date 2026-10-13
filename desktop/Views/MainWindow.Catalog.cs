using Cadenza.Core.Models;
using Cadenza.Desktop.Converters;
using Cadenza.Desktop.Models;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace Cadenza.Desktop.Views;

public sealed partial class MainWindow
{
    private const int SearchDebounceMs = 350;
    private const string PauseGlyph = "";
    private const string PlayGlyph = "";

    private async void Navigate_Click(object sender, RoutedEventArgs e)
    {
        if (_signingIn)
        {
            return;
        }

        await NavigateAsync(ParseView(((Button)sender).Tag));
    }

    private async Task NavigateAsync(AppView destination)
    {
        CancelViewWork();
        _view = destination;
        _selectedPlaylist = null;
        _hasMore = false;
        _catalogController.ResetTracks();
        SettingsPanel.Visibility = ToVisibility(_view == AppView.Settings);
        UpdateNav();
        UpdateViewSurfaces();
        QueueTools.Visibility = ToVisibility(_view == AppView.Queue);
        MoreButton.Visibility = Visibility.Collapsed;
        PageTitle.Text = _view switch
        {
            AppView.Library => "Your library",
            AppView.Recommendations => "For you",
            AppView.Queue => "Listening queue",
            _ => _view.ToString()
        };
        EmptyTitle.Text = _view == AppView.Queue ? "A fresh listening session" : "Make room for your music";
        EmptyDescription.Text = _view == AppView.Queue
            ? "Add tracks from your library or search. Queue edits can be undone."
            : "Connect Spotify in Settings to browse your library and playlists.";

        if (_view == AppView.Settings)
        {
            EmptyState.Visibility = Visibility.Collapsed;
            return;
        }

        if (_view == AppView.Recommendations)
        {
            EmptyState.Visibility = Visibility.Collapsed;
            await ShowRecommendationsAsync(refresh: false);
            return;
        }

        if (_view == AppView.Queue)
        {
            RenderQueue();
            return;
        }

        UpdateEmpty();
        if (!_auth.IsConnected)
        {
            return;
        }

        if (_view == AppView.Search && string.IsNullOrWhiteSpace(SearchBox.Text))
        {
            EmptyTitle.Text = "Find something to play";
            EmptyDescription.Text = "Search for a track or artist above.";
            return;
        }

        await RunAsync(async ct =>
        {
            if (_view == AppView.Playlists)
            {
                await _catalogController.LoadPlaylistsAsync(false, ct);
                _hasMore = _catalog.HasMorePlaylists;
                MoreButton.Visibility = ToVisibility(_hasMore);
                EmptyTitle.Text = "Your playlist collection";
                EmptyDescription.Text = "Your playlists will appear here as cover records.";
                UpdateEmpty();
            }
            else
            {
                await LoadTracksAsync(false, ct);
                if (_view == AppView.Library && PinnedPlaylists.Count == 0)
                {
                    await LoadPinnedPlaylistsAsync(ct);
                }
            }
        });
    }

    private async Task LoadPinnedPlaylistsAsync(CancellationToken ct)
    {
        var pinned = await _catalogController.LoadPinnedPlaylistsAsync(ct);
        PinnedPlaylists.Clear();
        foreach (var playlist in pinned)
        {
            PinnedPlaylists.Add(playlist);
        }

        UpdatePinnedPlaylists();
        RefreshPlaylistDeck();
        if (_view == AppView.Library)
        {
            ScrollLibraryToTop();
        }
    }

    private void UpdatePinnedPlaylists() => SidebarPlaylists.Visibility = ToVisibility(PinnedPlaylists.Count > 0);

    private async void PinnedPlaylist_Click(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).Tag is Playlist playlist)
        {
            await OpenPlaylistAsync(playlist);
        }
    }

    private void ScrollLibraryToTop()
    {
        // Page replacement changes the scroll extent. Commit layout once before resetting the
        // offset so recycled containers cannot keep an old page position.
        LibraryScroll.UpdateLayout();
        LibraryScroll.VerticalAnchorRatio = double.NaN;
        LibraryScroll.ChangeView(null, 0, null, true);
    }

    private async Task LoadTracksAsync(bool append, CancellationToken ct)
    {
        TrackSource source;
        var queryOrPlaylistId = "";
        switch (_view)
        {
            case AppView.Search:
                queryOrPlaylistId = SearchBox.Text.Trim();
                if (queryOrPlaylistId.Length == 0)
                {
                    return;
                }

                source = TrackSource.Search;
                break;
            case AppView.Playlists when _selectedPlaylist is { } selected:
                source = TrackSource.Playlist;
                queryOrPlaylistId = selected.Id;
                break;
            case AppView.Library:
                source = TrackSource.SavedLibrary;
                break;
            default:
                return;
        }

        await _catalogController.LoadTracksAsync(source, queryOrPlaylistId, append, ct);
        _hasMore = _catalog.HasMoreTracks;
        MoreButton.Visibility = ToVisibility(_hasMore);
        if (Tracks.Count >= CatalogModel.ItemLimit)
        {
            Message("Showing the first 500 tracks. Refine your search or choose another playlist to keep memory use bounded.");
        }

        EmptyTitle.Text = "Nothing here yet";
        EmptyDescription.Text = "No tracks were returned. Try another search or playlist.";
        ConnectionLabel.Text = "Spotify connected";
        UpdateEmpty();
        UpdateQueueControls();
        if (source == TrackSource.SavedLibrary)
        {
            RefreshLibrary(replaceSnapshot: true);
        }

        if (!append && _view == AppView.Library)
        {
            ScrollLibraryToTop();
        }
    }

    private void UpdateNav()
    {
        var selected = AppearanceBrush("NavSelectedBrush");
        var idle = AppearanceBrush("NavIdleBrush");
        foreach (var button in new[] { NavLibrary, NavRecommendations, NavSearch, NavPlaylists, NavQueue, NavSettings })
        {
            var current = ParseView(button.Tag) == _view;
            button.Background = current ? selected : idle;
            button.BorderBrush = current ? selected : idle;

            // Selection remains visible even when contrast colors make surfaces identical.
            button.Foreground = AppearanceBrush(current ? "Accent" : "Ink");
            button.FontWeight = current ? FontWeights.SemiBold : FontWeights.Normal;
        }
    }

    private void ShowPlayGlyph(bool playing)
    {
        PlayPauseGlyph.Glyph = playing ? PauseGlyph : PlayGlyph;
        AutomationProperties.SetName(PlayPauseButton, playing ? "Pause" : "Play");
        HeroPlayGlyph.Glyph = PlayPauseGlyph.Glyph;
        HeroPlayLabel.Text = playing ? "Pause" : "Play";
        AutomationProperties.SetName(HeroPlayButton, HeroPlayLabel.Text);
        RefreshHero();
        UpdateSpriteMood();
    }

    private void UpdateViewSurfaces()
    {
        var playlistOpen = _view == AppView.Playlists && _selectedPlaylist is not null;
        LibraryScroll.Visibility = ToVisibility(_view == AppView.Library);
        CatalogTools.Visibility = ToVisibility(_view is not (AppView.Library or AppView.Recommendations));
        RecommendationsPanel.Visibility = ToVisibility(_view == AppView.Recommendations);
        PlaylistCards.Visibility = ToVisibility(_view == AppView.Playlists && _selectedPlaylist is null);
        PlaylistDetail.Visibility = ToVisibility(playlistOpen);
        TrackPanel.Visibility = ToVisibility(_view is AppView.Search or AppView.Queue || playlistOpen);
        UpdateQueueControls();
        SearchPanel.Visibility = ToVisibility(_view is not (AppView.Settings or AppView.Playlists or AppView.Recommendations));
    }

    private void UpdateEmpty()
    {
        var queueView = _view == AppView.Queue;
        EmptyAction.Content = queueView ? "Browse library" : "Connect Spotify";
        EmptyAction.Tag = (queueView ? AppView.Library : AppView.Settings).ToString();
        EmptyAction.Visibility = ToVisibility(queueView || !_auth.IsConnected);
        var empty = _view == AppView.Playlists && _selectedPlaylist is null ? Playlists.Count == 0 : Tracks.Count == 0;
        EmptyState.Visibility = ToVisibility(_view is not (AppView.Settings or AppView.Recommendations) && empty);
    }

    private async void PlaylistCard_Click(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is Playlist selected)
        {
            await OpenPlaylistAsync(selected);
        }
    }

    private async Task OpenPlaylistAsync(Playlist selected)
    {
        if (_signingIn)
        {
            return;
        }

        if (_view != AppView.Playlists)
        {
            CancelViewWork();
            _view = AppView.Playlists;
            SettingsPanel.Visibility = Visibility.Collapsed;
            QueueTools.Visibility = Visibility.Collapsed;
            UpdateNav();
        }

        _selectedPlaylist = selected;
        _catalogController.ResetTracks();
        _hasMore = false;
        MoreButton.Visibility = Visibility.Collapsed;
        PlaylistHeroTitle.Text = selected.Name;
        PlaylistHeroSubtitle.Text = selected.Subtitle;
        PlaylistHeroImage.Source = ArtworkConverter.GetArtwork(selected.ArtworkUrl);
        PageTitle.Text = "In the collection";
        UpdateViewSurfaces();
        UpdateEmpty();
        await RunAsync(ct => LoadTracksAsync(false, ct));
    }

    private async void BackToPlaylists_Click(object sender, RoutedEventArgs e) => await NavigateAsync(AppView.Playlists);

    private async void Search_Click(object sender, RoutedEventArgs e)
    {
        if (_signingIn)
        {
            return;
        }

        await NavigateAsync(AppView.Search);
    }

    private async void Search_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter || _signingIn)
        {
            return;
        }

        e.Handled = true;
        await NavigateAsync(AppView.Search);
    }

    private async void Search_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_view != AppView.Search || !_auth.IsConnected || _signingIn)
        {
            return;
        }

        await RunAsync(async ct =>
        {
            await Task.Delay(SearchDebounceMs, ct);
            if (string.IsNullOrWhiteSpace(SearchBox.Text))
            {
                Tracks.Clear();
                _hasMore = false;
                MoreButton.Visibility = Visibility.Collapsed;
                UpdateEmpty();
                return;
            }

            await LoadTracksAsync(false, ct);
        });
    }

    private async void More_Click(object sender, RoutedEventArgs e)
    {
        if (_signingIn || !_hasMore)
        {
            return;
        }

        await RunAsync(async ct =>
        {
            if (_view == AppView.Playlists && _selectedPlaylist is null)
            {
                await _catalogController.LoadPlaylistsAsync(true, ct);
                _hasMore = _catalog.HasMorePlaylists;
                MoreButton.Visibility = ToVisibility(_hasMore);
                UpdateEmpty();
            }
            else
            {
                // A changed search query starts over instead of appending to the old results.
                var append = _view != AppView.Search || _catalog.LastSearchQuery == SearchBox.Text.Trim();
                await LoadTracksAsync(append, ct);
            }
        });
    }

    private static Visibility ToVisibility(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;
}
