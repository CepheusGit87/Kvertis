using System.Runtime.Versioning;
using Kvertis.Engine.Abstractions;
using Kvertis.Engine.Windows.Codecs;
using Windows.Graphics.Imaging;

namespace Kvertis.Engine.Windows.Imaging;

/// <summary>
/// Decodes HEIC/HEIF through the Windows Imaging Component and the system HEIF/HEVC extensions (ADR-006).
/// Writes a PNG without any metadata; EXIF orientation is applied to the pixels.
/// </summary>
[SupportedOSPlatform("windows10.0.17763.0")]
public sealed class WicHeicDecoder : IHeicDecoder
{
    private const string Step = "heic-decode";
    private const int BufferSize = 81920;

    // WIC HRESULTs (wincodec.h).
    private const int WincodecErrComponentNotFound = unchecked((int)0x88982F50);
    private const int WincodecErrUnknownImageFormat = unchecked((int)0x88982F07);
    private const int WincodecErrBadImage = unchecked((int)0x88982F60);
    private const int WincodecErrBadHeader = unchecked((int)0x88982F61);

    // MF_E_TOPO_CODEC_NOT_FOUND: expected when the HEIF container is readable but the HEVC decoder is missing (unverified).
    private const int MfErrTopoCodecNotFound = unchecked((int)0xC00D5212);

    /// <summary>True when the HEIF image decoder is registered with WIC.</summary>
    public bool IsAvailable => MediaFoundationCapabilities.HasHeifDecoder();

    public async Task DecodeToPngAsync(string heicPath, string outputPngPath, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(heicPath);
        ArgumentException.ThrowIfNullOrEmpty(outputPngPath);

        if (!IsAvailable)
        {
            throw new ConversionException(ConversionErrorCode.MissingSystemCodec, heicPath, Step, "HEIF image decoder not registered");
        }

        var outputCreated = false;
        try
        {
            ct.ThrowIfCancellationRequested();

            SoftwareBitmap bitmap;
            await using (var input = new FileStream(heicPath, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, FileOptions.Asynchronous))
            {
                using var inputRas = input.AsRandomAccessStream();
                var decoder = await BitmapDecoder.CreateAsync(BitmapDecoder.HeifDecoderId, inputRas).AsTask(ct).ConfigureAwait(false);

                // Straight alpha: PNG stores unpremultiplied colour, premultiplied input would darken soft edges.
                bitmap = await decoder.GetSoftwareBitmapAsync(
                    BitmapPixelFormat.Bgra8,
                    BitmapAlphaMode.Straight,
                    new BitmapTransform(),
                    ExifOrientationMode.RespectExifOrientation,
                    ColorManagementMode.ColorManageToSRgb).AsTask(ct).ConfigureAwait(false);
            }

            using (bitmap)
            {
                ct.ThrowIfCancellationRequested();
                await using var output = new FileStream(outputPngPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None, BufferSize, FileOptions.Asynchronous);
                outputCreated = true;
                using var outputRas = output.AsRandomAccessStream();

                // A fresh encoder (not CreateForTranscodingAsync) copies no metadata from the source.
                var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, outputRas).AsTask(ct).ConfigureAwait(false);
                encoder.SetSoftwareBitmap(bitmap);
                await encoder.FlushAsync().AsTask(ct).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            if (outputCreated)
            {
                TryDelete(outputPngPath);
            }

            throw Map(ex, heicPath);
        }
    }

    private static ConversionException Map(Exception ex, string path) => ex switch
    {
        ConversionException ce => ce,
        OperationCanceledException => ConversionException.From(ex, path, Step),
        _ when ex.HResult is WincodecErrComponentNotFound or MfErrTopoCodecNotFound
            => new ConversionException(ConversionErrorCode.MissingSystemCodec, path, Step, $"0x{ex.HResult:X8}", ex),
        _ when ex.HResult is WincodecErrBadHeader or WincodecErrUnknownImageFormat or WincodecErrBadImage
            => new ConversionException(ConversionErrorCode.CorruptFile, path, Step, $"0x{ex.HResult:X8}", ex),
        _ => ConversionException.From(ex, path, Step),
    };

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
