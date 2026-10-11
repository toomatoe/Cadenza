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
        QueueLabel.Text = $"{queue.Tracks.Count} tracks";
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
}
