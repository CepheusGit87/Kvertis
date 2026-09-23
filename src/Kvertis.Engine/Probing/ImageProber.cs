using Kvertis.Engine.Abstractions;
using Kvertis.Engine.Conversion.Images;
using Kvertis.Engine.Formats;
using Kvertis.Engine.Validation;

namespace Kvertis.Engine.Probing;

/// <summary>
/// Reads image headers with SkiaSharp (<c>SKCodec</c>, no pixel decoding) to fill in width and height and
/// to flag animated sources. HEIC, AVIF, TIFF and non-DNG RAW are only ever decoded by the system codec;
/// decoding them fully at probe time would be too slow, so their dimensions stay unknown.
/// Whether transparency gets lost depends on the output, so that warning is decided by the converter/UI.
/// </summary>
public sealed class ImageProber : IMediaProber
{
    private readonly TimeSpan _timeout;

    public ImageProber()
        : this(InputLimits.AnalysisTimeoutFor(MediaKind.Image))
    {
    }

    /// <summary>For tests: custom analysis timeout.</summary>
    internal ImageProber(TimeSpan timeout)
    {
        _timeout = timeout;
    }

    public bool Supports(MediaKind kind) => kind == MediaKind.Image;

    public async Task<InputInfo> ProbeAsync(InputInfo info, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(info);
        if (!SkiaImaging.IsSkiaReadable(info.Format))
        {
            // System-codec formats (and anything unknown): nothing we can add cheaply and safely.
            return info;
        }

        try
        {
            var header = await Task.Run(() => ReadHeader(info, ct), ct)
                .WaitAsync(_timeout, ct)
                .ConfigureAwait(false);
            if (header is not var (width, height, frames))
            {
                return info; // RAW that is not DNG: the system codec decides later.
            }

            var result = info with { Width = width, Height = height };
            if (frames > 1 && (info.Format == FormatRegistry.Gif || info.Format == FormatRegistry.WebP))
            {
                result = result.WithWarning(InputWarning.AnimationDropped);
            }
            return result;
        }
        catch (Exception ex)
        {
            throw ConversionException.From(ex, info.Path, "probe");
        }
    }

    private static (int Width, int Height, int Frames)? ReadHeader(InputInfo info, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var stream = new FileStream(info.Path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var codec = SkiaImaging.OpenCodec(stream, info.Format);
        if (codec is null)
        {
            if (info.Format == FormatRegistry.Raw)
            {
                return null;
            }
            // The magic bytes said this is an image Skia reads, but Skia cannot open it.
            throw new ConversionException(ConversionErrorCode.CorruptFile, info.Path, "probe", $"not a readable {info.Format} image");
        }
        var size = codec.Info;
        if (size.Width <= 0 || size.Height <= 0)
        {
            throw new ConversionException(ConversionErrorCode.CorruptFile, info.Path, "probe", "empty image");
        }
        return (size.Width, size.Height, Math.Max(1, codec.FrameCount));
    }
}
