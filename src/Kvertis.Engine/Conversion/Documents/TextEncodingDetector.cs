using System.Text;

namespace Kvertis.Engine.Conversion.Documents;

/// <summary>
/// Picks the encoding of a text input: byte order mark first; without BOM the file is validated as
/// UTF-8 and falls back to Latin-1 when it contains byte sequences that are not valid UTF-8.
/// </summary>
internal static class TextEncodingDetector
{
    private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, throwOnInvalidBytes: true);

    public static Encoding Detect(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16);
        return Detect(stream);
    }

    public static Encoding Detect(Stream stream)
    {
        Span<byte> bom = stackalloc byte[4];
        var n = stream.ReadAtLeast(bom, 4, throwOnEndOfStream: false);
        var fromBom = FromBom(bom[..n]);
        if (fromBom is not null)
        {
            return fromBom;
        }
        stream.Seek(0, SeekOrigin.Begin);
        return IsValidUtf8(stream) ? DocumentText.Utf8 : Encoding.Latin1;
    }

    public static Encoding Detect(ReadOnlySpan<byte> bytes)
    {
        var fromBom = FromBom(bytes[..Math.Min(4, bytes.Length)]);
        if (fromBom is not null)
        {
            return fromBom;
        }
        try
        {
            _ = StrictUtf8.GetCharCount(bytes);
            return DocumentText.Utf8;
        }
        catch (DecoderFallbackException)
        {
            return Encoding.Latin1;
        }
    }

    /// <summary>Opens a reader with the detected encoding; a BOM, if any, is skipped.</summary>
    public static StreamReader OpenReader(string path)
    {
        var encoding = Detect(path);
        return new StreamReader(path, encoding, detectEncodingFromByteOrderMarks: !ReferenceEquals(encoding, Encoding.Latin1),
            new FileStreamOptions { Access = FileAccess.Read, Share = FileShare.Read, BufferSize = 1 << 16 });
    }

    public static string ReadAllText(string path)
    {
        using var reader = OpenReader(path);
        return reader.ReadToEnd();
    }

    private static Encoding? FromBom(ReadOnlySpan<byte> b)
    {
        if (b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF)
        {
            return DocumentText.Utf8;
        }
        if (b.Length >= 4 && b[0] == 0xFF && b[1] == 0xFE && b[2] == 0 && b[3] == 0)
        {
            return Encoding.UTF32;
        }
        if (b.Length >= 4 && b[0] == 0 && b[1] == 0 && b[2] == 0xFE && b[3] == 0xFF)
        {
            return new UTF32Encoding(bigEndian: true, byteOrderMark: true);
        }
        if (b.Length >= 2 && b[0] == 0xFF && b[1] == 0xFE)
        {
            return Encoding.Unicode;
        }
        if (b.Length >= 2 && b[0] == 0xFE && b[1] == 0xFF)
        {
            return Encoding.BigEndianUnicode;
        }
        return null;
    }

    private static bool IsValidUtf8(Stream stream)
    {
        var decoder = StrictUtf8.GetDecoder();
        var bytes = new byte[1 << 16];
        var chars = new char[StrictUtf8.GetMaxCharCount(bytes.Length)];
        try
        {
            int read;
            while ((read = stream.Read(bytes, 0, bytes.Length)) > 0)
            {
                decoder.GetChars(bytes, 0, read, chars, 0, flush: false);
            }
            decoder.GetChars([], 0, 0, chars, 0, flush: true);
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }
}
