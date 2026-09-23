using System.IO.Compression;
using System.Text;

namespace Kvertis.Engine.Tests.Formats;

/// <summary>Minimal file headers built in code. Padded to 64 bytes like a real file prefix.</summary>
internal static class FormatSamples
{
    public static byte[] Pad(byte[] header, int length = 64)
    {
        var result = new byte[Math.Max(length, header.Length)];
        header.CopyTo(result, 0);
        return result;
    }

    public static byte[] Ascii(string text) => Encoding.ASCII.GetBytes(text);

    public static byte[] Concat(params byte[][] parts) => parts.SelectMany(p => p).ToArray();

    public static byte[] Png => Pad([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, (byte)'I', (byte)'H', (byte)'D', (byte)'R']);

    public static byte[] Jpg => Pad([0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, (byte)'J', (byte)'F', (byte)'I', (byte)'F', 0x00]);

    public static byte[] Gif => Pad(Ascii("GIF89a\u0001\u0000\u0001\u0000"));

    public static byte[] WebP => Pad(Concat(Ascii("RIFF"), [0x24, 0x00, 0x00, 0x00], Ascii("WEBPVP8 ")));

    public static byte[] Wav => Pad(Concat(Ascii("RIFF"), [0x24, 0x08, 0x00, 0x00], Ascii("WAVEfmt ")));

    public static byte[] Flac => Pad(Concat(Ascii("fLaC"), [0x00, 0x00, 0x00, 0x22]));

    /// <summary>Ogg page header (27 bytes + 1 segment) followed by the first packet.</summary>
    private static byte[] OggPage(string packetMagic) => Pad(Concat(
        Ascii("OggS"), [0x00, 0x02], new byte[20], [0x01, 0x13], Ascii(packetMagic)));

    public static byte[] OggVorbis => OggPage("\u0001vorbis");

    public static byte[] Opus => OggPage("OpusHead");

    public static byte[] Mp3Id3 => Pad(Concat(Ascii("ID3"), [0x04, 0x00, 0x00, 0x00, 0x00, 0x00, 0x21]));

    public static byte[] Mp3FrameSync => Pad([0xFF, 0xFB, 0x90, 0x64, 0x00, 0x00, 0x00, 0x00]);

    private static byte[] Ebml(string docType) => Pad(Concat(
        [0x1A, 0x45, 0xDF, 0xA3, 0x9F, 0x42, 0x86, 0x81, 0x01, 0x42, 0x82, (byte)(0x80 | docType.Length)], Ascii(docType)));

    public static byte[] Mkv => Ebml("matroska");

    public static byte[] WebM => Ebml("webm");

    private static byte[] Ftyp(params string[] brands) => Pad(Concat(
        [0x00, 0x00, 0x00, (byte)(8 + 4 * brands.Length + 4)], Ascii("ftyp"), Ascii(brands[0]), [0x00, 0x00, 0x00, 0x00],
        Ascii(string.Concat(brands.Skip(1)))));

    public static byte[] Mp4 => Ftyp("isom", "isom", "iso2", "avc1", "mp41");

    public static byte[] Mov => Ftyp("qt  ", "qt  ");

    public static byte[] Heic => Ftyp("heic", "mif1", "heic");

    public static byte[] Avif => Ftyp("avif", "avif", "mif1", "miaf");

    public static byte[] Pdf => Pad(Ascii("%PDF-1.7\n1 0 obj\n"));

    public static byte[] Svg => Ascii("<?xml version=\"1.0\"?>\n<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"10\" height=\"10\"></svg>\n");

    public static byte[] Text => Encoding.UTF8.GetBytes("Hello world.\nThis is a plain text file with umlauts: äöü.\n");

    public static byte[] LegacyOffice => Pad([0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1]);

    public static byte[] Zip(params string[] entries)
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var name in entries)
            {
                var entry = zip.CreateEntry(name);
                using var writer = new StreamWriter(entry.Open());
                writer.Write("<x/>");
            }
        }
        return stream.ToArray();
    }

    public static byte[] Docx => Zip("[Content_Types].xml", "_rels/.rels", "word/document.xml");

    public static byte[] Xlsx => Zip("[Content_Types].xml", "xl/workbook.xml");

    public static byte[] Pptx => Zip("[Content_Types].xml", "ppt/presentation.xml");

    public static byte[] PlainZip => Zip("readme.txt");
}
