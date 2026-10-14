using System.Buffers.Binary;
using System.Text;
using Cadenza.Core.Models.Social;
using Cadenza.Core.Services.Social;

namespace Cadenza.Core.Services.Music;

/// <summary>
/// Reads title, artist, album and duration from MP3 (ID3v2 + Xing/CBR), FLAC (STREAMINFO + Vorbis
/// comments) and Ogg Vorbis files, with no extra libraries. Missing tags fall back to the file name
/// ("Artist - Title.mp3"). Only the start and end of the file are read.
/// </summary>
public static class MusicFileReader
{
    private const int HeadBytes = 256 * 1024;
    private const int TailBytes = 64 * 1024;

    private static readonly int[] _mpeg1Bitrates = [0, 32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320];
    private static readonly int[] _mpeg2Bitrates = [0, 8, 16, 24, 32, 40, 48, 56, 64, 80, 96, 112, 128, 144, 160];
    private static readonly int[] _mpeg1Rates = [44100, 48000, 32000];

    /// <returns>The details and format, or null when the file isn't a supported music file.</returns>
    public static (UploadMetadata Metadata, string Format)? Read(string path)
    {
        using var stream = File.OpenRead(path);
        var head = new byte[(int)Math.Min(HeadBytes, stream.Length)];
        stream.ReadExactly(head);
        var format = MusicFormats.Detect(head);
        if (format is null)
            return null;

        var tail = new byte[(int)Math.Min(TailBytes, stream.Length)];
        stream.Seek(-tail.Length, SeekOrigin.End);
        stream.ReadExactly(tail);
        var tags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var duration = format switch
        {
            "flac" => ReadFlac(head, tags),
            "ogg" => ReadOgg(head, tail, tags),
            _ => ReadMp3(head, stream.Length, tags),
        };

        return (WithFallbacks(tags, Path.GetFileNameWithoutExtension(path), duration), format);
    }

    /// <summary>Uses the tags, filling gaps from a file name like "Artist - Title".</summary>
    public static UploadMetadata WithFallbacks(IReadOnlyDictionary<string, string> tags, string fileName, int durationMs)
    {
        ArgumentNullException.ThrowIfNull(tags);
        ArgumentNullException.ThrowIfNull(fileName);
        var separator = fileName.IndexOf(" - ", StringComparison.Ordinal);
        var nameArtist = separator > 0 ? fileName[..separator].Trim() : "";
        var nameTitle = separator > 0 ? fileName[(separator + 3)..].Trim() : fileName.Trim();
        string Tag(string key) => tags.TryGetValue(key, out var value) ? value.Trim() : "";
        var title = Tag("title");
        var artist = Tag("artist");

        return new UploadMetadata(
            Clip(title.Length > 0 ? title : nameTitle),
            Clip(artist.Length > 0 ? artist : nameArtist.Length > 0 ? nameArtist : "Unknown artist"),
            Clip(Tag("album")),
            Math.Max(0, durationMs));
    }

    private static string Clip(string value) => value.Length > MusicFormats.MaxTitleLength ? value[..MusicFormats.MaxTitleLength] : value;

    private static int ReadMp3(byte[] head, long fileLength, Dictionary<string, string> tags)
    {
        var audioStart = 0;
        if (head.AsSpan().StartsWith("ID3"u8) && head.Length >= 10)
        {
            var version = head[3];
            var size = SyncSafe(head.AsSpan(6, 4));
            audioStart = 10 + size + ((head[5] & 0x10) != 0 ? 10 : 0);
            ReadId3Frames(head.AsSpan(10, Math.Min(size, head.Length - 10)), version, tags);
        }

        // Find the first frame header after the tag.
        for (var i = audioStart; i + 4 <= head.Length; i++)
        {
            if (head[i] != 0xFF || (head[i + 1] & 0xE6) != 0xE2)
                continue;

            var mpeg1 = (head[i + 1] & 0x08) != 0;
            var bitrateIndex = head[i + 2] >> 4;
            var rateIndex = (head[i + 2] >> 2) & 0x03;
            if (bitrateIndex is 0 or 15 || rateIndex == 3)
                continue;

            var sampleRate = _mpeg1Rates[rateIndex] / (mpeg1 ? 1 : 2);
            var samplesPerFrame = mpeg1 ? 1152 : 576;
            var mono = (head[i + 3] >> 6) == 3;
            var sideInfo = mpeg1 ? (mono ? 17 : 32) : (mono ? 9 : 17);
            var xing = i + 4 + sideInfo;
            if (xing + 12 <= head.Length && (head.AsSpan(xing, 4).SequenceEqual("Xing"u8) || head.AsSpan(xing, 4).SequenceEqual("Info"u8)) &&
                (head[xing + 7] & 0x01) != 0)
            {
                var frames = BinaryPrimitives.ReadUInt32BigEndian(head.AsSpan(xing + 8, 4));
                return (int)Math.Min(int.MaxValue, frames * (long)samplesPerFrame * 1000 / sampleRate);
            }

            var bitrate = (mpeg1 ? _mpeg1Bitrates : _mpeg2Bitrates)[bitrateIndex] * 1000;
            return (int)Math.Min(int.MaxValue, (fileLength - i) * 8 * 1000 / bitrate);
        }

        return 0;
    }

    private static void ReadId3Frames(ReadOnlySpan<byte> frames, byte version, Dictionary<string, string> tags)
    {
        var position = 0;
        while (position + 10 <= frames.Length && frames[position] != 0)
        {
            var id = Encoding.ASCII.GetString(frames.Slice(position, 4));
            var size = version >= 4 ? SyncSafe(frames.Slice(position + 4, 4)) : (int)BinaryPrimitives.ReadUInt32BigEndian(frames.Slice(position + 4, 4));
            if (size <= 0 || position + 10 + size > frames.Length)
                break;

            var key = id switch
            {
                "TIT2" => "title",
                "TPE1" => "artist",
                "TALB" => "album",
                _ => null,
            };
            if (key is not null && size > 1)
                tags.TryAdd(key, DecodeId3Text(frames.Slice(position + 10, size)));

            position += 10 + size;
        }
    }

    private static string DecodeId3Text(ReadOnlySpan<byte> frame)
    {
        var text = frame[1..];
        var value = frame[0] switch
        {
            1 when text.Length >= 2 && text[0] == 0xFE && text[1] == 0xFF => Encoding.BigEndianUnicode.GetString(text[2..]),
            1 when text.Length >= 2 && text[0] == 0xFF && text[1] == 0xFE => Encoding.Unicode.GetString(text[2..]),
            1 => Encoding.Unicode.GetString(text),
            2 =>Encoding.BigEndianUnicode.GetString(text),
            3 => Encoding.UTF8.GetString(text),
            _ => Encoding.Latin1.GetString(text),
        };

        // Multiple values are separated by NUL; keep the first.
        var end = value.IndexOf('\0', StringComparison.Ordinal);

        return (end >= 0 ? value[..end] : value).Trim();
    }

    private static int ReadFlac(byte[] head, Dictionary<string, string> tags)
    {
        var duration = 0;
        var position = 4;
        while (position + 4 <= head.Length)
        {
            var last = (head[position] & 0x80) != 0;
            var type = head[position] & 0x7F;
            var length = (head[position + 1] << 16) | (head[position + 2] << 8) | head[position + 3];
            var body = position + 4;
            if (body + length > head.Length)
                break;

            if (type == 0 && length >= 18)
            {
                var sampleRate = (head[body + 10] << 12) | (head[body + 11] << 4) | (head[body + 12] >> 4);
                var samples = ((long)(head[body + 13] & 0x0F) << 32) | BinaryPrimitives.ReadUInt32BigEndian(head.AsSpan(body + 14, 4));
                duration = sampleRate > 0 ? (int)Math.Min(int.MaxValue, samples * 1000 / sampleRate) : 0;
            }
            else if (type == 4)
                ReadVorbisComments(head.AsSpan(body, length), tags);

            if (last)
                break;

            position = body + length;
        }

        return duration;
    }

    private static int ReadOgg(byte[] head, byte[] tail, Dictionary<string, string> tags)
    {
        var packets = OggPackets(head, maxPackets: 2);
        if (packets.Count == 0 || packets[0].Length < 16 || !packets[0].AsSpan(1, 6).SequenceEqual("vorbis"u8))
            return 0;

        var sampleRate = BinaryPrimitives.ReadInt32LittleEndian(packets[0].AsSpan(12, 4));
        if (packets.Count > 1 && packets[1].Length > 7 && packets[1][0] == 3)
            ReadVorbisComments(packets[1].AsSpan(7), tags);

        // The last page's granule position is the total sample count.
        for (var i = tail.Length - 27; i >= 0; i--)
        {
            if (tail.AsSpan(i, 4).SequenceEqual("OggS"u8))
            {
                var granule = BinaryPrimitives.ReadInt64LittleEndian(tail.AsSpan(i + 6, 8));
                return sampleRate > 0 && granule > 0 ? (int)Math.Min(int.MaxValue, granule * 1000 / sampleRate) : 0;
            }
        }

        return 0;
    }

    /// <summary>Reassembles the first packets from Ogg pages (packets may span pages).</summary>
    private static List<byte[]> OggPackets(byte[] data, int maxPackets)
    {
        var packets = new List<byte[]>();
        var current = new List<byte>();
        var position = 0;
        while (position + 27 <= data.Length && data.AsSpan(position, 4).SequenceEqual("OggS"u8) && packets.Count < maxPackets)
        {
            var segments = data[position + 26];
            var tableStart = position + 27;
            var payload = tableStart + segments;
            if (payload > data.Length)
                break;

            for (var s = 0; s < segments && packets.Count < maxPackets; s++)
            {
                var length = data[tableStart + s];
                if (payload + length > data.Length)
                    return packets;

                current.AddRange(data.AsSpan(payload, length));
                payload += length;
                if (length < 255)
                {
                    packets.Add([.. current]);
                    current.Clear();
                }
            }

            var pageBytes = 0;
            foreach (var length in data.AsSpan(tableStart, segments))
            {
                pageBytes += length;
            }

            position = tableStart + segments + pageBytes;
        }

        return packets;
    }

    private static void ReadVorbisComments(ReadOnlySpan<byte> block, Dictionary<string, string> tags)
    {
        if (block.Length < 8)
            return;

        var position = 4 + (int)BinaryPrimitives.ReadUInt32LittleEndian(block);
        if (position + 4 > block.Length)
            return;

        var count = BinaryPrimitives.ReadUInt32LittleEndian(block[position..]);
        position += 4;
        for (var i = 0; i < count && position + 4 <= block.Length; i++)
        {
            var length = (int)BinaryPrimitives.ReadUInt32LittleEndian(block[position..]);
            position += 4;
            if (length < 0 || position + length > block.Length)
                return;

            var comment = Encoding.UTF8.GetString(block.Slice(position, length));
            position += length;
            var equals = comment.IndexOf('=', StringComparison.Ordinal);
            if (equals > 0)
                tags.TryAdd(comment[..equals].ToLowerInvariant(), comment[(equals + 1)..]);
        }
    }

    private static int SyncSafe(ReadOnlySpan<byte> bytes) =>
        ((bytes[0] & 0x7F) << 21) | ((bytes[1] & 0x7F) << 14) | ((bytes[2] & 0x7F) << 7) | (bytes[3] & 0x7F);
}
