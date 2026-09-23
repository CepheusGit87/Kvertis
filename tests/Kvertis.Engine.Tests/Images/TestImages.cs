using System.Buffers.Binary;
using System.IO.Compression;
using Kvertis.Engine.Abstractions;
using Kvertis.Engine.Formats;
using Kvertis.Engine.Tests.Documents;
using SkiaSharp;

namespace Kvertis.Engine.Tests.Images;

/// <summary>A per-test scratch folder with helpers that build images in code with SkiaSharp (no binary test files).</summary>
public sealed class TestImages : IDisposable
{
    public string Directory { get; } = Path.Combine(Path.GetTempPath(), "kvertis-tests", Guid.NewGuid().ToString("N"));

    public TestImages()
    {
        System.IO.Directory.CreateDirectory(Directory);
    }

    public string PathFor(string fileName) => Path.Combine(Directory, fileName);

    /// <summary>Encodes with Skia (JPEG, PNG, WebP only; Skia has no other encoders).</summary>
    public static byte[] Encode(SKBitmap bitmap, SKEncodedImageFormat format, int quality = 90)
    {
        using var data = bitmap.Encode(format, quality) ?? throw new InvalidOperationException($"cannot encode {format}");
        return data.ToArray();
    }

    public string Write(SKBitmap bitmap, string fileName, SKEncodedImageFormat format, int quality = 90) =>
        Bytes(fileName, Encode(bitmap, format, quality));

    public static SKBitmap SolidBitmap(int width, int height, SKColor color)
    {
        var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        bitmap.Erase(color);
        return bitmap;
    }

    public string Solid(string fileName, SKEncodedImageFormat format, int width = 64, int height = 48, SKColor? color = null)
    {
        using var bitmap = SolidBitmap(width, height, color ?? SKColors.Red);
        return Write(bitmap, fileName, format);
    }

    /// <summary>Noisy gradient that compresses badly, so quality and size actually matter.</summary>
    public static SKBitmap NoisyBitmap(int width = 400, int height = 300, int seed = 42)
    {
        var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        var random = new Random(seed);
        var pixels = bitmap.GetPixelSpan();
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var i = y * bitmap.RowBytes + x * 4;
                var t = (double)x / width;
                pixels[i] = Clamp(255 * (1 - t) + random.Next(-60, 61));
                pixels[i + 1] = Clamp(random.Next(0, 80));
                pixels[i + 2] = Clamp(255 * t + random.Next(-60, 61));
                pixels[i + 3] = 255;
            }
        }
        bitmap.NotifyPixelsChanged();
        return bitmap;
    }

    public string Noisy(string fileName, SKEncodedImageFormat format, int width = 400, int height = 300)
    {
        using var bitmap = NoisyBitmap(width, height);
        return Write(bitmap, fileName, format, 95);
    }

    /// <summary>Three-frame animated GIF (red, green, blue, 32×32), written by hand: Skia has no GIF encoder.</summary>
    public string AnimatedGif(string fileName) => Bytes(fileName, GifWriter.Solid(32, 32, [SKColors.Red, SKColors.Lime, SKColors.Blue]));

    public string StillGif(string fileName) => Bytes(fileName, GifWriter.Solid(64, 48, [SKColors.Red]));

    /// <summary>JPEG with an EXIF APP1 segment (Artist "TestCamera", given orientation).</summary>
    public string JpegWithExif(string fileName, int width = 64, int height = 48, ushort orientation = 1)
    {
        using var bitmap = SolidBitmap(width, height, SKColors.Orange);
        return Bytes(fileName, ExifJpeg.WithExif(Encode(bitmap, SKEncodedImageFormat.Jpeg), orientation, "TestCamera"));
    }

    public string Bytes(string fileName, byte[] content)
    {
        var path = PathFor(fileName);
        File.WriteAllBytes(path, content);
        return path;
    }

    public static SKBitmap Load(string path) =>
        SKBitmap.Decode(path) ?? throw new InvalidOperationException($"cannot decode {path}");

    public static SKEncodedImageFormat FormatOf(string path)
    {
        using var codec = SKCodec.Create(path) ?? throw new InvalidOperationException($"cannot open {path}");
        return codec.EncodedFormat;
    }

    public static InputInfo Info(string path, FormatId format, int? width = null, int? height = null) =>
        new(path, format, new FormatRegistry().KindOf(format), new FileInfo(path).Length, null, width, height, null, []);

    public void Dispose()
    {
        try
        {
            System.IO.Directory.Delete(Directory, recursive: true);
        }
        catch (IOException)
        {
            // Best effort cleanup.
        }
    }

    private static byte Clamp(double value) => (byte)Math.Clamp((int)Math.Round(value), 0, 255);
}

/// <summary>Minimal GIF89a writer for tests: solid-colour frames, 4-entry palette, uncompressed-style LZW.</summary>
internal static class GifWriter
{
    public static byte[] Solid(int width, int height, IReadOnlyList<SKColor> frameColors)
    {
        using var ms = new MemoryStream();
        ms.Write("GIF89a"u8);
        WriteUInt16(ms, width);
        WriteUInt16(ms, height);
        ms.WriteByte(0x81); // global color table, 4 entries
        ms.WriteByte(0);
        ms.WriteByte(0);
        for (var i = 0; i < 4; i++)
        {
            var c = i < frameColors.Count ? frameColors[i] : SKColors.Black;
            ms.WriteByte(c.Red);
            ms.WriteByte(c.Green);
            ms.WriteByte(c.Blue);
        }
        if (frameColors.Count > 1)
        {
            // NETSCAPE2.0 looping extension.
            ms.Write([0x21, 0xFF, 0x0B]);
            ms.Write("NETSCAPE2.0"u8);
            ms.Write([0x03, 0x01, 0x00, 0x00, 0x00]);
        }
        for (var frame = 0; frame < frameColors.Count; frame++)
        {
            ms.Write([0x21, 0xF9, 0x04, 0x00, 10, 0, 0, 0]); // graphic control: 100 ms, no transparency
            ms.WriteByte(0x2C);
            WriteUInt16(ms, 0);
            WriteUInt16(ms, 0);
            WriteUInt16(ms, width);
            WriteUInt16(ms, height);
            ms.WriteByte(0);
            ms.WriteByte(2); // LZW minimum code size
            var data = Lzw(width * height, (byte)frame);
            for (var offset = 0; offset < data.Length; offset += 255)
            {
                var n = Math.Min(255, data.Length - offset);
                ms.WriteByte((byte)n);
                ms.Write(data, offset, n);
            }
            ms.WriteByte(0);
        }
        ms.WriteByte(0x3B);
        return ms.ToArray();
    }

    /// <summary>3-bit codes; a clear code before every two literals keeps the code width fixed.</summary>
    private static byte[] Lzw(int pixelCount, byte index)
    {
        const int clear = 4;
        const int end = 5;
        var codes = new List<int>();
        for (var i = 0; i < pixelCount; i++)
        {
            if (i % 2 == 0)
            {
                codes.Add(clear);
            }
            codes.Add(index);
        }
        codes.Add(end);
        var bytes = new List<byte>();
        int buffer = 0, bits = 0;
        foreach (var code in codes)
        {
            buffer |= code << bits;
            bits += 3;
            while (bits >= 8)
            {
                bytes.Add((byte)buffer);
                buffer >>= 8;
                bits -= 8;
            }
        }
        if (bits > 0)
        {
            bytes.Add((byte)buffer);
        }
        return [.. bytes];
    }

    private static void WriteUInt16(Stream s, int value)
    {
        s.WriteByte((byte)value);
        s.WriteByte((byte)(value >> 8));
    }
}

/// <summary>Minimal PNG writer for tests that need what Skia does not write (16-bit samples).</summary>
internal static class PngWriter
{
    public static byte[] Rgb16(int width, int height, ushort r, ushort g, ushort b)
    {
        using var raw = new MemoryStream();
        for (var y = 0; y < height; y++)
        {
            raw.WriteByte(0); // filter: none
            for (var x = 0; x < width; x++)
            {
                foreach (var v in new[] { r, g, b })
                {
                    raw.WriteByte((byte)(v >> 8));
                    raw.WriteByte((byte)v);
                }
            }
        }
        using var compressed = new MemoryStream();
        using (var z = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            raw.Position = 0;
            raw.CopyTo(z);
        }

        using var png = new MemoryStream();
        png.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        var ihdr = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr, width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4), height);
        ihdr[8] = 16; // bit depth
        ihdr[9] = 2;  // truecolor
        Chunk(png, "IHDR", ihdr);
        Chunk(png, "IDAT", compressed.ToArray());
        Chunk(png, "IEND", []);
        return png.ToArray();
    }

    private static void Chunk(Stream s, string type, byte[] data)
    {
        Span<byte> len = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(len, data.Length);
        s.Write(len);
        var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        s.Write(typeBytes);
        s.Write(data);
        BinaryPrimitives.WriteUInt32BigEndian(len, Crc32([.. typeBytes, .. data]));
        s.Write(len);
    }

    private static uint Crc32(byte[] data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in data)
        {
            crc ^= b;
            for (var k = 0; k < 8; k++)
            {
                crc = (crc & 1) != 0 ? 0xEDB88320u ^ (crc >> 1) : crc >> 1;
            }
        }
        return ~crc;
    }
}

/// <summary>Synchronous progress sink; System.Progress posts asynchronously and would make assertions racy.</summary>
public sealed class RecordingProgress : IProgress<ConversionProgress>
{
    private readonly List<ConversionProgress> _reports = [];

    public IReadOnlyList<ConversionProgress> Reports
    {
        get
        {
            lock (_reports)
            {
                return _reports.ToList();
            }
        }
    }

    public void Report(ConversionProgress value)
    {
        lock (_reports)
        {
            _reports.Add(value);
        }
    }
}
