using System.Globalization;
using Kvertis.Engine.Abstractions;
using Kvertis.Engine.Conversion.Video;
using Kvertis.Engine.Formats;

namespace Kvertis.Engine.Tuning;

/// <summary>
/// Maps a <see cref="QualityGrade"/> to converter settings and back, per media kind. Pure, deterministic,
/// no file access, no UI (ADR-019).
/// </summary>
/// <remarks>
/// The forward direction is a ladder of steps per aspect plus one linear term for <c>Quality</c>. Steps lose
/// information, so the round trip <c>GradeOf(Apply(g))</c> returns the representative grade of the step the
/// grade fell into, not <c>g</c> itself. It is monotone and stays within a few points of <c>g</c>; the exact
/// bound is asserted by the tests.
/// </remarks>
public static class GradeMapper
{
    /// <summary>Sentinel for "keep the source value" inside the ladders; written as maxDimension 0 / no frameRate.</summary>
    private const int Keep = int.MaxValue;

    /// <summary>Lowest grade of one rung and the parameter it produces (0 = keep the source value).</summary>
    private sealed record Rung(int MinGrade, int Value);

    /// <summary>Longest edge in pixels for images.</summary>
    private static readonly Rung[] ImageDimensionLadder =
    [
        new(0, 640), new(10, 800), new(25, 1280), new(40, 1920), new(55, 2560), new(70, 3840), new(85, 0),
    ];

    /// <summary>Height in pixels for video.</summary>
    private static readonly Rung[] VideoHeightLadder =
    [
        new(0, 360), new(10, 480), new(25, 540), new(40, 720), new(55, 1080), new(70, 1440), new(85, 0),
    ];

    /// <summary>Frame rate. Only ever reduces; the source rate is unknown to the engine before probing.</summary>
    private static readonly Rung[] FrameRateLadder =
    [
        new(0, 15), new(20, 20), new(35, 24), new(50, 25), new(65, 30), new(80, 0),
    ];

    /// <summary>Audio bitrate in kbit/s.</summary>
    private static readonly Rung[] AudioBitrateLadder =
    [
        new(0, 64), new(20, 96), new(40, 128), new(55, 160), new(70, 192), new(85, 256), new(95, 320),
    ];

    /// <summary>Audio bitrate ceiling for the audio track of a video.</summary>
    private const int VideoAudioCeilingKbps = 192;

    /// <summary>False for documents, 3D models and outputs whose parameters are all fixed (lossless audio).</summary>
    public static bool SupportsGrade(MediaKind kind, FormatId output, FormatRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        if (kind is not (MediaKind.Image or MediaKind.Audio or MediaKind.Video))
        {
            return false;
        }
        // A probe-free stand-in is enough: adjustability depends on the formats, not on the concrete file.
        var probe = new InputInfo("", output, kind, 0, null, null, null, null, []);
        return Aspects(new ConversionSettings(output), probe, registry).Any(a => a.Adjustable);
    }

    /// <summary>
    /// Returns <paramref name="baseline"/> with Quality, maxDimension, audioBitrateKbps and frameRate set for
    /// the grade. Keeps Output, Metadata, TargetSizeBytes, Preset and every advanced key it does not own.
    /// Lossless and palette outputs keep Quality 100; kinds without a grade are returned unchanged.
    /// </summary>
    public static ConversionSettings Apply(QualityGrade grade, ConversionSettings baseline, InputInfo input, FormatRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(registry);

        var g = grade.Clamped;
        var shape = Shape.Of(baseline.Output, input, registry);
        if (!shape.HasAnyAspect)
        {
            return baseline;
        }

        var advanced = CopyAdvanced(baseline);
        var quality = baseline.Quality;

        if (shape.HasSharpness)
        {
            SetDimension(advanced, Value(shape.DimensionLadder, shape.DimensionMap, g));
        }
        if (shape.HasDetail)
        {
            quality = QualityForGrade(g);
        }
        else if (shape.DetailFixed)
        {
            quality = 100;
        }
        if (shape.HasMotion)
        {
            SetFrameRate(advanced, Value(FrameRateLadder, shape.FrameRateMap, g));
        }
        if (shape.HasSound)
        {
            advanced[ConversionSettings.AdvancedKeys.AudioBitrateKbps] =
                Value(AudioBitrateLadder, shape.AudioMap, g).ToString(CultureInfo.InvariantCulture);
        }

        return baseline with { Quality = quality, Advanced = advanced };
    }

    /// <summary>Levels of all aspects that exist for this kind and output, in display order.</summary>
    public static IReadOnlyList<AspectLevel> Aspects(ConversionSettings settings, InputInfo input, FormatRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(registry);

        var shape = Shape.Of(settings.Output, input, registry);
        if (!shape.HasAnyAspect && !shape.DetailFixed && !shape.SoundFixed && !shape.VideoToAudio)
        {
            return [];
        }

        var levels = new List<AspectLevel>(4);

        if (shape.HasSharpness || shape.VideoToAudio)
        {
            var effective = EffectiveDimension(settings, shape);
            var detail = new AspectDetail(
                Pixels: effective == Keep ? shape.SourceDimension : effective,
                SourcePixels: shape.SourceDimension);
            levels.Add(shape.VideoToAudio
                ? new AspectLevel(TuningAspect.Sharpness, 0, false, detail)
                : new AspectLevel(
                    TuningAspect.Sharpness,
                    GradeFor(shape.DimensionLadder, shape.DimensionMap, effective),
                    Bands(shape.DimensionLadder, shape.DimensionMap).Count > 1,
                    detail));
        }

        if (shape.HasDetail || shape.DetailFixed || shape.VideoToAudio)
        {
            var quality = settings.QualityClamped;
            var detail = new AspectDetail(Quality: quality, Lossless: shape.Lossless);
            levels.Add(shape.VideoToAudio
                ? new AspectLevel(TuningAspect.Detail, 0, false, detail)
                : shape.HasDetail
                    ? new AspectLevel(TuningAspect.Detail, GradeForQuality(quality), true, detail)
                    : new AspectLevel(TuningAspect.Detail, 100, false, detail with { Quality = 100 }));
        }

        if (shape.HasMotion || shape.VideoToAudio)
        {
            var frameRate = EffectiveFrameRate(settings, shape);
            var detail = new AspectDetail(FrameRate: frameRate == Keep ? null : frameRate);
            levels.Add(shape.VideoToAudio
                ? new AspectLevel(TuningAspect.Motion, 0, false, detail)
                : new AspectLevel(
                    TuningAspect.Motion,
                    GradeFor(FrameRateLadder, shape.FrameRateMap, frameRate),
                    true,
                    detail));
        }

        if (shape.HasSound || shape.SoundFixed)
        {
            var kbps = EffectiveKbps(settings, shape);
            levels.Add(shape.SoundFixed
                ? new AspectLevel(TuningAspect.Sound, 100, false, new AspectDetail(Lossless: true))
                : new AspectLevel(
                    TuningAspect.Sound,
                    GradeFor(AudioBitrateLadder, shape.AudioMap, kbps),
                    Bands(AudioBitrateLadder, shape.AudioMap).Count > 1,
                    new AspectDetail(Kbps: kbps)));
        }

        return levels;
    }

    /// <summary>
    /// Changes one aspect (zone bar) to <paramref name="value"/> on the 0..100 scale and leaves the other
    /// parameters as they are. Aspects that do not exist or are not adjustable return the settings unchanged.
    /// </summary>
    public static ConversionSettings WithAspect(ConversionSettings settings, TuningAspect aspect, int value, InputInfo input, FormatRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(registry);

        var level = Aspects(settings, input, registry).FirstOrDefault(a => a.Aspect == aspect);
        if (level is null || !level.Adjustable)
        {
            return settings;
        }

        var shape = Shape.Of(settings.Output, input, registry);
        var g = Math.Clamp(value, 0, 100);
        var advanced = CopyAdvanced(settings);

        switch (aspect)
        {
            case TuningAspect.Sharpness:
                SetDimension(advanced, Value(shape.DimensionLadder, shape.DimensionMap, g));
                break;
            case TuningAspect.Detail:
                return settings with { Quality = QualityForGrade(g) };
            case TuningAspect.Motion:
                SetFrameRate(advanced, Value(FrameRateLadder, shape.FrameRateMap, g));
                break;
            case TuningAspect.Sound:
                advanced[ConversionSettings.AdvancedKeys.AudioBitrateKbps] =
                    Value(AudioBitrateLadder, shape.AudioMap, g).ToString(CultureInfo.InvariantCulture);
                break;
            default:
                return settings;
        }

        return settings with { Advanced = advanced };
    }

    /// <summary>
    /// The grade that best describes arbitrary settings (history entries, zone edits):
    /// 0.6 × mean + 0.4 × minimum over the adjustable aspects. Without an adjustable aspect the default applies.
    /// </summary>
    public static QualityGrade GradeOf(ConversionSettings settings, InputInfo input, FormatRegistry registry)
    {
        var adjustable = Aspects(settings, input, registry).Where(a => a.Adjustable).Select(a => a.Value).ToList();
        if (adjustable.Count == 0)
        {
            return QualityGrade.Default;
        }
        var value = (0.6 * adjustable.Average()) + (0.4 * adjustable.Min());
        return new QualityGrade(Math.Clamp((int)Math.Round(value, MidpointRounding.AwayFromZero), 0, 100));
    }

    /// <summary>Quality for a grade: linear from 30 at grade 0 to 100 at grade 100 (ADR-019 keeps this form).</summary>
    internal static int QualityForGrade(int grade) =>
        (int)Math.Round(30 + (0.7 * Math.Clamp(grade, 0, 100)), MidpointRounding.AwayFromZero);

    internal static int GradeForQuality(int quality) =>
        Math.Clamp((int)Math.Round((Math.Clamp(quality, 0, 100) - 30) / 0.7, MidpointRounding.AwayFromZero), 0, 100);

    private static Dictionary<string, string> CopyAdvanced(ConversionSettings settings) =>
        settings.Advanced is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(settings.Advanced, StringComparer.Ordinal);

    private static void SetDimension(Dictionary<string, string> advanced, int value) =>
        advanced[ConversionSettings.AdvancedKeys.MaxDimension] =
            value == Keep ? "0" : value.ToString(CultureInfo.InvariantCulture);

    private static void SetFrameRate(Dictionary<string, string> advanced, int value)
    {
        if (value == Keep)
        {
            advanced.Remove(ConversionSettings.AdvancedKeys.FrameRate);
        }
        else
        {
            advanced[ConversionSettings.AdvancedKeys.FrameRate] = value.ToString(CultureInfo.InvariantCulture);
        }
    }

    private static int EffectiveDimension(ConversionSettings settings, Shape shape)
    {
        var raw = settings.GetAdvancedInt(ConversionSettings.AdvancedKeys.MaxDimension) ?? 0;
        return shape.DimensionMap(raw);
    }

    private static int EffectiveFrameRate(ConversionSettings settings, Shape shape)
    {
        var raw = settings.GetAdvancedInt(ConversionSettings.AdvancedKeys.FrameRate) ?? 0;
        return shape.FrameRateMap(raw);
    }

    private static int EffectiveKbps(ConversionSettings settings, Shape shape)
    {
        var raw = settings.GetAdvancedInt(ConversionSettings.AdvancedKeys.AudioBitrateKbps) ?? 0;
        return shape.AudioMap(raw <= 0 ? ReferenceAudioKbps : raw);
    }

    /// <summary>Audio bitrate that the default settings stand for; also the reference of <see cref="Estimation.SizeModel"/>.</summary>
    internal const int ReferenceAudioKbps = 192;

    /// <summary>Grade bands after the format and the source have been applied to the ladder; equal neighbours merged.</summary>
    private static List<(int Lo, int Hi, int Value)> Bands(Rung[] ladder, Func<int, int> map)
    {
        var bands = new List<(int Lo, int Hi, int Value)>(ladder.Length);
        for (var i = 0; i < ladder.Length; i++)
        {
            var lo = ladder[i].MinGrade;
            var hi = i + 1 < ladder.Length ? ladder[i + 1].MinGrade - 1 : 100;
            var value = map(ladder[i].Value);
            if (bands.Count > 0 && bands[^1].Value == value)
            {
                bands[^1] = (bands[^1].Lo, hi, value);
            }
            else
            {
                bands.Add((lo, hi, value));
            }
        }
        return bands;
    }

    private static int Value(Rung[] ladder, Func<int, int> map, int grade)
    {
        var bands = Bands(ladder, map);
        foreach (var band in bands)
        {
            if (grade >= band.Lo && grade <= band.Hi)
            {
                return band.Value;
            }
        }
        return bands[^1].Value;
    }

    /// <summary>
    /// Inverse of <see cref="Value"/>: the representative grade of the band that produces this parameter,
    /// which is the middle of the band. A band that keeps the source untouched counts as 100, because
    /// "nothing was taken away" is the best this aspect can be. Values no band produces (set by hand or by
    /// a preset) pick the numerically closest band.
    /// </summary>
    private static int GradeFor(Rung[] ladder, Func<int, int> map, int value)
    {
        var bands = Bands(ladder, map);
        var best = bands[0];
        var bestDistance = long.MaxValue;
        foreach (var band in bands)
        {
            var distance = Math.Abs((long)band.Value - value);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = band;
            }
        }
        return best is { Value: Keep, Hi: 100 } ? 100 : (best.Lo + best.Hi) / 2;
    }

    private static int NearestStep(int value, IReadOnlyList<int> steps)
    {
        var best = steps[0];
        foreach (var step in steps)
        {
            if (Math.Abs(step - value) < Math.Abs(best - value))
            {
                best = step;
            }
        }
        return best;
    }

    /// <summary>Which aspects exist for one (kind, output, source) triple and how the ladders are clipped.</summary>
    private sealed class Shape
    {
        public required Rung[] DimensionLadder { get; init; }
        public required Func<int, int> DimensionMap { get; init; }
        public required Func<int, int> FrameRateMap { get; init; }
        public required Func<int, int> AudioMap { get; init; }
        public int? SourceDimension { get; init; }
        public bool HasSharpness { get; init; }
        public bool HasDetail { get; init; }
        public bool DetailFixed { get; init; }
        public bool HasMotion { get; init; }
        public bool HasSound { get; init; }
        public bool SoundFixed { get; init; }
        public bool VideoToAudio { get; init; }
        public bool Lossless { get; init; }

        public bool HasAnyAspect => HasSharpness || HasDetail || HasMotion || HasSound;

        public static Shape Of(FormatId output, InputInfo input, FormatRegistry registry)
        {
            var descriptor = registry.Get(output);
            var lossless = descriptor?.Lossless == true;
            var palette = output == FormatRegistry.Gif;
            var outputKind = registry.KindOf(output);
            var aac = output == FormatRegistry.M4a || output == FormatRegistry.Mp4;

            Func<int, int> audioMap = lossless
                ? _ => Keep
                : raw => aac ? NearestStep(raw, TranscodePlan.AacBitrateSteps) : raw;

            switch (input.Kind)
            {
                case MediaKind.Image:
                {
                    var source = LongestEdge(input);
                    return new Shape
                    {
                        DimensionLadder = ImageDimensionLadder,
                        DimensionMap = ClampToSource(source),
                        FrameRateMap = _ => Keep,
                        AudioMap = _ => Keep,
                        SourceDimension = source,
                        HasSharpness = true,
                        HasDetail = !lossless && !palette,
                        DetailFixed = lossless || palette,
                        Lossless = lossless,
                    };
                }

                case MediaKind.Audio:
                    return new Shape
                    {
                        DimensionLadder = ImageDimensionLadder,
                        DimensionMap = _ => Keep,
                        FrameRateMap = _ => Keep,
                        AudioMap = audioMap,
                        HasSound = !lossless,
                        SoundFixed = lossless,
                        Lossless = lossless,
                    };

                case MediaKind.Video when outputKind == MediaKind.Audio:
                    return new Shape
                    {
                        DimensionLadder = VideoHeightLadder,
                        DimensionMap = _ => Keep,
                        FrameRateMap = _ => Keep,
                        AudioMap = audioMap,
                        SourceDimension = input.Height,
                        HasSound = !lossless,
                        SoundFixed = lossless,
                        VideoToAudio = true,
                        Lossless = lossless,
                    };

                case MediaKind.Video:
                {
                    var source = input.Height;
                    return new Shape
                    {
                        DimensionLadder = VideoHeightLadder,
                        DimensionMap = ClampToSource(source),
                        FrameRateMap = raw => raw <= 0 ? Keep : raw,
                        AudioMap = raw => Math.Min(audioMap(raw), VideoAudioCeilingKbps),
                        SourceDimension = source,
                        HasSharpness = true,
                        HasDetail = true,
                        HasMotion = true,
                        HasSound = true,
                    };
                }

                default:
                    return new Shape
                    {
                        DimensionLadder = ImageDimensionLadder,
                        DimensionMap = _ => Keep,
                        FrameRateMap = _ => Keep,
                        AudioMap = _ => Keep,
                    };
            }
        }

        private static Func<int, int> ClampToSource(int? source) =>
            raw => raw <= 0 || (source is { } s && s > 0 && raw >= s) ? Keep : raw;

        private static int? LongestEdge(InputInfo input) =>
            input is { Width: { } w, Height: { } h } && w > 0 && h > 0 ? Math.Max(w, h) : null;
    }
}
