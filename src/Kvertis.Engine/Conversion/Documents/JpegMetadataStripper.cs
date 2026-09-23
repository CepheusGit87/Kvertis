using System.Buffers.Binary;

namespace Kvertis.Engine.Conversion.Documents;

/// <summary>
/// Removes metadata segments from a JPEG without re-encoding: EXIF/XMP (APP1), IPTC (APP13),
/// comments and other application segments. Kept: JFIF (APP0), ICC profile (APP2 "ICC_PROFILE"),
/// the color transform segment (APP14) and all image data. The PDF embeds JPEG bytes as they are, so this
/// keeps EXIF and GPS out of the PDF while the picture stays bit-identical.
/// </summary>
internal static class JpegMetadataStripper
{
    private static readonly byte[] IccIdentifier = "ICC_PROFILE\0"u8.ToArray();

    /// <summary>Returns the stripped JPEG, or null if the stream is not a JPEG we can parse safely.</summary>
    public static byte[]? Strip(ReadOnlySpan<byte> jpeg)
    {
        if (jpeg.Length < 4 || jpeg[0] != 0xFF || jpeg[1] != 0xD8)
        {
            return null;
        }
        using var output = new MemoryStream(jpeg.Length);
        output.Write(jpeg[..2]);
        var pos = 2;
        while (pos < jpeg.Length)
        {
            if (jpeg[pos] != 0xFF)
            {
                return null;
            }
            // Skip fill bytes.
            while (pos < jpeg.Length && jpeg[pos] == 0xFF)
            {
                pos++;
            }
            if (pos >= jpeg.Length)
            {
                return null;
            }
            var marker = jpeg[pos];
            pos++;

            if (marker == 0xDA || marker == 0xD9)
            {
                // Start of scan (or end of image): the rest is entropy-coded data. Copy as is.
                output.Write([0xFF, marker]);
                output.Write(jpeg[pos..]);
                return output.ToArray();
            }
            if (marker is >= 0xD0 and <= 0xD7 or 0x01)
            {
                output.Write([0xFF, marker]);
                continue;
            }
            if (pos + 2 > jpeg.Length)
            {
                return null;
            }
            var length = BinaryPrimitives.ReadUInt16BigEndian(jpeg.Slice(pos, 2));
            if (length < 2 || pos + length > jpeg.Length)
            {
                return null;
            }
            var payload = jpeg.Slice(pos + 2, length - 2);
            if (Keep(marker, payload))
            {
                output.Write([0xFF, marker]);
                output.Write(jpeg.Slice(pos, length));
            }
            pos += length;
        }
        return null;
    }

    private static bool Keep(byte marker, ReadOnlySpan<byte> payload)
    {
        if (marker == 0xFE)
        {
            return false; // Comment.
        }
        if (marker is < 0xE0 or > 0xEF)
        {
            return true; // Tables, frame headers and everything else needed to decode.
        }
        return marker switch
        {
            0xE0 => true, // JFIF
            0xE2 => payload.StartsWith(IccIdentifier), // ICC profile only, not multi-picture data.
            0xEE => true, // Color transform flags (needed for CMYK/YCCK).
            _ => false,
        };
    }
}
