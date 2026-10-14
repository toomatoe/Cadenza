using Cadenza.Core.Exceptions;
using Cadenza.Core.Interfaces;
using Cadenza.Core.Models;
using Cadenza.Core.Models.Social;
using Cadenza.Core.Services.Music;

namespace Cadenza.Core.Services.Social;

/// <summary>
/// The shared library: uploads (tags read on this PC), search results as tracks, and playable files
/// from a bounded cache. Holds no connection of its own; the Friends client is passed in.
/// </summary>
public sealed class SharedLibraryService : ISharedLibraryService
{
    private const string DefaultFormat = "mp3";

    // Library ID → file format, learned from search and uploads; only names the cached file.
    private readonly Dictionary<string, string> _formats = [];
    private readonly LibraryCache _cache;

    /// <param name="cacheFolder">Where downloaded songs are kept (capped at <see cref="LibraryCache.DefaultMaxBytes"/>).</param>
    public SharedLibraryService(string cacheFolder)
    {
        _cache = new LibraryCache(cacheFolder);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<Track>> SearchAsync(ISocialClient client, string query, CancellationToken cancellationToken)
    {
        var found = await client.SearchMusicAsync(query, cancellationToken);
        foreach (var song in found)
            _formats[song.Id] = song.Format;

        return found.Select(song => song.ToTrack()).ToArray();
    }

    /// <inheritdoc/>
    public Task<string> GetPlayableFileAsync(ISocialClient client, Track track, CancellationToken cancellationToken) =>
        _cache.GetAsync(client, track.Id, _formats.GetValueOrDefault(track.Id, DefaultFormat), cancellationToken);

    /// <inheritdoc/>
    public async Task<UploadSummary> UploadAsync(ISocialClient client, IReadOnlyList<string> paths, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        var added = 0;
        var skipped = new List<string>();
        for (var i = 0; i < paths.Count; i++)
        {
            var name = Path.GetFileName(paths[i]);
            progress?.Report($"Adding {i + 1} of {paths.Count}: {name}…");
            if (MusicFileReader.Read(paths[i]) is not { } info)
            {
                skipped.Add($"{name} (not an MP3, FLAC or Ogg file)");
                continue;
            }

            try
            {
                var song = await client.UploadMusicAsync(paths[i], info.Metadata, cancellationToken);
                _formats[song.Id] = song.Format;
                added++;
            }
            catch (SocialException error) when (!error.IsSignedOut)
            {
                skipped.Add($"{name} ({error.Message})");
            }
        }

        return new UploadSummary(added, skipped);
    }

    /// <inheritdoc/>
    public void ClearCache() => _cache.Clear();
}
