namespace Cadenza.Core.Services.Social;

/// <summary>
/// Library songs downloaded for playback, kept in one folder and capped in size. The least recently
/// played files are removed first. A song plays from here, so replays and seeking don't hit the server.
/// </summary>
public sealed class LibraryCache
{
    public const long DefaultMaxBytes = 1024L * 1024 * 1024;
    private readonly long _maxBytes;

    public LibraryCache(string folder, long maxBytes = LibraryCache.DefaultMaxBytes)
    {
        _maxBytes = maxBytes;
        Folder = folder;
    }

    public string Folder { get; }

    /// <summary>The cached file for a song, downloading it first if needed.</summary>
    public async Task<string> GetAsync(SocialClient client, string id, string format, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);
        if (id.Length == 0 || !id.All(char.IsAsciiLetterOrDigit) || format.Length == 0 || !format.All(char.IsAsciiLetterLower))
            throw new ArgumentException("Invalid library song.", nameof(id));

        Directory.CreateDirectory(Folder);
        var path = Path.Combine(Folder, $"{id}.{format}");
        if (!File.Exists(path))
        {
            await client.DownloadMusicAsync(id, path, cancellationToken);
            Trim(keep: path);
        }

        // Windows may not update access times on its own, so mark the file as just used.
        File.SetLastAccessTimeUtc(path, DateTime.UtcNow);

        return path;
    }

    /// <summary>Deletes the least recently used files until the folder fits within the cap.</summary>
    public void Trim(string? keep = null)
    {
        if (!Directory.Exists(Folder))
            return;

        var files = new DirectoryInfo(Folder).GetFiles().OrderBy(file => file.LastAccessTimeUtc).ToList();
        var total = files.Sum(file => file.Length);
        foreach (var file in files)
        {
            if (total <= _maxBytes)
                break;

            if (string.Equals(file.FullName, keep is null ? null : Path.GetFullPath(keep), StringComparison.OrdinalIgnoreCase))
                continue;

            try
            {
                total -= file.Length;
                file.Delete();
            }
            catch (IOException)
            {
                // In use by the player; it goes next time.
            }
        }
    }

    public void Clear()
    {
        if (Directory.Exists(Folder))
            Directory.Delete(Folder, recursive: true);
    }
}
