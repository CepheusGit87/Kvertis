using System.Buffers.Binary;

namespace Kvertis.Engine.Conversion.Images;

/// <summary>
/// Minimal JPEG marker handling for <see cref="Abstractions.MetadataPolicy.Keep"/>: reads the EXIF APP1
/// segment of a source JPEG and splices it into a freshly encoded one. SkiaSharp never writes EXIF/XMP on
/// its own, so <see cref="Abstractions.MetadataPolicy.Strip"/> needs no work at all.
/// Related: <see cref="Documents.JpegMetadataStripper"/> (removes segments for the PDF path).
/// </summary>
internal static class JpegSegments
{
    private const byte App0 = 0xE0;
    private const byte App1 = 0xE1;
    private const int MaxSegmentPayload = 0xFFFF - 2;
    private const ushort OrientationTag = 0x0112;

    /// <summary>"Exif\0\0": identifier at the start of an EXIF APP1 payload.</summary>
    public static ReadOnlySpan<byte> ExifIdentifier => "Exif\0\0"u8;

    /// <summary>
    /// Returns the payload (after marker and length) of the first EXIF APP1 segment, or null when the
    /// stream is not a JPEG or has no EXIF. Reads only the header segments, never the image data.
    /// </summary>
    public static byte[]? ReadExif(Stream stream)
    {
        Span<byte> two = stackalloc byte[2];
        if (stream.ReadAtLeast(two, 2, throwOnEndOfStream: false) < 2 || two[0] != 0xFF || two[1] != 0xD8)
        {
            return null;
        }
        while (true)
        {
            int b;
            do
            {
                b = stream.ReadByte();
            }
            while (b == 0xFF);
            if (b < 0)
            {
                return null;
            }
            var marker = (byte)b;
            if (marker is 0xD9 or 0xDA)
            {
                return null; // end of image or start of scan: no more header segments
            }
            if (marker is >= 0xD0 and <= 0xD7 or 0x01)
            {
                continue;
            }
            if (stream.ReadAtLeast(two, 2, throwOnEndOfStream: false) < 2)
            {
                return null;
            }
            var length = BinaryPrimitives.ReadUInt16BigEndian(two);
            if (length < 2)
            {
                return null;
            }
            var payload = new byte[length - 2];
            if (stream.ReadAtLeast(payload, payload.Length, throwOnEndOfStream: false) < payload.Length)
            {
                return null;
            }
            if (marker == App1 && payload.AsSpan().StartsWith(ExifIdentifier))
            {
                return payload;
            }
        }
    }

    /// <summary>
    /// Sets the EXIF orientation (IFD0 tag 0x0112) to 1 = upright in a copy of the payload. The converter bakes
    /// the orientation into the pixels, so a kept tag would rotate the picture a second time.
    /// </summary>
    public static byte[] WithUprightOrientation(byte[] exifPayload)
    {
        var copy = (byte[])exifPayload.Clone();
        var tiff = copy.AsSpan(ExifIdentifier.Length);
        if (tiff.Length < 8)
        {
            return copy;
        }
        bool little;
        if (tiff[0] == (byte)'I' && tiff[1] == (byte)'I')
        {
            little = true;
        }
        else if (tiff[0] == (byte)'M' && tiff[1] == (byte)'M')
        {
            little = false;
        }
        else
        {
            return copy;
        }
        var ifd = ReadUInt32(tiff[4..], little);
        if (ifd > (uint)tiff.Length - 2)
        {
            return copy;
        }
        var count = ReadUInt16(tiff[(int)ifd..], little);
        for (var i = 0; i < count; i++)
        {
            var entry = (int)ifd + 2 + i * 12;
            if (entry + 12 > tiff.Length)
            {
                break;
            }
            if (ReadUInt16(tiff[entry..], little) == OrientationTag && ReadUInt16(tiff[(entry + 2)..], little) == 3)
            {
                var value = tiff.Slice(entry + 8, 2);
                if (little)
                {
                    BinaryPrimitives.WriteUInt16LittleEndian(value, 1);
                }
                else
                {
                    BinaryPrimitives.WriteUInt16BigEndian(value, 1);
                }
            }
        }
        return copy;
    }

    /// <summary>
    /// Inserts an APP1 segment with <paramref name="payload"/> right after SOI (and after a JFIF APP0 if the
    /// encoder wrote one). Returns the input unchanged when it is not a JPEG or the payload does not fit a segment.
    /// </summary>
    public static byte[] InsertApp1(byte[] jpeg, ReadOnlySpan<byte> payload)
    {
        if (jpeg.Length < 4 || jpeg[0] != 0xFF || jpeg[1] != 0xD8 || payload.Length > MaxSegmentPayload)
        {
            return jpeg;
        }
        var insertAt = 2;
        if (jpeg[2] == 0xFF && jpeg[3] == App0 && jpeg.Length >= 6)
        {
            insertAt = 4 + BinaryPrimitives.ReadUInt16BigEndian(jpeg.AsSpan(4, 2));
            if (insertAt > jpeg.Length)
            {
                return jpeg;
            }
        }
        var result = new byte[jpeg.Length + 4 + payload.Length];
        jpeg.AsSpan(0, insertAt).CopyTo(result);
        result[insertAt] = 0xFF;
        result[insertAt + 1] = App1;
        BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(insertAt + 2, 2), (ushort)(payload.Length + 2));
        payload.CopyTo(result.AsSpan(insertAt + 4));
        jpeg.AsSpan(insertAt).CopyTo(result.AsSpan(insertAt + 4 + payload.Length));
        return result;
    }

    /// <summary>True when the JPEG has at least one APP1 segment (EXIF or XMP) before the image data.</summary>
    public static bool HasApp1(byte[] jpeg)
    {
        using var stream = new MemoryStream(jpeg, writable: false);
        Span<byte> two = stackalloc byte[2];
        if (stream.ReadAtLeast(two, 2, throwOnEndOfStream: false) < 2 || two[0] != 0xFF || two[1] != 0xD8)
        {
            return false;
        }
        while (true)
        {
            int b;
            do
            {
                b = stream.ReadByte();
            }
            while (b == 0xFF);
            if (b < 0 || b is 0xD9 or 0xDA)
            {
                return false;
            }
            if (b is >= 0xD0 and <= 0xD7 or 0x01)
            {
                continue;
            }
            if (b == App1)
            {
                return true;
            }
            if (stream.ReadAtLeast(two, 2, throwOnEndOfStream: false) < 2)
            {
                return false;
            }
            stream.Seek(BinaryPrimitives.ReadUInt16BigEndian(two) - 2, SeekOrigin.Current);
        }
    }

    private static ushort ReadUInt16(ReadOnlySpan<byte> span, bool little) =>
        little ? BinaryPrimitives.ReadUInt16LittleEndian(span) : BinaryPrimitives.ReadUInt16BigEndian(span);

    private static uint ReadUInt32(ReadOnlySpan<byte> span, bool little) =>
        little ? BinaryPrimitives.ReadUInt32LittleEndian(span) : BinaryPrimitives.ReadUInt32BigEndian(span);
}

/// <summary>Adds an EXIF chunk to a WebP file (RIFF container), converting a simple file to the extended VP8X layout.</summary>
internal static class WebPChunks
{
    private const byte ExifFlag = 0x08;
    private const byte AlphaFlag = 0x10;

    /// <summary>
    /// Returns the WebP with an "EXIF" chunk holding <paramref name="tiffExif"/> (TIFF header onwards, without
    /// the JPEG "Exif\0\0" identifier). Unchanged when the input is not a WebP this helper understands.
    /// </summary>
    public static byte[] AddExif(byte[] webp, ReadOnlySpan<byte> tiffExif, int width, int height)
    {
        if (webp.Length < 20 || !webp.AsSpan(0, 4).SequenceEqual("RIFF"u8) || !webp.AsSpan(8, 4).SequenceEqual("WEBP"u8))
        {
            return webp;
        }
        using var output = new MemoryStream(webp.Length + tiffExif.Length + 32);
        output.Write("RIFF"u8);
        output.Write(new byte[4]); // size, patched below
        output.Write("WEBP"u8);

        var first = webp.AsSpan(12, 4);
        if (first.SequenceEqual("VP8X"u8))
        {
            output.Write(webp.AsSpan(12));
            output.Position = 20;
            var flags = (byte)(webp[20] | ExifFlag);
            output.WriteByte(flags);
            output.Seek(0, SeekOrigin.End);
        }
        else if (first.SequenceEqual("VP8 "u8) || first.SequenceEqual("VP8L"u8))
        {
            var flags = ExifFlag;
            if (first.SequenceEqual("VP8L"u8) && webp.Length >= 25)
            {
                // VP8L header: signature byte, then 14+14 bits of size and one alpha_is_used bit (bit 28).
                var bits = BinaryPrimitives.ReadUInt32LittleEndian(webp.AsSpan(21, 4));
                if ((bits >> 28 & 1) == 1)
                {
                    flags |= AlphaFlag;
                }
            }
            output.Write("VP8X"u8);
            Span<byte> chunk = stackalloc byte[14];
            BinaryPrimitives.WriteUInt32LittleEndian(chunk, 10);
            chunk[4] = flags;
            WriteUInt24(chunk[8..], width - 1);
            WriteUInt24(chunk[11..], height - 1);
            output.Write(chunk);
            output.Write(webp.AsSpan(12));
        }
        else
        {
            return webp;
        }

        output.Write("EXIF"u8);
        Span<byte> size = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(size, (uint)tiffExif.Length);
        output.Write(size);
        output.Write(tiffExif);
        if (tiffExif.Length % 2 == 1)
        {
            output.WriteByte(0);
        }

        var bytes = output.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4, 4), (uint)(bytes.Length - 8));
        return bytes;
    }

    private static void WriteUInt24(Span<byte> target, int value)
    {
        target[0] = (byte)value;
        target[1] = (byte)(value >> 8);
        target[2] = (byte)(value >> 16);
    }
}
