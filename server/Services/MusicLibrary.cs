using System.Security.Cryptography;
using Cadenza.Core.Social;
using Cadenza.Server.Exceptions;
using Cadenza.Server.Interfaces;
using Cadenza.Server.Models;
using Microsoft.Extensions.Options;

namespace Cadenza.Server.Services;

/// <summary>
/// The shared song library: files on disk named by ID (so Navidrome could index the same folder),
/// details in <see cref="ISocialStore"/>. Identical files are stored once.
/// </summary>
public sealed class MusicLibrary : IMusicLibrary
{
    private const int HeaderBytes = 16;
    private readonly ISocialStore _store;

    public MusicLibrary(ISocialStore store, IOptions<CadenzaServerOptions> options)
    {
        _store = store;
        Folder = Path.GetFullPath(options.Value.MusicFolder);
        Directory.CreateDirectory(Folder);
    }

    public string Folder { get; }

    /// <summary>Checks and stores an upload. Returns the existing song when the same file was uploaded before.</summary>
    public async Task<LibraryTrack> SaveAsync(SocialUser uploader, IFormFile file, UploadMetadata metadata, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(file);
        if (MusicFormats.Validate(metadata) is { } problem)
            throw new SocialRuleException(problem);

        if (file.Length is 0 or > MusicFormats.MaxUploadBytes)
            throw new SocialRuleException(StatusCodes.Status413PayloadTooLarge, $"Songs can be up to {MusicFormats.MaxUploadBytes / (1024 * 1024)} MB.");

        var temporary = Path.Combine(Folder, $".upload-{Guid.NewGuid():N}");
        try
        {
            string hash;
            string? format;
            await using (var input = file.OpenReadStream())
            await using (var output = File.Create(temporary))
            {
                var header = new byte[HeaderBytes];
                var read = await input.ReadAtLeastAsync(header, HeaderBytes, throwOnEndOfStream: false, cancellationToken);
                format = MusicFormats.Detect(header.AsSpan(0, read));
                if (format is null)
                    throw new SocialRuleException(StatusCodes.Status415UnsupportedMediaType, "Only MP3, FLAC and Ogg Vorbis files can be added.");

                using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                sha.AppendData(header, 0, read);
                await output.WriteAsync(header.AsMemory(0, read), cancellationToken);
                var buffer = new byte[81920];
                int count;
                while ((count = await input.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    sha.AppendData(buffer, 0, count);
                    await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
                }

                hash = Convert.ToHexStringLower(sha.GetHashAndReset());
            }

            if (_store.FindMusicByHash(hash) is { } existing)
                return existing;

            var id = Guid.NewGuid().ToString("N");
            File.Move(temporary, PathFor(id, format));
            return _store.AddMusic(uploader, id, metadata, format, hash, now);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    public string PathFor(string id, string format) => Path.Combine(Folder, $"{id}.{format}");

    /// <summary>Deletes a song the user uploaded. Returns false if it isn't theirs or doesn't exist.</summary>
    public bool Delete(string userId, string id)
    {
        if (_store.FindMusic(id) is not { } track || !_store.RemoveMusic(userId, id))
            return false;

        File.Delete(PathFor(track.Id, track.Format));

        return true;
    }
}
