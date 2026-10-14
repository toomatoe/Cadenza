using Cadenza.Core.Models;
using Cadenza.Core.Music;
using Cadenza.Core.Social;
using Cadenza.UnitTests.Fixtures;

namespace Cadenza.UnitTests.Music;

/// <summary>Reading tags from music files, the local playback cache, and library rules.</summary>
[TestClass]
public sealed class MusicLibraryTests : IDisposable
{
    private const string Mp3 = "mp3";

    private readonly DirectoryInfo _folder = Directory.CreateTempSubdirectory("cadenza-music-");

    public void Dispose() => _folder.Delete(recursive: true);

    [TestMethod]
    public void Mp3Id3TagsAreRead()
    {
        var mp3 = MusicFileReader.Read(Write("track.mp3", MusicSamples.Mp3("Night Drive", "Neon Owls", "City Lights")));

        Assert.IsTrue(mp3 is { Format: Mp3, Metadata: { Title: "Night Drive", Artist: "Neon Owls", Album: "City Lights" } }, "MP3 ID3 tags are read");
    }

    [TestMethod]
    public void Mp3DurationComesFromTheXingHeader()
    {
        var mp3 = MusicFileReader.Read(Write("track.mp3", MusicSamples.Mp3("Night Drive", "Neon Owls", "City Lights")));

        Assert.AreEqual(26_122, mp3?.Metadata.DurationMs, "MP3 duration comes from the Xing header");
    }

    [TestMethod]
    public void FlacTagsAndDurationAreRead()
    {
        var flac = MusicFileReader.Read(Write("track.flac", MusicSamples.Flac("Glass", "Ice Choir")));

        Assert.IsTrue(flac is { Format: "flac", Metadata: { Title: "Glass", Artist: "Ice Choir", DurationMs: 10_000 } }, "FLAC tags and duration are read");
    }

    [TestMethod]
    public void OggVorbisTagsAndDurationAreRead()
    {
        var ogg = MusicFileReader.Read(Write("track.ogg", MusicSamples.Ogg("Tide", "Sea Fern")));

        Assert.IsTrue(ogg is { Format: "ogg", Metadata: { Title: "Tide", Artist: "Sea Fern", DurationMs: 10_000 } }, "Ogg Vorbis tags and duration are read");
    }

    [TestMethod]
    public void UntaggedFilesFallBackToArtistDashTitleFileNames()
    {
        var untagged = MusicFileReader.Read(Write("Some Band - Some Song.mp3", MusicSamples.Mp3("", "")));

        Assert.IsTrue(untagged?.Metadata is { Title: "Some Song", Artist: "Some Band" }, "Untagged files fall back to 'Artist - Title' file names");
    }

    [TestMethod]
    public void NonMusicFilesAreNotReadAsMusic()
    {
        var notes = MusicFileReader.Read(Write("notes.mp3", "just text"u8.ToArray()));

        Assert.IsNull(notes, "Non-music files are not read as music");
    }

    [TestMethod]
    public void PlaybackCacheDropsTheLeastRecentlyPlayedSongsFirst()
    {
        var folder = Directory.CreateDirectory(Path.Combine(_folder.FullName, "cache")).FullName;
        var now = DateTime.UtcNow;
        for (var i = 0; i < 4; i++)
        {
            var path = Path.Combine(folder, $"song{i}.mp3");
            File.WriteAllBytes(path, new byte[100]);
            File.SetLastAccessTimeUtc(path, now.AddMinutes(-10 + i));
        }

        new LibraryCache(folder, maxBytes: 250).Trim();

        var left = Directory.GetFiles(folder).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToArray();
        Assert.IsTrue(left.SequenceEqual(["song2.mp3", "song3.mp3"]), "The playback cache drops the least recently played songs first");
    }

    [TestMethod]
    public void MusicFormatsAreRecognisedFromTheirFirstBytes()
    {
        Assert.AreEqual("flac", MusicFormats.Detect("fLaC"u8), "FLAC is recognised from its first bytes");
        Assert.AreEqual("ogg", MusicFormats.Detect("OggS"u8), "Ogg is recognised from its first bytes");
        Assert.AreEqual(Mp3, MusicFormats.Detect("ID3"u8), "MP3 with ID3 is recognised from its first bytes");
        Assert.AreEqual(Mp3, MusicFormats.Detect(new byte[] { 0xFF, 0xFB }), "MP3 frames are recognised from their first bytes");
    }

    [TestMethod]
    public void OtherFilesAreRefused()
    {
        Assert.IsNull(MusicFormats.Detect("<html>"u8), "HTML is refused");
        Assert.IsNull(MusicFormats.Detect("MZ"u8), "Programs are refused");
    }

    [TestMethod]
    public void SongsNeedATitle() =>
        Assert.IsNotNull(MusicFormats.Validate(new UploadMetadata("", "Artist", "", 0)), "Songs need a title");

    [TestMethod]
    public void LibrarySongsBecomeLibraryTracksThatCreditTheUploader()
    {
        var track = new LibraryTrack("abc123", "Song", "Band", "", 1000, Mp3, "mika").ToTrack();

        Assert.AreEqual(MusicSource.Library, track.Source, "Library songs become Library tracks");
        Assert.IsFalse(track.IsSpotify, "Library songs are not Spotify tracks");
        Assert.Contains("mika", track.Album, StringComparison.Ordinal, "Library tracks credit the uploader");
    }

    private string Write(string name, byte[] bytes)
    {
        var path = Path.Combine(_folder.FullName, name);
        File.WriteAllBytes(path, bytes);

        return path;
    }
}
