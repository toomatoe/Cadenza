using Cadenza.Core.Exceptions;
using Cadenza.Core.Interfaces;
using Cadenza.Core.Models;
using Cadenza.Core.Services.Social;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;

namespace Cadenza.Desktop.Views;

/// <summary>
/// The shared Cadenza library in the window: the upload picker, library results in search, and playing
/// library songs. The work itself is in <see cref="ISharedLibraryService"/>.
/// </summary>
public sealed partial class MainWindow
{
    private const string JoinFirst = "Join Cadenza Friends first (Friends page). Songs you add go to your group's shared library.";

    private async Task PlayLibraryTrackAsync(Track track, CancellationToken cancellationToken)
    {
        if (FriendsPage.Client is not { } social)
            throw new InvalidOperationException("Join Cadenza Friends to play songs from the shared library.");

        PlaybackLabel.Text = "Downloading…";
        string path;
        try
        {
            path = await _services.SharedLibrary.GetPlayableFileAsync(social, track, cancellationToken);
        }
        catch (SocialException error)
        {
            throw new InvalidOperationException(error.Message, error);
        }

        var engine = EnsureEngine();
        await engine.SetVolumeAsync((uint)VolumeSlider.Value, cancellationToken);
        uint stamp;
        _awaitingTrackLoad = true;
        try
        {
            stamp = await engine.LoadFileAsync(path, cancellationToken);
        }
        finally
        {
            _awaitingTrackLoad = false;
        }

        cancellationToken.ThrowIfCancellationRequested();
        _playingFile = true;
        ShowLoadedTrack(engine, track, stamp);
    }

    /// <summary>Adds matching library songs to the top of Spotify search results. Silent when offline.</summary>
    private async Task AddLibraryResultsAsync(string query, CancellationToken cancellationToken)
    {
        if (FriendsPage.Client is not { } social)
            return;

        IReadOnlyList<Track> found;
        try
        {
            found = await _services.SharedLibrary.SearchAsync(social, query, cancellationToken);
        }
        catch (SocialException)
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        for (var i = found.Count - 1; i >= 0; i--)
            Tracks.Insert(0, found[i]);

        UpdateEmpty();
    }

    private async void UploadMusic_Click(object sender, RoutedEventArgs e)
    {
        if (FriendsPage.Client is not { } social)
        {
            Message(JoinFirst);
            return;
        }

        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.MusicLibrary };
        foreach (var extension in MusicFormats.Extensions)
            picker.FileTypeFilter.Add(extension);

        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
        var files = await picker.PickMultipleFilesAsync();
        if (files.Count == 0)
            return;

        try
        {
            var progress = new Progress<string>(text => Message(text));
            var summary = await _services.SharedLibrary.UploadAsync(social, files.Select(file => file.Path).ToArray(), progress, _lifetime.Token);
            var added = summary.Added == 1 ? "Added 1 song to the shared library." : $"Added {summary.Added} songs to the shared library.";
            if (summary.Skipped.Count == 0)
                Message(added + " Anyone on your server can now find it in search.", InfoBarSeverity.Success);
            else
                Message(added + " Skipped: " + string.Join("; ", summary.Skipped), InfoBarSeverity.Warning);
        }
        catch (SocialException error) when (error.IsSignedOut)
        {
            FriendsPage.Forget();
            Message("Your Cadenza Friends sign-in ended. Join again with a new invite.", InfoBarSeverity.Warning);
        }
        catch (SocialException error)
        {
            Message(error.Message, InfoBarSeverity.Warning);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            // The window is closing.
        }
    }

    private void OpenOnSpotify(Track track)
    {
        if (track.IsSpotify)
            OpenInBrowser(track.Url);
        else
            Message("This song is from the Cadenza library, so it has no Spotify page.");
    }

    /// <summary>The engine can take commands: signed in to Spotify, or playing a library file.</summary>
    private bool EngineReady => _engine is not null && (_nativeConnected || _playingFile);
}
