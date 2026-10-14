using System.Buffers.Binary;
using System.Text;

namespace Cadenza.UnitTests.Fixtures;

/// <summary>Tiny, valid-looking music file headers built in code, so the tests need no binary files.</summary>
internal static class MusicSamples
{
    /// <summary>ID3v2.3 title/artist/album, then one MPEG-1 Layer III frame with a Xing header of 1000 frames (26,122 ms).</summary>
    public static byte[] Mp3(string title, string artist, string album = "")
    {
        var frames = new List<byte>();
        void TextFrame(string id, string value)
        {
            if (value.Length == 0)
                return;

            var text = Encoding.Latin1.GetBytes(value);
            frames.AddRange(Encoding.ASCII.GetBytes(id));
            var size = new byte[4];
            BinaryPrimitives.WriteInt32BigEndian(size, text.Length + 1);
            frames.AddRange(size);
            frames.AddRange(new byte[] { 0, 0, 0 });
            frames.AddRange(text);
        }

        TextFrame("TIT2", title);
        TextFrame("TPE1", artist);
        TextFrame("TALB", album);
        var tagSize = frames.Count;
        var file = new List<byte>(Encoding.ASCII.GetBytes("ID3"))
        {
            3, 0, 0,
            (byte)((tagSize >> 21) & 0x7F), (byte)((tagSize >> 14) & 0x7F), (byte)((tagSize >> 7) & 0x7F), (byte)(tagSize & 0x7F),
        };
        file.AddRange(frames);

        var frame = new byte[417];
        frame[0] = 0xFF;
        frame[1] = 0xFB;
        frame[2] = 0x90;
        frame[3] = 0x44;
        "Xing"u8.CopyTo(frame.AsSpan(36));
        frame[43] = 0x01;
        BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(44), 1000);
        file.AddRange(frame);
        return [.. file];
    }

    /// <summary>FLAC with STREAMINFO (44.1 kHz, 441,000 samples = 10 s) and Vorbis comments.</summary>
    public static byte[] Flac(string title, string artist)
    {
        var file = new List<byte>(Encoding.ASCII.GetBytes("fLaC")) { 0x00, 0x00, 0x00, 34 };
        var info = new byte[34];
        info[10] = 0x0A;
        info[11] = 0xC4;
        info[12] = 0x42;
        BinaryPrimitives.WriteUInt32BigEndian(info.AsSpan(14), 441_000);
        file.AddRange(info);
        var comments = Comments(title, artist);
        file.Add(0x84);
        file.Add((byte)(comments.Length >> 16));
        file.Add((byte)(comments.Length >> 8));
        file.Add((byte)comments.Length);
        file.AddRange(comments);
        return [.. file];
    }

    /// <summary>Ogg Vorbis: identification header (48 kHz), comment header, and a last page at 480,000 samples (10 s).</summary>
    public static byte[] Ogg(string title, string artist)
    {
        var identification = new byte[30];
        identification[0] = 1;
        "vorbis"u8.CopyTo(identification.AsSpan(1));
        identification[11] = 2;
        BinaryPrimitives.WriteInt32LittleEndian(identification.AsSpan(12), 48_000);
        var comment = new List<byte> { 3 };
        comment.AddRange("vorbis"u8.ToArray());
        comment.AddRange(Comments(title, artist));
        comment.Add(1);
        return [.. Page(identification, 0), .. Page([.. comment], 0), .. Page(new byte[10], 480_000)];
    }

    private static byte[] Comments(string title, string artist)
    {
        var block = new List<byte>(new byte[4]);
        var entries = new[] { "TITLE=" + title, "ARTIST=" + artist };
        block.AddRange(BitConverter.GetBytes((uint)entries.Length));
        foreach (var entry in entries)
        {
            var bytes = Encoding.UTF8.GetBytes(entry);
            block.AddRange(BitConverter.GetBytes((uint)bytes.Length));
            block.AddRange(bytes);
        }

        return [.. block];
    }

    private static byte[] Page(byte[] packet, long granule)
    {
        var page = new List<byte>(Encoding.ASCII.GetBytes("OggS")) { 0, 0 };
        page.AddRange(BitConverter.GetBytes(granule));
        page.AddRange(new byte[12]);
        var segments = new List<byte>();
        var remaining = packet.Length;
        while (remaining >= 255)
        {
            segments.Add(255);
            remaining -= 255;
        }

        segments.Add((byte)remaining);
        page.Add((byte)segments.Count);
        page.AddRange(segments);
        page.AddRange(packet);
        return [.. page];
    }
}
