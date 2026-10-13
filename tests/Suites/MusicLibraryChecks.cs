using Cadenza.Core.Models;
using Cadenza.Core.Music;
using Cadenza.Core.Social;
using Cadenza.Tests.Fixtures;
using Cadenza.Tests.Infrastructure;

namespace Cadenza.Tests.Suites;

/// <summary>Reading tags from music files, the local playback cache, and library rules.</summary>
internal static class MusicLibraryChecks
{
    public static void Run(CheckRunner checks)
    {
        var folder = Directory.CreateTempSubdirectory("cadenza-music-");
        try
        {
            CheckReader(checks, folder.FullName);
            CheckCache(checks, Path.Combine(folder.FullName, "cache"));
        }
        finally
        {
            folder.Delete(recursive: true);
        }

        checks.Check(MusicFormats.Detect("fLaC"u8) == "flac" && MusicFormats.Detect("OggS"u8) == "ogg" &&
            MusicFormats.Detect("ID3"u8) == "mp3" && MusicFormats.Detect(new byte[] { 0xFF, 0xFB }) == "mp3",
            "Music formats are recognised from their first bytes");
        checks.Check(MusicFormats.Detect("<html>"u8) is null && MusicFormats.Detect("MZ"u8) is null, "Other files are refused");
        checks.Check(MusicFormats.Validate(new UploadMetadata("", "Artist", "", 0)) is not null, "Songs need a title");
        var track = new LibraryTrack("abc123", "Song", "Band", "", 1000, "mp3", "mika").ToTrack();
        checks.Check(track.Source == MusicSource.Library && !track.IsSpotify && track.Album.Contains("mika", StringComparison.Ordinal),
            "Library songs become Library tracks that credit the uploader");
    }

    private static void CheckReader(CheckRunner checks, string folder)
    {
        string Write(string name, byte[] bytes)
        {
            var path = Path.Combine(folder, name);
            File.WriteAllBytes(path, bytes);
            return path;
        }

        var mp3 = MusicFileReader.Read(Write("track.mp3", MusicSamples.Mp3("Night Drive", "Neon Owls", "City Lights")));
        checks.Check(mp3 is { Format: "mp3", Metadata: { Title: "Night Drive", Artist: "Neon Owls", Album: "City Lights" } },
            "MP3 ID3 tags are read");
        checks.Check(mp3?.Metadata.DurationMs == 26_122, "MP3 duration comes from the Xing header");

        var flac = MusicFileReader.Read(Write("track.flac", MusicSamples.Flac("Glass", "Ice Choir")));
        checks.Check(flac is { Format: "flac", Metadata: { Title: "Glass", Artist: "Ice Choir", DurationMs: 10_000 } }, "FLAC tags and duration are read");

        var ogg = MusicFileReader.Read(Write("track.ogg", MusicSamples.Ogg("Tide", "Sea Fern")));
        checks.Check(ogg is { Format: "ogg", Metadata: { Title: "Tide", Artist: "Sea Fern", DurationMs: 10_000 } }, "Ogg Vorbis tags and duration are read");

        var untagged = MusicFileReader.Read(Write("Some Band - Some Song.mp3", MusicSamples.Mp3("", "")));
        checks.Check(untagged?.Metadata is { Title: "Some Song", Artist: "Some Band" }, "Untagged files fall back to 'Artist - Title' file names");
        checks.Check(MusicFileReader.Read(Write("notes.mp3", "just text"u8.ToArray())) is null, "Non-music files are not read as music");
    }

    private static void CheckCache(CheckRunner checks, string folder)
    {
        Directory.CreateDirectory(folder);
        var now = DateTime.UtcNow;
        for (var i = 0; i < 4; i++)
        {
            var path = Path.Combine(folder, $"song{i}.mp3");
            File.WriteAllBytes(path, new byte[100]);
            File.SetLastAccessTimeUtc(path, now.AddMinutes(-10 + i));
        }

        new LibraryCache(folder, maxBytes: 250).Trim();
        var left = Directory.GetFiles(folder).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToArray();
        checks.Check(left.SequenceEqual(["song2.mp3", "song3.mp3"]), "The playback cache drops the least recently played songs first");
    }
}
