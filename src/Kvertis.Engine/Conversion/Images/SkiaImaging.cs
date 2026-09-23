using System.Runtime.InteropServices;
using Kvertis.Engine.Abstractions;
using Kvertis.Engine.Formats;
using SkiaSharp;

namespace Kvertis.Engine.Conversion.Images;

/// <summary>
/// Shared SkiaSharp glue for the image converter, the image prober and the document converters:
/// which formats Skia may decode, decoding into an 8-bit sRGB bitmap with the EXIF orientation applied,
/// resizing, flattening and encoding. Skia has no external delegates, no URL loaders and no scripting
/// coders, so the old "no network, no external programs" lockdown (ADR-012, historical) is inherent.
/// Formats Skia cannot read (HEIC, AVIF, TIFF, RAW other than DNG) never reach it; they go through
/// <see cref="ISystemImageCodec"/> first.
/// </summary>
internal static class SkiaImaging
{
    /// <summary>Longest allowed edge; above this a file is rejected before pixels are allocated.</summary>
    public const int MaxEdge = 65_535;

    /// <summary>Largest decoded image (256 MP, about 1 GiB as RGBA). Guards against decompression bombs.</summary>
    public const long MaxPixels = 256L * 1024 * 1024;

    private static readonly Dictionary<FormatId, SKEncodedImageFormat> SkiaReadable = new()
    {
        [FormatRegistry.Jpg] = SKEncodedImageFormat.Jpeg,
        [FormatRegistry.Png] = SKEncodedImageFormat.Png,
        [FormatRegistry.WebP] = SKEncodedImageFormat.Webp,
        [FormatRegistry.Gif] = SKEncodedImageFormat.Gif,
        [FormatRegistry.Bmp] = SKEncodedImageFormat.Bmp,
        [FormatRegistry.Ico] = SKEncodedImageFormat.Ico,
        // RAW: only DNG is decoded by Skia (DNG SDK); every other RAW variant goes to the system codec.
        [FormatRegistry.Raw] = SKEncodedImageFormat.Dng,
    };

    /// <summary>Formats that are always decoded by the operating system (never handed to Skia).</summary>
    private static readonly HashSet<FormatId> SystemOnly = [FormatRegistry.Heic, FormatRegistry.Avif, FormatRegistry.Tiff];

    private static readonly SKSamplingOptions DownscaleSampling = new(SKFilterMode.Linear, SKMipmapMode.Linear);

    /// <summary>True when the format may be read by Skia directly (RAW: DNG only, checked at decode time).</summary>
    public static bool IsSkiaReadable(FormatId format) => SkiaReadable.ContainsKey(format);

    /// <summary>True when only the system codec may decode the format.</summary>
    public static bool IsSystemOnly(FormatId format) => SystemOnly.Contains(format);

    /// <summary>Every image format the converter can read at all (some only with a system codec).</summary>
    public static bool IsReadable(FormatId format) => IsSkiaReadable(format) || IsSystemOnly(format);

    /// <summary>
    /// Opens a Skia codec and checks that the content is what the magic bytes said. Returns null when Skia
    /// cannot open it or recognizes something else (the caller decides: CorruptFile, or system fallback for RAW).
    /// The stream is owned by the returned codec's caller and must outlive the codec.
    /// </summary>
    public static SKCodec? OpenCodec(Stream stream, FormatId expected)
    {
        if (!SkiaReadable.TryGetValue(expected, out var encoded))
        {
            return null;
        }
        var codec = SKCodec.Create(stream, out _);
        if (codec is null)
        {
            return null;
        }
        if (codec.EncodedFormat != encoded)
        {
            codec.Dispose();
            return null;
        }
        return codec;
    }

    /// <summary>
    /// Decodes the first frame of a Skia-readable file into an RGBA8888 (unpremultiplied) bitmap in sRGB.
    /// An embedded ICC profile is converted into sRGB pixels ("baked in"); the output never carries the
    /// source profile. The EXIF orientation is applied to the pixels.
    /// </summary>
    public static SKBitmap Decode(string path, FormatId expected, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var codec = OpenCodec(stream, expected)
                          ?? throw new ConversionException(ConversionErrorCode.CorruptFile, path, "decode", $"not a readable {expected} image");
        return Decode(codec, path, ct);
    }

    public static SKBitmap Decode(SKCodec codec, string path, CancellationToken ct)
    {
        var info = codec.Info;
        EnsureWithinLimits(info.Width, info.Height, path);
        var target = new SKImageInfo(info.Width, info.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul, SKColorSpace.CreateSrgb());
        var bitmap = new SKBitmap();
        try
        {
            if (!bitmap.TryAllocPixels(target))
            {
                throw new ConversionException(ConversionErrorCode.FileTooLarge, path, "decode", $"{info.Width}x{info.Height}");
            }
            ct.ThrowIfCancellationRequested();
            var result = codec.GetPixels(target, bitmap.GetPixels(), bitmap.RowBytes, new SKCodecOptions(0));
            if (result != SKCodecResult.Success)
            {
                // IncompleteInput included: a silently half-grey picture is worse than a clear error.
                throw new ConversionException(ConversionErrorCode.CorruptFile, path, "decode", result.ToString());
            }
            bitmap.NotifyPixelsChanged();
            var oriented = ApplyOrigin(bitmap, codec.EncodedOrigin);
            if (!ReferenceEquals(oriented, bitmap))
            {
                bitmap.Dispose();
            }
            return oriented;
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }
    }

    public static void EnsureWithinLimits(int width, int height, string? path)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ConversionException(ConversionErrorCode.CorruptFile, path, "decode", "empty image");
        }
        if (width > MaxEdge || height > MaxEdge || (long)width * height > MaxPixels)
        {
            throw new ConversionException(ConversionErrorCode.FileTooLarge, path, "decode", $"{width}x{height}");
        }
    }

    /// <summary>
    /// Returns a bitmap with the EXIF orientation baked into the pixels (the input itself when upright).
    /// Pure pixel copy, no resampling.
    /// </summary>
    public static SKBitmap ApplyOrigin(SKBitmap source, SKEncodedOrigin origin)
    {
        if (origin is SKEncodedOrigin.TopLeft || (int)origin == 0)
        {
            return source;
        }
        var w = source.Width;
        var h = source.Height;
        var swap = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var dw = swap ? h : w;
        var dh = swap ? w : h;
        var result = new SKBitmap(new SKImageInfo(dw, dh, source.ColorType, source.AlphaType, source.ColorSpace));
        var src = MemoryMarshal.Cast<byte, uint>(source.GetPixelSpan());
        var dst = MemoryMarshal.Cast<byte, uint>(result.GetPixelSpan());
        var srcStride = source.RowBytes / 4;
        var dstStride = result.RowBytes / 4;
        for (var y = 0; y < dh; y++)
        {
            for (var x = 0; x < dw; x++)
            {
                var (sx, sy) = origin switch
                {
                    SKEncodedOrigin.TopRight => (w - 1 - x, y),
                    SKEncodedOrigin.BottomRight => (w - 1 - x, h - 1 - y),
                    SKEncodedOrigin.BottomLeft => (x, h - 1 - y),
                    SKEncodedOrigin.LeftTop => (y, x),
                    SKEncodedOrigin.RightTop => (y, h - 1 - x),
                    SKEncodedOrigin.RightBottom => (w - 1 - y, h - 1 - x),
                    SKEncodedOrigin.LeftBottom => (w - 1 - y, x),
                    _ => (x, y),
                };
                dst[y * dstStride + x] = src[sy * srcStride + sx];
            }
        }
        result.NotifyPixelsChanged();
        return result;
    }

    /// <summary>Longest edge after limiting to <paramref name="maxDimension"/>; never upscales.</summary>
    public static (int Width, int Height) FitWithin(int width, int height, int? maxDimension)
    {
        var longest = Math.Max(width, height);
        if (maxDimension is not { } max || max <= 0 || longest <= max)
        {
            return (width, height);
        }
        var scale = (double)max / longest;
        return (Math.Max(1, (int)Math.Round(width * scale)), Math.Max(1, (int)Math.Round(height * scale)));
    }

    /// <summary>High-quality downscale (mipmapped linear filtering). Returns a new bitmap.</summary>
    public static SKBitmap Resize(SKBitmap source, int width, int height)
    {
        var info = new SKImageInfo(width, height, source.ColorType, source.AlphaType, source.ColorSpace);
        return source.Resize(info, DownscaleSampling)
               ?? throw new ConversionException(ConversionErrorCode.Unknown, step: "resize", detail: $"{width}x{height}");
    }

    /// <summary>Composites the (unpremultiplied RGBA) pixels onto white in place and marks them opaque.</summary>
    public static void FlattenOnWhite(SKBitmap bitmap)
    {
        var pixels = bitmap.GetPixelSpan();
        var row = bitmap.RowBytes;
        var any = false;
        for (var y = 0; y < bitmap.Height; y++)
        {
            var line = pixels.Slice(y * row, bitmap.Width * 4);
            for (var i = 0; i < line.Length; i += 4)
            {
                var a = line[i + 3];
                if (a == 255)
                {
                    continue;
                }
                any = true;
                var inverse = 255 - a;
                line[i] = (byte)((line[i] * a + 255 * inverse + 127) / 255);
                line[i + 1] = (byte)((line[i + 1] * a + 255 * inverse + 127) / 255);
                line[i + 2] = (byte)((line[i + 2] * a + 255 * inverse + 127) / 255);
                line[i + 3] = 255;
            }
        }
        if (any)
        {
            bitmap.NotifyPixelsChanged();
        }
    }

    /// <summary>
    /// Pixels to hand to an encoder: the bitmap without a color space, so Skia writes no ICC profile.
    /// The pixels are sRGB already (see <see cref="Decode(string, FormatId, CancellationToken)"/>).
    /// </summary>
    private static SKPixmap EncodablePixmap(SKBitmap bitmap)
    {
        using var pixmap = bitmap.PeekPixels()
                           ?? throw new ConversionException(ConversionErrorCode.Unknown, step: "encode", detail: "no pixels");
        return pixmap.WithColorSpace(null!);
    }

    public static byte[] EncodeJpeg(SKBitmap bitmap, int quality)
    {
        using var pixmap = EncodablePixmap(bitmap);
        var options = new SKJpegEncoderOptions(Math.Clamp(quality, 1, 100), SKJpegEncoderDownsample.Downsample420, SKJpegEncoderAlphaOption.Ignore);
        using var data = pixmap.Encode(options);
        return ToArray(data, "jpg");
    }

    public static byte[] EncodeWebP(SKBitmap bitmap, int quality, bool lossless)
    {
        using var pixmap = EncodablePixmap(bitmap);
        var options = lossless
            ? new SKWebpEncoderOptions(SKWebpEncoderCompression.Lossless, 75)
            : new SKWebpEncoderOptions(SKWebpEncoderCompression.Lossy, Math.Clamp(quality, 1, 100));
        using var data = pixmap.Encode(options);
        return ToArray(data, "webp");
    }

    public static byte[] EncodePng(SKBitmap bitmap, bool maxCompression = false)
    {
        using var pixmap = EncodablePixmap(bitmap);
        var options = new SKPngEncoderOptions(SKPngEncoderFilterFlags.AllFilters, maxCompression ? 9 : 6);
        using var data = pixmap.Encode(options);
        return ToArray(data, "png");
    }

    private static byte[] ToArray(SKData? data, string format) =>
        data?.ToArray() ?? throw new ConversionException(ConversionErrorCode.Unknown, step: "encode", detail: $"{format} encoder failed");
}
