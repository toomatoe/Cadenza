using System.Collections.ObjectModel;
using System.Diagnostics;
using Cadenza.Core.Models;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml;
using Windows.System;

namespace Cadenza.Desktop.Views;

public sealed partial class MainWindow
{
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
        var count = queue.Entries.Count;
        var duration = TimeSpan.FromMilliseconds(queue.DurationMs);
        var time = duration.TotalHours >= 1 ? $"{(int)duration.TotalHours} hr {duration.Minutes} min" : $"{Math.Ceiling(duration.TotalMinutes)} min";
        QueueCountBadge.Text = count.ToString();
        ToolTipService.SetToolTip(QueueCountBadge, count == 0 ? "Queue empty" : $"{count} upcoming / {time}");
        QueueLabel.Text = playingTrack?.Artist ?? "Choose a track";
        UpdateQueueControls();
        RefreshUpcoming();
    }
    private void RenderQueue(int selection = -1)
    {
        Tracks.Clear(); foreach (var track in queue.Tracks) Tracks.Add(track);
        if (selection >= 0 && selection < Tracks.Count) TrackList.SelectedIndex = selection;
        UpdateQueueLabel(); UpdateEmpty();
    }
    private void QueueSelection_Changed(object sender, SelectionChangedEventArgs e) => UpdateQueueControls();
    private void UpdateQueueControls()
    {
        if (QueuePlayButton is null) return;
        var count = queue.Entries.Count;
        var index = view == "Queue" ? TrackList.SelectedIndex : -1;
        QueueUpButton.IsEnabled = index > 0;
        QueueDownButton.IsEnabled = index >= 0 && index < count - 1;
        QueueRemoveButton.IsEnabled = index >= 0;
        QueueUndoButton.IsEnabled = queue.CanUndo;
        QueueShuffleButton.IsEnabled = count > 1;
        QueueClearButton.IsEnabled = count > 0;
        QueuePlayButton.IsEnabled = count > 0 && !playbackBusy;
        QueueLoadedButton.Visibility = view == "Library" || view == "Search" || (view == "Playlists" && selectedPlaylist is not null)
            ? Visibility.Visible : Visibility.Collapsed;
        QueueLoadedButton.IsEnabled = Tracks.Count > 0 && count < ListeningQueue.Capacity;
    }
    private void QueueLoaded_Click(object sender, RoutedEventArgs e)
    {
        if (view is not ("Library" or "Search" or "Playlists")) return;
        try
        {
            var count = Tracks.Count;
            queue.AddRange(Tracks);
            UpdateQueueLabel();
            Message($"Added {count} loaded tracks to your queue.", InfoBarSeverity.Success);
        }
        catch (InvalidOperationException error) { Message(error.Message, InfoBarSeverity.Warning); }
    }
    private void PlayNextMenu_Click(object sender, RoutedEventArgs e)
    {
        if (((MenuFlyoutItem)sender).Tag is not Track track) return;
        try
        {
            queue.PlayNext(track); UpdateQueueLabel();
            if (view == "Queue") RenderQueue();
            Message($"{track.Name} will play next.");
        }
        catch (InvalidOperationException error) { Message(error.Message, InfoBarSeverity.Warning); }
    }
    private async void QueuePlay_Click(object sender, RoutedEventArgs e) => await PlaybackWorkAsync(PlayNextAsync);
    private void Up_Click(object sender, RoutedEventArgs e) { var i = TrackList.SelectedIndex; if (i <= 0) return; queue.Move(i, i - 1); RenderQueue(i - 1); }
    private void Down_Click(object sender, RoutedEventArgs e) { var i = TrackList.SelectedIndex; if (i < 0 || i >= queue.Entries.Count - 1) return; queue.Move(i, i + 1); RenderQueue(i + 1); }
    private void Remove_Click(object sender, RoutedEventArgs e) { var i = TrackList.SelectedIndex; queue.RemoveAt(i); RenderQueue(Math.Min(i, queue.Entries.Count - 1)); }
    private void Undo_Click(object sender, RoutedEventArgs e) { queue.Undo(); RenderQueue(); }
    private void Clear_Click(object sender, RoutedEventArgs e) { queue.Clear(); RenderQueue(); }
    private void Shuffle_Click(object sender, RoutedEventArgs e) { queue.Shuffle(preferences.ArtistSpacing, Random.Shared); RenderQueue(); Message("Queue shuffled using your artist-spacing preference."); }
}
