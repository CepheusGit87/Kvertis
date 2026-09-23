using System.Buffers.Binary;

namespace Kvertis.Engine.Conversion.Images;

/// <summary>Writes a single-image ICO file with PNG-compressed content (supported since Windows Vista).</summary>
internal static class IcoWriter
{
    public const int MaxDimension = 256;
    private const int HeaderSize = 6 + 16;

    public static byte[] Write(ReadOnlySpan<byte> png, int width, int height)
    {
        if (width is < 1 or > MaxDimension || height is < 1 or > MaxDimension)
        {
            throw new ArgumentOutOfRangeException(nameof(width), $"{width}x{height} exceeds {MaxDimension} px");
        }
        var result = new byte[HeaderSize + png.Length];
        var span = result.AsSpan();
        // ICONDIR: reserved, type 1 = icon, one image.
        BinaryPrimitives.WriteUInt16LittleEndian(span[0..], 0);
        BinaryPrimitives.WriteUInt16LittleEndian(span[2..], 1);
        BinaryPrimitives.WriteUInt16LittleEndian(span[4..], 1);
        // ICONDIRENTRY: 0 means 256 for width/height.
        span[6] = (byte)(width == MaxDimension ? 0 : width);
        span[7] = (byte)(height == MaxDimension ? 0 : height);
        span[8] = 0; // no palette
        span[9] = 0; // reserved
        BinaryPrimitives.WriteUInt16LittleEndian(span[10..], 1);  // color planes
        BinaryPrimitives.WriteUInt16LittleEndian(span[12..], 32); // bits per pixel
        BinaryPrimitives.WriteUInt32LittleEndian(span[14..], (uint)png.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(span[18..], HeaderSize);
        png.CopyTo(span[HeaderSize..]);
        return result;
    }
}
