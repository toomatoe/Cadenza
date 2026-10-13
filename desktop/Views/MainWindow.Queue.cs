using System.Collections.ObjectModel;
using System.Globalization;
using Cadenza.Core.Models;
using Cadenza.Desktop.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Cadenza.Desktop.Views;

public sealed partial class MainWindow
{
    private bool _renderingQueue;

    /// <summary>
    /// Queue rows, kept apart from the catalog's <see cref="Tracks"/> so rendering the queue never
    /// rewrites search or playlist results (and can't trigger re-entrant list updates).
    /// </summary>
    public ObservableCollection<Track> QueueTracks { get; } = [];

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).Tag is Track track)
        {
            AddToQueue(track);
        }
    }

    private void AddToQueue(Track track)
    {
        try
        {
            _queue.Add(track);
            UpdateQueueLabel();
            if (_view == AppView.Queue)
            {
                RenderQueue();
            }
        }
        catch (InvalidOperationException error)
        {
            Message(error.Message, InfoBarSeverity.Warning);
        }
    }

    private void OpenTrack_Click(object sender, RoutedEventArgs e)
    {
        if (((HyperlinkButton)sender).Tag is Track track)
        {
            OpenOnSpotify(track);
        }
    }

    private void UpdateQueueLabel()
    {
        var count = _queue.Entries.Count;
        var duration = TimeSpan.FromMilliseconds(_queue.DurationMs);
        var time = duration.TotalHours >= 1
            ? $"{(int)duration.TotalHours} hr {duration.Minutes} min"
            : $"{Math.Ceiling(duration.TotalMinutes)} min";
        QueueCountBadge.Text = count.ToString(CultureInfo.CurrentCulture);
        ToolTipService.SetToolTip(QueueCountBadge, count == 0 ? "Queue empty" : $"{count} upcoming / {time}");
        QueueLabel.Text = _playingTrack?.Artist ?? "Choose a track";
        UpdateQueueControls();
        RefreshUpcoming();
    }

    private void RenderQueue(int selection = -1)
    {
        if (_renderingQueue)
        {
            return;
        }

        _renderingQueue = true;
        try
        {
            // Clear the selection before replacing rows, so selection events can't read stale
            // indexes while the list virtualizes.
            TrackList.SelectedIndex = -1;
            QueueTracks.Clear();
            foreach (var track in _queue.Tracks)
            {
                QueueTracks.Add(track);
            }

            if (selection >= 0 && selection < QueueTracks.Count)
            {
                TrackList.SelectedIndex = selection;
            }
        }
        finally
        {
            _renderingQueue = false;
        }

        UpdateQueueLabel();
        UpdateEmpty();
        UpdateQueueControls();
    }

    private void QueueSelection_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_renderingQueue)
        {
            UpdateQueueControls();
        }
    }

    private void UpdateQueueControls()
    {
        // Called during InitializeComponent before the toolbar exists, and skipped mid-render.
        if (QueuePlayButton is null || _renderingQueue)
        {
            return;
        }

        var count = _queue.Entries.Count;
        var index = _view == AppView.Queue ? TrackList.SelectedIndex : -1;
        QueueUpButton.IsEnabled = index > 0;
        QueueDownButton.IsEnabled = index >= 0 && index < count - 1;
        QueueRemoveButton.IsEnabled = index >= 0;
        QueueUndoButton.IsEnabled = _queue.CanUndo;
        QueueShuffleButton.IsEnabled = count > 1;
        QueueClearButton.IsEnabled = count > 0;
        QueuePlayButton.IsEnabled = count > 0 && !_playbackBusy;
        QueueLoadedButton.Visibility = ToVisibility(
            _view is AppView.Library or AppView.Search || (_view == AppView.Playlists && _selectedPlaylist is not null));
        QueueLoadedButton.IsEnabled = Tracks.Count > 0 && count < ListeningQueue.Capacity;
    }

    private void QueueLoaded_Click(object sender, RoutedEventArgs e)
    {
        if (_view is not (AppView.Library or AppView.Search or AppView.Playlists))
        {
            return;
        }

        try
        {
            var count = Tracks.Count;
            _queue.AddRange(Tracks);
            UpdateQueueLabel();
            Message($"Added {count} loaded tracks to your queue.", InfoBarSeverity.Success);
        }
        catch (InvalidOperationException error)
        {
            Message(error.Message, InfoBarSeverity.Warning);
        }
    }

    private void PlayNextMenu_Click(object sender, RoutedEventArgs e)
    {
        if (((MenuFlyoutItem)sender).Tag is not Track track)
        {
            return;
        }

        try
        {
            _queue.PlayNext(track);
            UpdateQueueLabel();
            if (_view == AppView.Queue)
            {
                RenderQueue();
            }

            Message($"{track.Name} will play next.");
        }
        catch (InvalidOperationException error)
        {
            Message(error.Message, InfoBarSeverity.Warning);
        }
    }

    private async void QueuePlay_Click(object sender, RoutedEventArgs e) => await PlaybackWorkAsync(PlayNextAsync);

    private void Up_Click(object sender, RoutedEventArgs e)
    {
        var index = TrackList.SelectedIndex;
        if (index <= 0)
        {
            return;
        }

        _queue.Move(index, index - 1);
        RenderQueue(index - 1);
    }

    private void Down_Click(object sender, RoutedEventArgs e)
    {
        var index = TrackList.SelectedIndex;
        if (index < 0 || index >= _queue.Entries.Count - 1)
        {
            return;
        }

        _queue.Move(index, index + 1);
        RenderQueue(index + 1);
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        var index = TrackList.SelectedIndex;
        _queue.RemoveAt(index);
        RenderQueue(Math.Min(index, _queue.Entries.Count - 1));
    }

    private void Undo_Click(object sender, RoutedEventArgs e)
    {
        _queue.Undo();
        RenderQueue();
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        _queue.Clear();
        RenderQueue();
    }

    /// <summary>Shuffles the queue from the queue toolbar or the player bar.</summary>
    private void Shuffle_Click(object sender, RoutedEventArgs e)
    {
        if (_queue.Entries.Count < 2)
        {
            Message("Add a few tracks to your queue to shuffle them.");
            return;
        }

        _queue.Shuffle(_preferences.ArtistSpacing, Random.Shared);
        UpdateQueueLabel();
        if (_view == AppView.Queue)
        {
            RenderQueue();
        }

        Message("Queue shuffled using your artist-spacing preference.");
    }
}
