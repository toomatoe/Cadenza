using System.Collections.ObjectModel;
using System.Diagnostics;
using Cadenza.Core.Models;
using Cadenza.Core.Controllers;
using Cadenza.Core.Services.Playback;
using Cadenza.Core.Services.Spotify;
using Cadenza.Desktop.Converters;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml;
using Windows.System;

namespace Cadenza.Desktop.Views;

public sealed partial class MainWindow
{
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
        playbackBusy = true; PlayPauseButton.IsEnabled = false; UpdateQueueControls();
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
            if (!closing) { PlayPauseButton.IsEnabled = true; UpdateQueueControls(); }
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
            await AuthenticatePlaybackAsync(currentEngine, ct);
            ct.ThrowIfCancellationRequested(); nativeConnected = true;
            await currentEngine.SetVolumeAsync((uint)VolumeSlider.Value, ct);
        }
        uint stamp;
        awaitingTrackLoad = true;
        try { stamp = await currentEngine.LoadTrackAsync(track.Id, ct); }
        finally { awaitingTrackLoad = false; }
        ct.ThrowIfCancellationRequested();
        playingStamp = stamp; playingTrack = track; playbackState = PlaybackState.Loading;
        NowPlayingLabel.Text = $"{track.Name} · {track.Artist}";
        NowPlayingImage.Source = ArtworkConverter.GetArtwork(track.ArtworkUrl, 112);
        PositionSlider.Maximum = Math.Max(1, track.DurationMs); PositionSlider.Value = 0; PositionSlider.IsEnabled = true;
        PlaybackLabel.Text = "Loading…"; ShowPlayGlyph(true);
        if (currentEngine.LatestPlayback is { } update) ApplyPlayback(update);
    }
    private async Task AuthenticatePlaybackAsync(NativeEngine target, CancellationToken ct)
    {
        if (!playbackAuth.IsConnected) await playbackAuth.RestoreAsync(SpotifyAuth.PlaybackClientId, ct);
        if (!playbackAuth.IsConnected)
        {
            PlaybackLabel.Text = "Approve in browser…";
            Message("One-time step: approve Spotify playback in your browser, then return here.");
            await playbackAuth.ConnectAsync(SpotifyAuth.PlaybackClientId, ct, SpotifyAuth.PlaybackRedirectUri);
            Notice.IsOpen = false;
        }
        PlaybackLabel.Text = "Connecting…";
        try { await target.AuthenticateAsync(playbackAuth.ClientId, await playbackAuth.GetAccessTokenAsync(ct), ct); }
        catch (InvalidOperationException) when (!ct.IsCancellationRequested)
        {
            // A stale playback token is the common cause; refresh once before reporting failure.
            await target.AuthenticateAsync(playbackAuth.ClientId, await playbackAuth.GetAccessTokenAsync(ct, forceRefresh: true), ct);
        }
    }
    private void ApplyPlayback(PlaybackUpdate update)
    {
        if (awaitingTrackLoad) return;
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
        ShowPlayGlyph(update.State is PlaybackState.Playing or PlaybackState.Loading);
        PlaybackLabel.Text = update.State switch
        {
            PlaybackState.Loading => "Loading…",
            PlaybackState.Paused => "Paused",
            PlaybackState.Stopped => "Stopped",
            PlaybackState.Ended => "Finished",
            _ => $"{TimeSpan.FromMilliseconds(update.PositionMs):m\\:ss} / {playingTrack.Duration}"
        };
        // Explicit queue progression, only after the native end-of-track event.
        if (update.State == PlaybackState.Ended && endedStamp != update.TrackStamp)
        {
            endedStamp = update.TrackStamp;
            if (playbackBusy) advanceAfterOperation = true;
            else _ = PlaybackWorkAsync(PlayNextAsync);
        }
    }
    private async void PlayTrack_Click(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).Tag is not Track track) return;
        if (view == "Queue")
        {
            DependencyObject? container = (DependencyObject)sender;
            while (container is not null && container is not ListViewItem)
                container = VisualTreeHelper.GetParent(container);
            var index = container is null ? -1 : TrackList.IndexFromContainer(container);
            await PlaybackWorkAsync(ct => PlayQueueEntryAsync(index, ct));
        }
        else
        {
            TrackList.SelectedItem = track; LibraryCards.SelectedItem = track;
            await PlaybackWorkAsync(ct => PlayTrackAsync(track, ct));
        }
    }
    private async Task PlayQueueEntryAsync(int index, CancellationToken ct)
    {
        try
        {
            var controller = new QueuePlaybackController(queue);
            if (!await controller.PlayAsync(index, PlayTrackAsync, ct)) Message("Your listening queue is empty.");
        }
        finally
        {
            UpdateQueueLabel();
            if (view == "Queue") RenderQueue(Math.Min(index, queue.Entries.Count - 1));
        }
    }
    private async void PlayPause_Click(object sender, RoutedEventArgs e)
    {
        await PlaybackWorkAsync(async ct =>
        {
            if (playingTrack is null)
            {
                if (view == "Queue")
                {
                    await PlayQueueEntryAsync(TrackList.SelectedIndex >= 0 ? TrackList.SelectedIndex : 0, ct);
                    return;
                }
                var selected = view == "Library" ? LibraryCards.SelectedItem as Track : TrackList.SelectedItem as Track;
                if (selected is null && queue.Entries.Count > 0) { await PlayQueueEntryAsync(0, ct); return; }
                if (selected is null) throw new InvalidOperationException("Choose a track or add one to your queue.");
                await PlayTrackAsync(selected, ct);
            }
            else if (!nativeConnected || playbackState is PlaybackState.Ended or PlaybackState.Failed or PlaybackState.Stopped) await PlayTrackAsync(playingTrack, ct);
            else if (playbackState is PlaybackState.Playing or PlaybackState.Loading) await engine!.PauseAsync(ct);
            else await engine!.ResumeAsync(ct);
        });
    }
    private Task PlayNextAsync(CancellationToken ct) => PlayQueueEntryAsync(0, ct);
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
        var old = engine; engine = null; nativeConnected = false; playingTrack = null; playingStamp = 0; endedStamp = 0;
        playbackState = PlaybackState.Stopped;
        if (old is not null) await old.DisposeAsync();
        if (!closing)
        {
            NowPlayingLabel.Text = "Choose a track to play"; NowPlayingImage.Source = null; PlaybackLabel.Text = "Not playing";
            ShowPlayGlyph(false); PositionSlider.IsEnabled = false; PositionSlider.Value = 0;
        }
    }
}
