using Kvertis.Engine.Abstractions;
using Kvertis.Engine.Formats;

namespace Kvertis.Engine.Estimation;

/// <summary>
/// Relative size of the output for these settings compared with the reference settings, per media kind.
/// The reference is <see cref="ConversionSettings"/> as constructed by default: Quality 80, no dimension
/// change, no explicit bitrate and no frame rate. <see cref="Factor"/> is exactly 1.0 there, so a speed
/// profile learned before this model stays valid (ADR-019).
/// </summary>
public static class SizeModel
{
    /// <summary>Quality of the reference settings.</summary>
    internal const int ReferenceQuality = 80;

    /// <summary>Audio bitrate the reference settings stand for, in kbit/s.</summary>
    internal const int ReferenceKbps = 192;

    /// <summary>Frame rate the reference settings stand for.</summary>
    private const double ReferenceFrameRate = 30;

    /// <summary>Share of a video file that is video rather than audio; used to blend the two factors.</summary>
    private const double VideoShare = 0.92;

    /// <summary>Guard rails only: a factor outside these bounds would be a bug, not a setting.</summary>
    private const double MinFactor = 0.0001;
    private const double MaxFactor = 50.0;

    /// <summary>Factor &gt; 0 and finite; 1.0 at the reference settings.</summary>
    public static double Factor(InputInfo input, ConversionSettings settings, FormatRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(registry);

        var lossless = registry.Get(settings.Output)?.Lossless == true;
        var outputKind = registry.KindOf(settings.Output);

        var factor = input.Kind switch
        {
            MediaKind.Image => ImageFactor(input, settings, lossless),
            MediaKind.Audio => lossless ? 1.0 : BitrateFactor(settings),
            MediaKind.Video when outputKind == MediaKind.Audio => lossless ? 1.0 : BitrateFactor(settings),
            MediaKind.Video => VideoFactor(input, settings),
            _ => 1.0,
        };

        return double.IsFinite(factor) ? Math.Clamp(factor, MinFactor, MaxFactor) : 1.0;
    }

    private static double ImageFactor(InputInfo input, ConversionSettings settings, bool lossless)
    {
        var pixels = PixelFactor(LongestEdge(input), settings);
        return lossless ? pixels : pixels * (QualityCurve(settings.QualityClamped) / QualityCurve(ReferenceQuality));
    }

    private static double VideoFactor(InputInfo input, ConversionSettings settings)
    {
        var pixels = PixelFactor(input.Height, settings);
        // Same shape as FfmpegArguments.QualityVideoKbps: 0.02..0.12 bits per pixel and frame.
        var bitsPerPixel = (0.02 + (0.10 * settings.QualityClamped / 100.0))
                           / (0.02 + (0.10 * ReferenceQuality / 100.0));
        var frameRate = settings.GetAdvancedInt(ConversionSettings.AdvancedKeys.FrameRate) is { } fps and > 0
            ? Math.Min(fps / ReferenceFrameRate, 1.0)
            : 1.0;
        return (VideoShare * pixels * bitsPerPixel * frameRate) + ((1 - VideoShare) * BitrateFactor(settings));
    }

    private static double BitrateFactor(ConversionSettings settings)
    {
        var kbps = settings.GetAdvancedInt(ConversionSettings.AdvancedKeys.AudioBitrateKbps) is { } k and > 0
            ? k
            : ReferenceKbps;
        return kbps / (double)ReferenceKbps;
    }

    /// <summary>Area kept after maxDimension, relative to the source. 1.0 when the source size is unknown.</summary>
    private static double PixelFactor(int? source, ConversionSettings settings)
    {
        var max = settings.GetAdvancedInt(ConversionSettings.AdvancedKeys.MaxDimension) ?? 0;
        if (max <= 0 || source is not { } s || s <= 0 || max >= s)
        {
            return 1.0;
        }
        var scale = max / (double)s;
        return scale * scale;
    }

    /// <summary>
    /// Bytes per pixel of a lossy encoder over the quality slider, from the prototype:
    /// 0.06 + 0.45 × ((q − 30) / 70)^2.2, clamped below 30 where encoders stop shrinking.
    /// </summary>
    private static double QualityCurve(int quality)
    {
        var normalized = Math.Clamp((quality - 30) / 70.0, 0, 1);
        return 0.06 + (0.45 * Math.Pow(normalized, 2.2));
    }

    private static int? LongestEdge(InputInfo input) =>
        input is { Width: { } w, Height: { } h } && w > 0 && h > 0 ? Math.Max(w, h) : null;
}
