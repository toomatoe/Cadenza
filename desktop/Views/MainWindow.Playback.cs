using Cadenza.Core.Controllers;
using Cadenza.Core.Models;
using Cadenza.Core.Services.Playback;
using Cadenza.Core.Services.Spotify;
using Cadenza.Core.Sprites;
using Cadenza.Desktop.Converters;
using Cadenza.Desktop.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace Cadenza.Desktop.Views;

public sealed partial class MainWindow
{
    private const int NowPlayingArtworkWidth = 112;

    private async void CheckBridge_Click(object sender, RoutedEventArgs e)
    {
        if (_signingIn)
        {
            return;
        }

        await RunAsync(async ct =>
        {
            var version = await EnsureEngine().CheckBridgeAsync(ct);
            Message(
                $"Native bridge ABI {version} responded. Playback still needs a live account and Windows audio test.",
                InfoBarSeverity.Success);
        });
    }

    private NativeEngine EnsureEngine()
    {
        if (_engine is not null)
        {
            return _engine;
        }

        var created = new NativeEngine();
        _engine = created;

        // Native events arrive on a worker thread; ignore events from an engine that has been replaced.
        created.PlaybackChanged += update => DispatcherQueue.TryEnqueue(() =>
        {
            if (!_closing && ReferenceEquals(_engine, created))
            {
                ApplyPlayback(update);
            }
        });
        return created;
    }

    /// <summary>Serializes playback commands: one runs at a time and failures are shown to the user.</summary>
    private async Task PlaybackWorkAsync(Func<CancellationToken, Task> action)
    {
        if (_closing || _signingIn || _playbackBusy)
        {
            return;
        }

        SetPlaybackBusy(true);
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _playbackOperation = operation;
        try
        {
            await action(operation.Token);
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            if (!_closing)
            {
                PlaybackLabel.Text = "Playback failed";
                Message(error.Message, InfoBarSeverity.Error);
            }
        }
        finally
        {
            _playbackBusy = false;
            if (!_closing)
            {
                SetPlaybackBusy(false);
            }

            if (ReferenceEquals(_playbackOperation, operation))
            {
                _playbackOperation = null;
            }

            if (_advanceAfterOperation && !_closing)
            {
                _advanceAfterOperation = false;
                _ = PlaybackWorkAsync(PlayNextAsync);
            }
        }
    }

    private void SetPlaybackBusy(bool busy)
    {
        _playbackBusy = busy;
        PlayPauseButton.IsEnabled = !busy;
        UpdateQueueControls();
        RefreshHero();
    }

    private async Task PlayTrackAsync(Track track, CancellationToken ct)
    {
        if (!_auth.IsConnected)
        {
            throw new InvalidOperationException("Connect Spotify in Settings first.");
        }

        var engine = EnsureEngine();
        if (!_nativeConnected)
        {
            await AuthenticatePlaybackAsync(engine, ct);
            ct.ThrowIfCancellationRequested();
            _nativeConnected = true;
            await engine.SetVolumeAsync((uint)VolumeSlider.Value, ct);
        }

        uint stamp;
        _awaitingTrackLoad = true;
        try
        {
            stamp = await engine.LoadTrackAsync(track.Id, ct);
        }
        finally
        {
            _awaitingTrackLoad = false;
        }

        ct.ThrowIfCancellationRequested();
        _playingStamp = stamp;
        _playingTrack = track;
        _playbackState = PlaybackState.Loading;
        NowPlayingLabel.Text = track.Name;
        QueueLabel.Text = track.Artist;
        NowPlayingImage.Source = ArtworkConverter.GetArtwork(track.ArtworkUrl, NowPlayingArtworkWidth);
        PositionSlider.Maximum = Math.Max(1, track.DurationMs);
        PositionSlider.Value = 0;
        PositionSlider.IsEnabled = true;
        PlaybackLabel.Text = "Loading…";
        ShowPlayGlyph(true);
        if (engine.LatestPlayback is { } update)
        {
            ApplyPlayback(update);
        }
    }

    private async Task AuthenticatePlaybackAsync(NativeEngine engine, CancellationToken ct)
    {
        if (!_playbackAuth.IsConnected)
        {
            await _playbackAuth.RestoreAsync(SpotifyAuth.PlaybackClientId, ct);
        }

        if (!_playbackAuth.IsConnected)
        {
            PlaybackLabel.Text = "Approve in browser…";
            Message("One-time step: approve Spotify playback in your browser, then return here.");
            await _playbackAuth.ConnectAsync(SpotifyAuth.PlaybackClientId, ct, SpotifyAuth.PlaybackRedirectUri);
            Notice.IsOpen = false;
        }

        PlaybackLabel.Text = "Connecting…";
        try
        {
            await engine.AuthenticateAsync(_playbackAuth.ClientId, await _playbackAuth.GetAccessTokenAsync(ct), ct);
        }
        catch (InvalidOperationException) when (!ct.IsCancellationRequested)
        {
            // A stale playback token is the common cause; refresh once before reporting failure.
            var refreshed = await _playbackAuth.GetAccessTokenAsync(ct, forceRefresh: true);
            await engine.AuthenticateAsync(_playbackAuth.ClientId, refreshed, ct);
        }
    }

    private void ApplyPlayback(PlaybackUpdate update)
    {
        if (_awaitingTrackLoad)
        {
            return;
        }

        if (update.State == PlaybackState.Failed)
        {
            if (update.TrackStamp != 0 && update.TrackStamp != _playingStamp)
            {
                return;
            }

            if (PlaybackErrors.RequiresReconnect(update.ErrorCode))
            {
                _nativeConnected = false;
            }

            _playbackState = PlaybackState.Failed;
            PlaybackLabel.Text = "Playback failed";
            ShowPlayGlyph(false);
            Message(PlaybackErrors.Describe(update.ErrorCode), InfoBarSeverity.Error);
            ReactSprite(SpriteGlyph.Question);
            return;
        }

        if (update.TrackStamp != _playingStamp || _playingTrack is null)
        {
            return;
        }

        _playbackState = update.State;
        if (!_adjustingPosition)
        {
            PositionSlider.Value = Math.Min(update.PositionMs, PositionSlider.Maximum);
        }

        ShowPlayGlyph(update.State is PlaybackState.Playing or PlaybackState.Loading);
        PlaybackLabel.Text = update.State switch
        {
            PlaybackState.Loading => "Loading…",
            PlaybackState.Paused => "Paused",
            PlaybackState.Stopped => "Stopped",
            PlaybackState.Ended => "Finished",
            _ => $"{TimeSpan.FromMilliseconds(update.PositionMs):m\\:ss} / {_playingTrack.Duration}"
        };

        // Explicit queue progression, only after the native end-of-track event.
        if (update.State == PlaybackState.Ended && _endedStamp != update.TrackStamp)
        {
            _endedStamp = update.TrackStamp;
            if (_playbackBusy)
            {
                _advanceAfterOperation = true;
            }
            else
            {
                _ = PlaybackWorkAsync(PlayNextAsync);
            }
        }
    }

    private async void PlayTrack_Click(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).Tag is not Track track)
        {
            return;
        }

        if (_view == AppView.Queue)
        {
            // The queue may hold the same track twice, so play the clicked row by position.
            var index = FindListIndex((DependencyObject)sender);
            await PlaybackWorkAsync(ct => PlayQueueEntryAsync(index, ct));
        }
        else
        {
            TrackList.SelectedItem = track;
            _selectedLibraryTrack = track;
            await PlaybackWorkAsync(ct => PlayTrackAsync(track, ct));
        }
    }

    private int FindListIndex(DependencyObject element)
    {
        var container = element;
        while (container is not null and not ListViewItem)
        {
            container = VisualTreeHelper.GetParent(container);
        }

        return container is null ? -1 : TrackList.IndexFromContainer(container);
    }

    private async Task PlayQueueEntryAsync(int index, CancellationToken ct)
    {
        try
        {
            var controller = new QueuePlaybackController(_queue);
            if (!await controller.PlayAsync(index, PlayTrackAsync, ct))
            {
                Message("Your listening queue is empty.");
            }
        }
        finally
        {
            UpdateQueueLabel();
            if (_view == AppView.Queue)
            {
                RenderQueue(Math.Min(index, _queue.Entries.Count - 1));
            }
        }
    }

    private async void PlayPause_Click(object sender, RoutedEventArgs e)
    {
        await PlaybackWorkAsync(async ct =>
        {
            if (_playingTrack is null)
            {
                await PlaySelectionAsync(ct);
            }
            else if (!_nativeConnected || _playbackState is PlaybackState.Ended or PlaybackState.Failed or PlaybackState.Stopped)
            {
                await PlayTrackAsync(_playingTrack, ct);
            }
            else if (_playbackState is PlaybackState.Playing or PlaybackState.Loading)
            {
                await EnsureEngine().PauseAsync(ct);
            }
            else
            {
                await EnsureEngine().ResumeAsync(ct);
            }
        });
    }

    /// <summary>Starts playback when nothing is playing: the selected track, else the queue's first entry.</summary>
    private async Task PlaySelectionAsync(CancellationToken ct)
    {
        if (_view == AppView.Queue)
        {
            await PlayQueueEntryAsync(Math.Max(TrackList.SelectedIndex, 0), ct);
            return;
        }

        var selected = _view == AppView.Library ? _selectedLibraryTrack : TrackList.SelectedItem as Track;
        if (selected is not null)
        {
            await PlayTrackAsync(selected, ct);
        }
        else if (_queue.Entries.Count > 0)
        {
            await PlayQueueEntryAsync(0, ct);
        }
        else
        {
            throw new InvalidOperationException("Choose a track or add one to your queue.");
        }
    }

    private Task PlayNextAsync(CancellationToken ct) => PlayQueueEntryAsync(0, ct);

    private async void Next_Click(object sender, RoutedEventArgs e) => await PlaybackWorkAsync(PlayNextAsync);

    private async void Stop_Click(object sender, RoutedEventArgs e) => await PlaybackWorkAsync(async ct =>
    {
        if (_engine is not null && _nativeConnected)
        {
            await _engine.StopAsync(ct);
        }
    });

    private void Position_Pressed(object sender, PointerRoutedEventArgs e) => _adjustingPosition = true;

    private void Position_KeyDown(object sender, KeyRoutedEventArgs e) => _adjustingPosition = true;

    private async void Position_Released(object sender, PointerRoutedEventArgs e) => await SeekAsync();

    private async void Position_KeyUp(object sender, KeyRoutedEventArgs e) => await SeekAsync();

    private async Task SeekAsync()
    {
        var position = (uint)PositionSlider.Value;
        _adjustingPosition = false;
        await PlaybackWorkAsync(async ct =>
        {
            if (_engine is not null && _nativeConnected && _playingTrack is not null)
            {
                await _engine.SeekAsync(position, ct);
            }
        });
    }

    private async void Volume_Released(object sender, PointerRoutedEventArgs e) => await SetVolumeAsync();

    private async void Volume_KeyUp(object sender, KeyRoutedEventArgs e) => await SetVolumeAsync();

    private Task SetVolumeAsync() => PlaybackWorkAsync(async ct =>
    {
        if (_engine is not null && _nativeConnected)
        {
            await _engine.SetVolumeAsync((uint)VolumeSlider.Value, ct);
        }
    });

    private async Task ResetPlaybackAsync()
    {
        _playbackOperation?.Cancel();
        _advanceAfterOperation = false;
        var previous = _engine;
        _engine = null;
        _nativeConnected = false;
        _playingTrack = null;
        _playingStamp = 0;
        _endedStamp = 0;
        _playbackState = PlaybackState.Stopped;
        if (previous is not null)
        {
            await previous.DisposeAsync();
        }

        if (!_closing)
        {
            NowPlayingLabel.Text = "Nothing playing";
            NowPlayingImage.Source = null;
            PlaybackLabel.Text = "Not playing";
            ShowPlayGlyph(false);
            PositionSlider.IsEnabled = false;
            PositionSlider.Value = 0;
        }
    }
}
