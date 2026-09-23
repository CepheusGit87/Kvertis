using ImageMagick;
using Kvertis.Engine.Abstractions;
using Kvertis.Engine.Conversion.Images;
using Kvertis.Engine.Formats;
using Kvertis.Engine.Validation;

namespace Kvertis.Engine.Probing;

/// <summary>
/// Reads image headers with Magick.NET (ping, no pixel decoding) to fill in width and height and to
/// flag animated sources. HEIC is never handed to Magick.NET (ADR-006); its dimensions stay unknown.
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
        if (info.Format == FormatRegistry.Heic || MagickSupport.ReadFormatFor(info.Format, info.Path) is null)
        {
            // HEIC: only the system decoder may touch it. Unknown coder: nothing we can add safely.
            return info;
        }

        try
        {
            var (width, height, frames) = await Task.Run(() => Ping(info, ct), ct)
                .WaitAsync(_timeout, ct)
                .ConfigureAwait(false);

            var result = info with { Width = width, Height = height };
            if (frames > 1 && (info.Format == FormatRegistry.Gif || info.Format == FormatRegistry.WebP))
            {
                result = result.WithWarning(InputWarning.AnimationDropped);
            }
            return result;
        }
        catch (Exception ex)
        {
            throw MagickSupport.Translate(ex, info.Path, "probe");
        }
    }

    private static (int Width, int Height, int Frames) Ping(InputInfo info, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var settings = MagickSupport.ReadSettings(info.Format, info.Path, firstFrameOnly: false);
        var frames = MagickImageInfo.ReadCollection(info.Path, settings).ToList();
        if (frames.Count == 0)
        {
            throw new ConversionException(ConversionErrorCode.CorruptFile, info.Path, "probe", "no frames");
        }
        var first = frames[0];
        return (checked((int)first.Width), checked((int)first.Height), frames.Count);
    }
}
