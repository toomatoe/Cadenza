namespace Cadenza.Core.Social;

/// <summary>The music file types the library accepts: the ones the audio engine can decode.</summary>
public static class MusicFormats
{
    /// <summary>Largest upload; Cloudflare's free plan caps a request at 100 MB.</summary>
    public const long MaxUploadBytes = 95L * 1024 * 1024;

    public const int MaxTitleLength = 200;

    /// <summary>File extensions offered in the import picker.</summary>
    public static IReadOnlyList<string> Extensions { get; } = [".mp3", ".flac", ".ogg", ".oga"];

    /// <summary>Recognises a file from its first bytes, so a renamed file can't pose as music.</summary>
    /// <returns>"mp3", "flac", "ogg", or null.</returns>
    public static string? Detect(ReadOnlySpan<byte> header)
    {
        if (header.StartsWith("fLaC"u8))
        {
            return "flac";
        }

        if (header.StartsWith("OggS"u8))
        {
            return "ogg";
        }

        // An ID3v2 tag, or an MPEG audio frame sync (11 set bits) for Layer III.
        if (header.StartsWith("ID3"u8) || (header.Length >= 2 && header[0] == 0xFF && (header[1] & 0xE6) == 0xE2))
        {
            return "mp3";
        }

        return null;
    }

    public static string ContentType(string format) => format switch
    {
        "flac" => "audio/flac",
        "ogg" => "audio/ogg",
        _ => "audio/mpeg",
    };

    /// <summary>Checks upload details; returns the problem, or null when they are fine.</summary>
    public static string? Validate(UploadMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        if (string.IsNullOrWhiteSpace(metadata.Title) || string.IsNullOrWhiteSpace(metadata.Artist))
        {
            return "Every song needs a title and an artist.";
        }

        if (metadata.Title.Length > MaxTitleLength || metadata.Artist.Length > MaxTitleLength || (metadata.Album ?? "").Length > MaxTitleLength)
        {
            return $"Titles, artists and albums are limited to {MaxTitleLength} characters.";
        }

        return metadata.DurationMs < 0 ? "That duration isn't valid." : null;
    }
}
