using System.Collections.ObjectModel;
using Cadenza.Core.Models;
using Cadenza.Core.Models.Recommendations;
using Cadenza.Core.Services.Sprites;
using Cadenza.Desktop.Converters;
using Cadenza.Desktop.Models;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.UI;

namespace Cadenza.Desktop.Views;

public sealed partial class MainWindow
{
    private const string LikedFilterTag = "Liked";
    private const string LikedSongsTitle = "Liked songs";
    private const string FilledHeartGlyph = "";
    private const string EmptyHeartGlyph = "";
    private const int UpcomingPreviewCount = 3;
    private const int HeroArtworkWidth = 352;

    // Playlist fan geometry, in device-independent pixels.
    private const int FanPlaylistCount = 3;
    private const double FanCardWidth = 180;
    private const double FanCardHeight = 200;
    private const double FanCollapsedStep = 90;
    private const double FanExpandedStep = 190;
    private const double FanTiltDegrees = 8;
    private const double FanSideReserve = 280;
    private const double FanExpandedSideReserve = 300;

    private static readonly Color[] _fanRecordColors =
    [
        Color.FromArgb(255, 47, 111, 169),
        Color.FromArgb(255, 61, 133, 190),
        Color.FromArgb(255, 82, 154, 207),
        Color.FromArgb(255, 86, 162, 213)
    ];

    private static readonly Color _fanLabelColor = Color.FromArgb(255, 217, 240, 255);

    private readonly List<Track> _librarySnapshot = [];
    private readonly HashSet<string> _likedTrackIds = [];
    private bool _likedOnly;
    private Track? _selectedLibraryTrack;

    public ObservableCollection<Track> LibraryTracks { get; } = [];

    public ObservableCollection<QueueEntry> UpcomingTracks { get; } = [];

    private Track? HeroTrack => _playingTrack ?? _librarySnapshot.FirstOrDefault();

    private bool PlayingTrackLiked => _playingTrack is not null && _likedTrackIds.Contains(_playingTrack.Id);

    private void RefreshLibrary(bool replaceSnapshot = false)
    {
        if (replaceSnapshot)
        {
            _librarySnapshot.Clear();
            _librarySnapshot.AddRange(Tracks);
        }

        LibraryTracks.Clear();
        foreach (var track in _librarySnapshot.Where(track => !_likedOnly || _likedTrackIds.Contains(track.Id)))
        {
            LibraryTracks.Add(track);
        }

        AllTracksChip.Background = AppearanceBrush(_likedOnly ? "GlassSurface" : "Ink");
        AllTracksChip.Foreground = AppearanceBrush(_likedOnly ? "Ink" : "SolidWindow");
        LikedTracksChip.Background = AppearanceBrush(_likedOnly ? "Ink" : "GlassSurface");
        LikedTracksChip.Foreground = AppearanceBrush(_likedOnly ? "SolidWindow" : "Ink");
        AutomationProperties.SetHelpText(AllTracksChip, _likedOnly ? "Show all tracks" : "Selected filter");
        AutomationProperties.SetHelpText(LikedTracksChip, _likedOnly ? "Selected filter" : "Show tracks liked in this session");
        LikedEmpty.Visibility = ToVisibility(_likedOnly && LibraryTracks.Count == 0);
        RefreshHero();
    }

    private void RefreshHero()
    {
        var track = HeroTrack;
        HeroEyebrow.Text = _playingTrack is null ? "JUMP BACK IN" : "NOW PLAYING";
        HeroTitle.Text = track?.Name ?? "Your next favourite";
        HeroSubtitle.Text = track is null ? "Choose a track from your library" : $"{track.Artist} / {track.Album}";
        HeroArtwork.Source = track is null ? null : ArtworkConverter.GetArtwork(track.ArtworkUrl, HeroArtworkWidth);
        HeroPlayButton.IsEnabled = track is not null && !_playbackBusy;
        HeroAddButton.IsEnabled = track is not null;
        HeroOpenButton.IsEnabled = track is not null;
        LikeButton.IsEnabled = _playingTrack is not null;
        LikeGlyph.Glyph = PlayingTrackLiked ? FilledHeartGlyph : EmptyHeartGlyph;
        LikeButton.Foreground = AppearanceBrush(PlayingTrackLiked ? "Accent" : "MutedText");
    }

    private void RefreshUpcoming()
    {
        UpcomingTracks.Clear();
        foreach (var entry in _queue.Entries.Take(UpcomingPreviewCount))
        {
            UpcomingTracks.Add(entry);
        }

        HeroQueueEmpty.Visibility = ToVisibility(UpcomingTracks.Count == 0);
    }

    private void LibraryFilter_Click(object sender, RoutedEventArgs e)
    {
        _likedOnly = (string)((Button)sender).Tag == LikedFilterTag;
        RefreshLibrary();
    }

    private void Like_Click(object sender, RoutedEventArgs e)
    {
        if (_playingTrack is null)
            return;

        if (_likedTrackIds.Add(_playingTrack.Id))
        {
            ReactSprite(SpriteGlyph.Heart);
            RecordListening(ListeningEventKind.Liked, _playingTrack);
        }
        else
            _likedTrackIds.Remove(_playingTrack.Id);

        RefreshLibrary();
    }

    private async void HeroPlay_Click(object sender, RoutedEventArgs e)
    {
        if (HeroTrack is not { } track)
            return;

        if (_playingTrack is not null)
        {
            PlayPause_Click(sender, e);
            return;
        }

        _selectedLibraryTrack = track;
        await PlaybackWorkAsync(ct => PlayTrackAsync(track, ct));
    }

    private void HeroAdd_Click(object sender, RoutedEventArgs e)
    {
        if (HeroTrack is { } track)
            AddToQueue(track);
    }

    private void HeroOpen_Click(object sender, RoutedEventArgs e)
    {
        if (HeroTrack is { } track)
            OpenOnSpotify(track);
    }

    private async void UpcomingPlay_Click(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).Tag is not Guid id)
            return;

        var index = _queue.Entries.ToList().FindIndex(entry => entry.Id == id);
        await PlaybackWorkAsync(ct => PlayQueueEntryAsync(index, ct));
    }

    /// <summary>Rebuilds the fanned "Your playlists" cards: up to three pinned playlists plus Liked songs.</summary>
    private void RefreshPlaylistDeck(bool expanded = false)
    {
        PlaylistFan.Children.Clear();
        var playlists = PinnedPlaylists.Take(FanPlaylistCount).ToArray();
        var count = playlists.Length + 1;
        var step = expanded ? FanExpandedStep : FanCollapsedStep;
        PlaylistFan.Width = Math.Min(
            ((count - 1) * step) + FanCardWidth,
            Math.Max(FanCardWidth, Root.ActualWidth - FanSideReserve));
        for (var index = 0; index < count; index++)
        {
            var playlist = index < playlists.Length ? playlists[index] : null;
            PlaylistFan.Children.Add(CreateFanCard(playlist, index, count, step, expanded));
        }
    }

    private Button CreateFanCard(Playlist? playlist, int index, int count, double step, bool expanded)
    {
        var title = playlist?.Name ?? LikedSongsTitle;
        var card = new Button
        {
            Width = FanCardWidth,
            Height = FanCardHeight,
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(12),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
            Margin = new Thickness(index * step, 0, 0, 0),
            Tag = playlist,
            Background = AppearanceBrush("PlaylistFanSurface"),
            BorderBrush = AppearanceBrush("GlassEdge"),
            BorderThickness = new Thickness(1),
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new RotateTransform { Angle = FanAngle(index, count, expanded) },
            Content = CreateFanCardContent(playlist is not null, title, index)
        };
        AutomationProperties.SetName(card, $"Open {title}");
        ToolTipService.SetToolTip(card, title);

        if (playlist is not null)
            card.Click += PinnedPlaylist_Click;
        else
            card.Click += LikedSongsCard_Click;

        card.GotFocus += (_, _) => SetPlaylistFanExpanded(true);
        card.LostFocus += (_, _) => SetPlaylistFanExpanded(false);

        return card;
    }

    private Grid CreateFanCardContent(bool isPlaylist, string title, int index)
    {
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(40) });

        var record = new Grid { Width = 100, Height = 100 };
        record.Children.Add(new Ellipse { Fill = new SolidColorBrush(_fanRecordColors[index % _fanRecordColors.Length]) });
        record.Children.Add(new Border
        {
            Width = 36,
            Height = 36,
            CornerRadius = new CornerRadius(18),
            Background = isPlaylist ? new SolidColorBrush(_fanLabelColor) : AppearanceBrush("Accent"),
            Child = new Ellipse { Width = 8, Height = 8, Fill = (Brush)Application.Current.Resources["VinylFill"] }
        });
        grid.Children.Add(record);

        var caption = new Border
        {
            Background = AppearanceBrush("PlaylistCaption"),
            BorderBrush = AppearanceBrush("CaptionEdge"),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(14, 0, 8, 0),

            // Left-aligned so the start of the name stays visible where the next card overlaps.
            Child = new TextBlock
            {
                Text = title,
                FontSize = 13,
                FontWeight = FontWeights.Medium,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                TextAlignment = TextAlignment.Left,
                TextTrimming = TextTrimming.CharacterEllipsis
            }
        };
        Grid.SetRow(caption, 1);
        grid.Children.Add(caption);

        return grid;
    }

    private async void LikedSongsCard_Click(object sender, RoutedEventArgs e)
    {
        if (_view != AppView.Library)
            await NavigateAsync(AppView.Library);

        _likedOnly = true;
        RefreshLibrary();
    }

    private void SetPlaylistFanExpanded(bool expanded)
    {
        var count = PlaylistFan.Children.Count;
        if (count == 0)
            return;

        var available = Math.Max(FanCardWidth, Root.ActualWidth - FanExpandedSideReserve);
        var step = count == 1
            ? 0
            : Math.Min(expanded ? FanExpandedStep : FanCollapsedStep, (available - FanCardWidth) / (count - 1));
        PlaylistFan.Width = FanCardWidth + (step * (count - 1));
        for (var index = 0; index < count; index++)
        {
            if (PlaylistFan.Children[index] is Button card)
            {
                card.Margin = new Thickness(index * step, 0, 0, 0);
                card.RenderTransform = new RotateTransform { Angle = FanAngle(index, count, expanded) };
            }
        }
    }

    /// <summary>Spreads cards symmetrically around zero degrees; expanded cards sit flat.</summary>
    private static double FanAngle(int index, int count, bool expanded) =>
        expanded ? 0 : (index - ((count - 1) / 2.0)) * FanTiltDegrees;
}
