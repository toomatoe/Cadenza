using System.Collections.ObjectModel;
using Cadenza.Core.Models;
using Cadenza.Desktop.Converters;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace Cadenza.Desktop.Views;

public sealed partial class MainWindow
{
    public ObservableCollection<Track> LibraryTracks { get; } = [];
    public ObservableCollection<QueueEntry> UpcomingTracks { get; } = [];
    private readonly List<Track> librarySnapshot = [];
    private readonly HashSet<string> likedTrackIds = [];
    private bool likedOnly;
    private Track? selectedLibraryTrack;
    private Track? HeroTrack => playingTrack ?? librarySnapshot.FirstOrDefault();

    private void RefreshLibrary(bool replaceSnapshot = false)
    {
        if (replaceSnapshot)
        {
            librarySnapshot.Clear(); librarySnapshot.AddRange(Tracks);
        }
        LibraryTracks.Clear();
        foreach (var track in librarySnapshot.Where(track => !likedOnly || likedTrackIds.Contains(track.Id))) LibraryTracks.Add(track);
        AllTracksChip.Background = AppearanceBrush(likedOnly ? "GlassSurface" : "Ink");
        AllTracksChip.Foreground = AppearanceBrush(likedOnly ? "Ink" : "SolidWindow");
        LikedTracksChip.Background = AppearanceBrush(likedOnly ? "Ink" : "GlassSurface");
        LikedTracksChip.Foreground = AppearanceBrush(likedOnly ? "SolidWindow" : "Ink");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetHelpText(AllTracksChip, likedOnly ? "Show all tracks" : "Selected filter");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetHelpText(LikedTracksChip, likedOnly ? "Selected filter" : "Show tracks liked in this session");
        LikedEmpty.Visibility = likedOnly && LibraryTracks.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        RefreshHero();
    }
    private void RefreshHero()
    {
        var track = HeroTrack;
        HeroEyebrow.Text = playingTrack is null ? "JUMP BACK IN" : "NOW PLAYING";
        HeroTitle.Text = track?.Name ?? "Your next favourite";
        HeroSubtitle.Text = track is null ? "Choose a track from your library" : $"{track.Artist} / {track.Album}";
        HeroArtwork.Source = track is null ? null : ArtworkConverter.GetArtwork(track.ArtworkUrl, 352);
        HeroPlayButton.IsEnabled = track is not null && !playbackBusy;
        HeroAddButton.IsEnabled = track is not null;
        HeroOpenButton.IsEnabled = track is not null;
        LikeButton.IsEnabled = playingTrack is not null;
        LikeGlyph.Glyph = playingTrack is not null && likedTrackIds.Contains(playingTrack.Id) ? "\uEB52" : "\uEB51";
        LikeButton.Foreground = AppearanceBrush(playingTrack is not null && likedTrackIds.Contains(playingTrack.Id) ? "Accent" : "MutedText");
    }
    private void RefreshUpcoming()
    {
        UpcomingTracks.Clear();
        foreach (var entry in queue.Entries.Take(3)) UpcomingTracks.Add(entry);
        HeroQueueEmpty.Visibility = UpcomingTracks.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }
    private void LibraryFilter_Click(object sender, RoutedEventArgs e)
    {
        likedOnly = (string)((Button)sender).Tag == "Liked";
        RefreshLibrary();
    }
    private void Like_Click(object sender, RoutedEventArgs e)
    {
        if (playingTrack is null) return;
        if (!likedTrackIds.Add(playingTrack.Id)) likedTrackIds.Remove(playingTrack.Id);
        RefreshLibrary();
    }
    private async void HeroPlay_Click(object sender, RoutedEventArgs e)
    {
        if (HeroTrack is not Track track) return;
        if (playingTrack is not null) { PlayPause_Click(sender, e); return; }
        selectedLibraryTrack = track;
        await PlaybackWorkAsync(ct => PlayTrackAsync(track, ct));
    }
    private void HeroAdd_Click(object sender, RoutedEventArgs e)
    {
        if (HeroTrack is not Track track) return;
        try { queue.Add(track); UpdateQueueLabel(); }
        catch (InvalidOperationException error) { Message(error.Message, InfoBarSeverity.Warning); }
    }
    private void HeroOpen_Click(object sender, RoutedEventArgs e)
    {
        if (HeroTrack is not Track track) return;
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(track.Url) { UseShellExecute = true }); }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException) { Message("Windows couldn't open the Spotify link.", InfoBarSeverity.Warning); }
    }
    private async void UpcomingPlay_Click(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).Tag is not Guid id) return;
        var index = queue.Entries.ToList().FindIndex(entry => entry.Id == id);
        await PlaybackWorkAsync(ct => PlayQueueEntryAsync(index, ct));
    }
    private void RefreshPlaylistDeck(bool expanded = false)
    {
        PlaylistFan.Children.Clear();
        var items = PinnedPlaylists.Take(3).ToArray();
        var count = items.Length + 1;
        var step = expanded ? 190.0 : 90.0;
        PlaylistFan.Width = Math.Min((count - 1) * step + 180, Math.Max(180, Root.ActualWidth - 280));
        for (var i = 0; i < count; i++)
        {
            var playlist = i < items.Length ? items[i] : null;
            var button = new Button
            {
                Width = 180, Height = 200, Padding = new Thickness(0), CornerRadius = new CornerRadius(12),
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center,
                HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch,
                Margin = new Thickness(i * step, 0, 0, 0), Tag = playlist,
                Background = AppearanceBrush("PlaylistFanSurface"), BorderBrush = AppearanceBrush("GlassEdge"), BorderThickness = new Thickness(1),
                RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5),
                RenderTransform = new RotateTransform { Angle = expanded ? 0 : (i - (count - 1) / 2.0) * 8 }
            };
            var grid = new Grid(); grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(40) });
            var record = new Grid { Width = 100, Height = 100 };
            var colors = new[] { Windows.UI.Color.FromArgb(255, 47, 111, 169), Windows.UI.Color.FromArgb(255, 61, 133, 190), Windows.UI.Color.FromArgb(255, 82, 154, 207), Windows.UI.Color.FromArgb(255, 86, 162, 213) };
            record.Children.Add(new Ellipse { Fill = new SolidColorBrush(colors[i % colors.Length]) });
            var center = new Border { Width = 36, Height = 36, CornerRadius = new CornerRadius(18), Background = AppearanceBrush("Accent") };
            if (playlist is not null) { center.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 217, 240, 255)); center.Child = new Ellipse { Width = 8, Height = 8, Fill = (Brush)Application.Current.Resources["VinylFill"] }; }
            else center.Child = new Ellipse { Width = 8, Height = 8, Fill = (Brush)Application.Current.Resources["VinylFill"] };
            record.Children.Add(center); grid.Children.Add(record);
            var caption = new Border { Background = AppearanceBrush("PlaylistCaption"), BorderBrush = AppearanceBrush("CaptionEdge"), BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(8, 0, 8, 0), Child = new TextBlock { Text = playlist?.Name ?? "Liked songs", FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.Medium, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Stretch, TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis } };
            Grid.SetRow(caption, 1); grid.Children.Add(caption); button.Content = grid;
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, $"Open {playlist?.Name ?? "Liked songs"}");
            if (playlist is not null) button.Click += PinnedPlaylist_Click;
            else button.Click += async (_, _) => { if (view != "Library") await NavigateAsync("Library"); likedOnly = true; RefreshLibrary(); };
            button.GotFocus += (_, _) => SetPlaylistFanExpanded(true);
            button.LostFocus += (_, _) => SetPlaylistFanExpanded(false);
            PlaylistFan.Children.Add(button);
        }
    }
    private void ArtworkTile_SizeChanged(object sender, SizeChangedEventArgs args)
    {
        if (sender is Border tile && args.NewSize.Width > 0 && Math.Abs(tile.Height - args.NewSize.Width) > 0.5)
            tile.Height = args.NewSize.Width;
    }
    private void SetPlaylistFanExpanded(bool expanded)
    {
        var count = PlaylistFan.Children.Count;
        if (count == 0) return;
        var available = Math.Max(180, Root.ActualWidth - 300);
        var step = count == 1 ? 0 : Math.Min(expanded ? 190 : 90, (available - 180) / (count - 1));
        PlaylistFan.Width = 180 + step * (count - 1);
        for (var i = 0; i < count; i++)
        {
            if (PlaylistFan.Children[i] is not Button button) continue;
            button.Margin = new Thickness(i * step, 0, 0, 0);
            button.RenderTransform = new RotateTransform { Angle = expanded ? 0 : (i - (count - 1) / 2.0) * 8 };
        }
    }

}
