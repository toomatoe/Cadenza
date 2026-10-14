using Cadenza.Core.Exceptions;
using Cadenza.Core.Models;
using Cadenza.Core.Models.Social;
using Cadenza.Core.Services.Music;
using Cadenza.Core.Services.Social;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;

namespace Cadenza.Desktop.Views;

/// <summary>
/// The shared Cadenza library: songs people uploaded because they aren't on Spotify. They show up in
/// search for everyone on the server and play through the audio engine from a bounded local cache.
/// </summary>
public sealed partial class MainWindow
{
    // Library ID → file format, learned from search and uploads; only names the cached file.
    private readonly Dictionary<string, string> _libraryFormats = [];
    private LibraryCache? _libraryCache;

    private LibraryCache SongCache => _libraryCache ??= new LibraryCache(_services.DataPath("library-cache"));

    private async Task PlayLibraryTrackAsync(Track track, CancellationToken cancellationToken)
    {
        if (_social is null)
            throw new InvalidOperationException("Join Cadenza Friends to play songs from the shared library.");

        PlaybackLabel.Text = "Downloading…";
        string path;
        try
        {
            path = await SongCache.GetAsync(_social, track.Id, _libraryFormats.GetValueOrDefault(track.Id, "mp3"), cancellationToken);
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
        if (_social is null)
            return;

        IReadOnlyList<LibraryTrack> found;
        try
        {
            found = await _social.SearchMusicAsync(query, cancellationToken);
        }
        catch (SocialException)
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        for (var i = found.Count - 1; i >= 0; i--)
        {
            _libraryFormats[found[i].Id] = found[i].Format;
            Tracks.Insert(0, found[i].ToTrack());
        }

        UpdateEmpty();
    }

    private async void UploadMusic_Click(object sender, RoutedEventArgs e)
    {
        if (_social is null)
        {
            Message("Join Cadenza Friends first (Friends page). Songs you add go to your group's shared library.");
            return;
        }

        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.MusicLibrary };
        foreach (var extension in MusicFormats.Extensions)
        {
            picker.FileTypeFilter.Add(extension);
        }

        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
        var files = await picker.PickMultipleFilesAsync();
        if (files.Count == 0)
            return;

        await FriendsWorkAsync(async ct =>
        {
            var added = 0;
            var skipped = new List<string>();
            for (var i = 0; i < files.Count; i++)
            {
                var file = files[i];
                Message($"Adding {i + 1} of {files.Count}: {file.Name}…");
                if (MusicFileReader.Read(file.Path) is not { } info)
                {
                    skipped.Add($"{file.Name} (not an MP3, FLAC or Ogg file)");
                    continue;
                }

                try
                {
                    var song = await _social.UploadMusicAsync(file.Path, info.Metadata, ct);
                    _libraryFormats[song.Id] = song.Format;
                    added++;
                }
                catch (SocialException error) when (!error.IsSignedOut)
                {
                    skipped.Add($"{file.Name} ({error.Message})");
                }
            }

            var summary = added == 1 ? "Added 1 song to the shared library." : $"Added {added} songs to the shared library.";
            Message(skipped.Count == 0 ? summary + " Anyone on your server can now find it in search." : summary + " Skipped: " + string.Join("; ", skipped),
                skipped.Count == 0 ? InfoBarSeverity.Success : InfoBarSeverity.Warning);
        });
    }

    /// <summary>Library songs have no Spotify page or ID, so friends get a message pointing them to search instead.</summary>
    private Task<bool> ShareLibraryTrackAsync(Track track) =>
        SendToFriendAsync(new SendMessageRequest(
            ChatMessageKind.Text,
            $"Listen to {track.Name} by {track.Artist}. It's in our Cadenza library; search for it.",
            null));

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
