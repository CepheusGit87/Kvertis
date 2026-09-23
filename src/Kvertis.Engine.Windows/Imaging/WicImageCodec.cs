using System.Runtime.Versioning;
using Kvertis.Engine.Abstractions;
using Kvertis.Engine.Formats;
using Kvertis.Engine.Windows.Codecs;
using Windows.Foundation;
using Windows.Graphics.Imaging;

namespace Kvertis.Engine.Windows.Imaging;

/// <summary>
/// The Windows Imaging Component as <see cref="ISystemImageCodec"/> (ADR-006). Decodes what the bundled
/// image library cannot or must not read: HEIC/HEIF (HEIF + HEVC extensions), AVIF (AV1 extension),
/// camera RAW (Raw Image Extension) and TIFF (built in, multi-page). Encodes TIFF (LZW), BMP, GIF, PNG and
/// JPEG with the built-in encoders. Kvertis ships none of these codecs. Every PNG written here is created
/// by a fresh encoder, so no metadata of the source is copied; EXIF orientation is applied to the pixels
/// and colors are converted to sRGB.
/// </summary>
[SupportedOSPlatform("windows10.0.17763.0")]
public sealed class WicImageCodec : ISystemImageCodec
{
    private const string DecodeStep = "system-decode";
    private const string EncodeStep = "system-encode";
    private const int BufferSize = 81920;

    // WICTiffCompressionOption.WICTiffCompressionLZW (wincodec.h).
    private const byte TiffCompressionLzw = 4;

    // WIC HRESULTs (wincodec.h).
    private const int WincodecErrComponentNotFound = unchecked((int)0x88982F50);
    private const int WincodecErrUnknownImageFormat = unchecked((int)0x88982F07);
    private const int WincodecErrBadImage = unchecked((int)0x88982F60);
    private const int WincodecErrBadHeader = unchecked((int)0x88982F61);

    // MF_E_TOPO_CODEC_NOT_FOUND: expected when the HEIF container is readable but the HEVC/AV1 decoder is missing (unverified).
    private const int MfErrTopoCodecNotFound = unchecked((int)0xC00D5212);

    /// <summary>WIC is part of every supported Windows version.</summary>
    public bool IsAvailable => true;

    public bool CanDecode(FormatId format)
    {
        if (format == FormatRegistry.Heic)
        {
            return MediaFoundationCapabilities.HasHeifDecoder();
        }
        if (format == FormatRegistry.Avif)
        {
            return MediaFoundationCapabilities.HasAvifDecoder();
        }
        if (format == FormatRegistry.Raw)
        {
            return MediaFoundationCapabilities.HasRawDecoder();
        }
        return format == FormatRegistry.Tiff || format == FormatRegistry.Png || format == FormatRegistry.Jpg
               || format == FormatRegistry.Gif || format == FormatRegistry.Bmp || format == FormatRegistry.Ico;
    }

    public bool CanEncode(FormatId format) => EncoderIdFor(format) is not null;

    public async Task<IReadOnlyList<string>> DecodeToPngFramesAsync(string path, string outputDirectory, int maxFrames, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentException.ThrowIfNullOrEmpty(outputDirectory);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxFrames, 1);

        var written = new List<string>();
        FormatId? expected = null;
        try
        {
            ct.ThrowIfCancellationRequested();
            Directory.CreateDirectory(outputDirectory);
            await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, FileOptions.Asynchronous);
            expected = DetectFamily(input);
            input.Position = 0;
            using var inputRas = input.AsRandomAccessStream();

            // HEIC: the HEIF decoder explicitly. Everything else: WIC picks by content, which is safe because the
            // caller verified the format by magic bytes; the chosen decoder is checked below.
            var decoder = expected == FormatRegistry.Heic
                ? await BitmapDecoder.CreateAsync(BitmapDecoder.HeifDecoderId, inputRas).AsTask(ct).ConfigureAwait(false)
                : await BitmapDecoder.CreateAsync(inputRas).AsTask(ct).ConfigureAwait(false);
            EnsureExpectedDecoder(decoder, expected, path);

            var count = (int)Math.Min(decoder.FrameCount, (uint)maxFrames);
            for (var i = 0; i < count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var frame = await decoder.GetFrameAsync((uint)i).AsTask(ct).ConfigureAwait(false);
                // Straight alpha: PNG stores unpremultiplied colour, premultiplied input would darken soft edges.
                using var bitmap = await frame.GetSoftwareBitmapAsync(
                    BitmapPixelFormat.Bgra8,
                    BitmapAlphaMode.Straight,
                    new BitmapTransform(),
                    ExifOrientationMode.RespectExifOrientation,
                    ColorManagementMode.ColorManageToSRgb).AsTask(ct).ConfigureAwait(false);

                var pngPath = Path.Combine(outputDirectory, $"frame{i + 1:000}.png");
                written.Add(pngPath);
                await WriteAsync(bitmap, pngPath, BitmapEncoder.PngEncoderId, options: null, ct).ConfigureAwait(false);
            }
            return written;
        }
        catch (Exception ex)
        {
            foreach (var file in written)
            {
                TryDelete(file);
            }
            throw Map(ex, path, DecodeStep, expected);
        }
    }

    public async Task EncodeFromPngAsync(string pngPath, string outputPath, FormatId format, int quality, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(pngPath);
        ArgumentException.ThrowIfNullOrEmpty(outputPath);
        var encoderId = EncoderIdFor(format)
                        ?? throw new ConversionException(ConversionErrorCode.MissingSystemCodec, outputPath, EncodeStep, $"no system encoder for '{format}'");
        var outputCreated = false;
        try
        {
            ct.ThrowIfCancellationRequested();
            SoftwareBitmap bitmap;
            await using (var input = new FileStream(pngPath, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, FileOptions.Asynchronous))
            {
                using var inputRas = input.AsRandomAccessStream();
                var decoder = await BitmapDecoder.CreateAsync(BitmapDecoder.PngDecoderId, inputRas).AsTask(ct).ConfigureAwait(false);
                bitmap = await decoder.GetSoftwareBitmapAsync(
                    BitmapPixelFormat.Bgra8,
                    BitmapAlphaMode.Straight,
                    new BitmapTransform(),
                    ExifOrientationMode.IgnoreExifOrientation,
                    ColorManagementMode.DoNotColorManage).AsTask(ct).ConfigureAwait(false);
            }

            using (bitmap)
            {
                outputCreated = true;
                await WriteAsync(bitmap, outputPath, encoderId, EncoderOptions(format, quality), ct).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            if (outputCreated)
            {
                TryDelete(outputPath);
            }
            throw Map(ex, outputPath, EncodeStep, format);
        }
    }

    private static async Task WriteAsync(SoftwareBitmap bitmap, string path, Guid encoderId, BitmapPropertySet? options, CancellationToken ct)
    {
        await using var output = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None, BufferSize, FileOptions.Asynchronous);
        using var outputRas = output.AsRandomAccessStream();
        // A fresh encoder (not CreateForTranscodingAsync) copies no metadata from the source.
        var encoder = options is null
            ? await BitmapEncoder.CreateAsync(encoderId, outputRas).AsTask(ct).ConfigureAwait(false)
            : await BitmapEncoder.CreateAsync(encoderId, outputRas, options).AsTask(ct).ConfigureAwait(false);
        encoder.SetSoftwareBitmap(bitmap);
        await encoder.FlushAsync().AsTask(ct).ConfigureAwait(false);
    }

    private static Guid? EncoderIdFor(FormatId format)
    {
        if (format == FormatRegistry.Tiff)
        {
            return BitmapEncoder.TiffEncoderId;
        }
        if (format == FormatRegistry.Bmp)
        {
            return BitmapEncoder.BmpEncoderId;
        }
        if (format == FormatRegistry.Gif)
        {
            return BitmapEncoder.GifEncoderId;
        }
        if (format == FormatRegistry.Png)
        {
            return BitmapEncoder.PngEncoderId;
        }
        if (format == FormatRegistry.Jpg)
        {
            return BitmapEncoder.JpegEncoderId;
        }
        return null;
    }

    private static BitmapPropertySet? EncoderOptions(FormatId format, int quality)
    {
        if (format == FormatRegistry.Tiff)
        {
            return new BitmapPropertySet
            {
                ["TiffCompressionMethod"] = new BitmapTypedValue(TiffCompressionLzw, PropertyType.UInt8),
            };
        }
        if (format == FormatRegistry.Jpg)
        {
            return new BitmapPropertySet
            {
                ["ImageQuality"] = new BitmapTypedValue(Math.Clamp(quality, 1, 100) / 100f, PropertyType.Single),
            };
        }
        return null;
    }

    /// <summary>Coarse family from the first bytes, only to pick the decoder and to judge WIC's choice.</summary>
    private static FormatId? DetectFamily(Stream stream)
    {
        Span<byte> head = stackalloc byte[32];
        var read = stream.ReadAtLeast(head, head.Length, throwOnEndOfStream: false);
        head = head[..read];
        if (read >= 12 && head.Slice(4, 4).SequenceEqual("ftyp"u8))
        {
            var brand = head.Slice(8, 4);
            if (brand.SequenceEqual("avif"u8) || brand.SequenceEqual("avis"u8))
            {
                return FormatRegistry.Avif;
            }
            if (brand.SequenceEqual("crx "u8))
            {
                return FormatRegistry.Raw;
            }
            return FormatRegistry.Heic;
        }
        if (read >= 4 && (head[..4].SequenceEqual("II*\0"u8) || head[..4].SequenceEqual("MM\0*"u8)))
        {
            return FormatRegistry.Tiff; // may still be a TIFF-based RAW; judged by the decoder WIC picks
        }
        return FormatRegistry.Raw; // RAF, ORF, RW2 and friends have their own signatures
    }

    /// <summary>
    /// Rejects a decoder that does not fit the verified family. In particular a TIFF-based RAW file must not be
    /// read by the plain TIFF or JPEG decoder (that would convert only the embedded thumbnail).
    /// </summary>
    private static void EnsureExpectedDecoder(BitmapDecoder decoder, FormatId? family, string path)
    {
        var id = decoder.DecoderInformation?.CodecId ?? Guid.Empty;
        var builtIn = id == BitmapDecoder.TiffDecoderId || id == BitmapDecoder.JpegDecoderId || id == BitmapDecoder.PngDecoderId
                      || id == BitmapDecoder.GifDecoderId || id == BitmapDecoder.BmpDecoderId || id == BitmapDecoder.IcoDecoderId
                      || id == BitmapDecoder.JpegXRDecoderId;
        var isRawFile = HasRawExtension(path);
        if (family == FormatRegistry.Tiff && !isRawFile)
        {
            if (id != BitmapDecoder.TiffDecoderId)
            {
                throw new ConversionException(ConversionErrorCode.CorruptFile, path, DecodeStep, "not a TIFF image");
            }
            return;
        }
        if (family == FormatRegistry.Raw || isRawFile)
        {
            if (builtIn)
            {
                throw new ConversionException(ConversionErrorCode.MissingSystemCodec, path, DecodeStep, "raw image extension not available");
            }
            return;
        }
        if (builtIn)
        {
            // HEIC/AVIF must come from the HEIF/AV1 extension decoder, never from a built-in one.
            throw new ConversionException(ConversionErrorCode.CorruptFile, path, DecodeStep, "unexpected decoder");
        }
    }

    private static bool HasRawExtension(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is ".dng" or ".cr2" or ".cr3" or ".nef" or ".arw" or ".orf" or ".raf" or ".rw2";

    private static ConversionException Map(Exception ex, string path, string step, FormatId? format)
    {
        var needsExtension = format == FormatRegistry.Heic || format == FormatRegistry.Avif || format == FormatRegistry.Raw;
        return ex switch
        {
            ConversionException ce => ce,
            OperationCanceledException => ConversionException.From(ex, path, step),
            _ when ex.HResult is WincodecErrComponentNotFound or MfErrTopoCodecNotFound
                => new ConversionException(ConversionErrorCode.MissingSystemCodec, path, step, $"0x{ex.HResult:X8}", ex),
            _ when ex.HResult is WincodecErrUnknownImageFormat && needsExtension
                => new ConversionException(ConversionErrorCode.MissingSystemCodec, path, step, $"0x{ex.HResult:X8}", ex),
            _ when ex.HResult is WincodecErrBadHeader or WincodecErrUnknownImageFormat or WincodecErrBadImage
                => new ConversionException(ConversionErrorCode.CorruptFile, path, step, $"0x{ex.HResult:X8}", ex),
            _ => ConversionException.From(ex, path, step),
        };
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
