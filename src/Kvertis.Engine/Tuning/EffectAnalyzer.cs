using Kvertis.Engine.Abstractions;
using Kvertis.Engine.Formats;

namespace Kvertis.Engine.Tuning;

public enum EffectSeverity
{
    Fine = 0,
    Notice,
    Warning,
}

/// <summary>
/// What the chosen settings do to the output, as codes. The app maps a code to a .resw key and fills the
/// numbers from <see cref="AspectDetail"/>; the engine never produces text (ADR-004).
/// </summary>
public enum EffectCode
{
    ResolutionReduced,
    FullResolutionKept,
    VisibleArtifacts,
    SlightArtifacts,
    NearOriginalQuality,
    LosslessOutput,
    PaletteLimited,
    TransparencyLost,
    AnimationDropped,
    BitrateSpeechOnly,
    BitrateSlightlyDull,
    BitrateLikeOriginal,
    FrameRateReduced,
    MotionBlocky,
    VideoTrackDropped,
    MetadataStripped,
    MetadataKept,
    MetadataNotStrippable,
    FormattingLost,
    StructureKept,
    LayoutSimplified,
    ColorsAndMaterialsDropped,
    UnitUnknown,
    TargetSizeMayBeUnreachable,
}

public sealed record ConversionEffect(EffectCode Code, EffectSeverity Severity, AspectDetail Detail)
{
    public ConversionEffect(EffectCode code, EffectSeverity severity)
        : this(code, severity, AspectDetail.None)
    {
    }
}

/// <summary>Derives "Was sich ändert" from input and settings. Pure and deterministic.</summary>
public static class EffectAnalyzer
{
    /// <summary>Effects of these settings on this input, worst first and stable within a severity.</summary>
    public static IReadOnlyList<ConversionEffect> Analyze(InputInfo input, ConversionSettings settings, FormatRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(registry);

        var effects = new List<ConversionEffect>();
        var lossless = registry.Get(settings.Output)?.Lossless == true;
        var outputKind = registry.KindOf(settings.Output);
        var levels = GradeMapper.Aspects(settings, input, registry);

        switch (input.Kind)
        {
            case MediaKind.Image:
                AddPictureEffects(effects, input, settings, levels, lossless);
                break;
            case MediaKind.Audio:
                AddSoundEffects(effects, levels, lossless);
                break;
            case MediaKind.Video when outputKind == MediaKind.Audio:
                effects.Add(new ConversionEffect(EffectCode.VideoTrackDropped, EffectSeverity.Warning));
                AddSoundEffects(effects, levels, lossless);
                break;
            case MediaKind.Video:
                AddPictureEffects(effects, input, settings, levels, lossless);
                AddMotionEffects(effects, levels);
                AddSoundEffects(effects, levels, lossless);
                break;
            case MediaKind.Document:
                AddDocumentEffects(effects, settings);
                break;
            case MediaKind.Model3D:
                AddModelEffects(effects, settings);
                break;
            default:
                break;
        }

        AddMetadataEffect(effects, input, settings);

        return [.. effects
            .Select((effect, index) => (effect, index))
            .OrderByDescending(x => x.effect.Severity)
            .ThenBy(x => x.index)
            .Select(x => x.effect)];
    }

    public static EffectSeverity Worst(IReadOnlyList<ConversionEffect> effects)
    {
        ArgumentNullException.ThrowIfNull(effects);
        var worst = EffectSeverity.Fine;
        foreach (var effect in effects)
        {
            if (effect.Severity > worst)
            {
                worst = effect.Severity;
            }
        }
        return worst;
    }

    private static void AddPictureEffects(
        List<ConversionEffect> effects,
        InputInfo input,
        ConversionSettings settings,
        IReadOnlyList<AspectLevel> levels,
        bool lossless)
    {
        if (Level(levels, TuningAspect.Sharpness) is { } sharpness)
        {
            var detail = sharpness.Detail;
            var reduced = detail is { Pixels: { } p, SourcePixels: { } s } && p < s;
            effects.Add(reduced
                ? new ConversionEffect(EffectCode.ResolutionReduced, EffectSeverity.Notice, detail)
                : new ConversionEffect(EffectCode.FullResolutionKept, EffectSeverity.Fine, detail));
        }

        if (settings.Output == FormatRegistry.Gif)
        {
            effects.Add(new ConversionEffect(EffectCode.PaletteLimited, EffectSeverity.Notice));
        }
        else if (lossless)
        {
            effects.Add(new ConversionEffect(EffectCode.LosslessOutput, EffectSeverity.Fine, new AspectDetail(Lossless: true)));
        }
        else
        {
            var quality = settings.QualityClamped;
            var detail = new AspectDetail(Quality: quality);
            effects.Add(quality switch
            {
                < 55 => new ConversionEffect(EffectCode.VisibleArtifacts, EffectSeverity.Warning, detail),
                < 75 => new ConversionEffect(EffectCode.SlightArtifacts, EffectSeverity.Notice, detail),
                _ => new ConversionEffect(EffectCode.NearOriginalQuality, EffectSeverity.Fine, detail),
            });
            if (input.Kind == MediaKind.Video && quality < 55)
            {
                effects.Add(new ConversionEffect(EffectCode.MotionBlocky, EffectSeverity.Warning, detail));
            }
        }

        if (input.HasWarning(InputWarning.TransparencyLost))
        {
            effects.Add(new ConversionEffect(EffectCode.TransparencyLost, EffectSeverity.Notice));
        }
        if (input.HasWarning(InputWarning.AnimationDropped))
        {
            effects.Add(new ConversionEffect(EffectCode.AnimationDropped, EffectSeverity.Notice));
        }
    }

    private static void AddMotionEffects(List<ConversionEffect> effects, IReadOnlyList<AspectLevel> levels)
    {
        if (Level(levels, TuningAspect.Motion)?.Detail is { FrameRate: { } fps })
        {
            effects.Add(new ConversionEffect(EffectCode.FrameRateReduced, EffectSeverity.Notice, new AspectDetail(FrameRate: fps)));
        }
    }

    private static void AddSoundEffects(List<ConversionEffect> effects, IReadOnlyList<AspectLevel> levels, bool lossless)
    {
        if (lossless)
        {
            effects.Add(new ConversionEffect(EffectCode.LosslessOutput, EffectSeverity.Fine, new AspectDetail(Lossless: true)));
            return;
        }
        if (Level(levels, TuningAspect.Sound)?.Detail is not { Kbps: { } kbps })
        {
            return;
        }
        var detail = new AspectDetail(Kbps: kbps);
        effects.Add(kbps switch
        {
            <= 96 => new ConversionEffect(EffectCode.BitrateSpeechOnly, EffectSeverity.Warning, detail),
            <= 128 => new ConversionEffect(EffectCode.BitrateSlightlyDull, EffectSeverity.Notice, detail),
            _ => new ConversionEffect(EffectCode.BitrateLikeOriginal, EffectSeverity.Fine, detail),
        });
    }

    private static void AddDocumentEffects(List<ConversionEffect> effects, ConversionSettings settings)
    {
        if (settings.Output == FormatRegistry.Txt || settings.Output == FormatRegistry.Csv)
        {
            effects.Add(new ConversionEffect(EffectCode.FormattingLost, EffectSeverity.Warning));
        }
        else if (settings.Output == FormatRegistry.Markdown)
        {
            effects.Add(new ConversionEffect(EffectCode.StructureKept, EffectSeverity.Fine));
        }
        else if (settings.Output == FormatRegistry.Html)
        {
            effects.Add(new ConversionEffect(EffectCode.LayoutSimplified, EffectSeverity.Notice));
        }
    }

    private static void AddModelEffects(List<ConversionEffect> effects, ConversionSettings settings)
    {
        // Own parsers keep geometry only (ADR-016); colors and materials never survive.
        effects.Add(new ConversionEffect(EffectCode.ColorsAndMaterialsDropped, EffectSeverity.Notice));
        if (settings.Output == FormatRegistry.Stl)
        {
            effects.Add(new ConversionEffect(EffectCode.UnitUnknown, EffectSeverity.Notice));
        }
    }

    private static void AddMetadataEffect(List<ConversionEffect> effects, InputInfo input, ConversionSettings settings)
    {
        if (settings.Metadata == MetadataPolicy.Keep)
        {
            effects.Add(new ConversionEffect(EffectCode.MetadataKept, EffectSeverity.Fine));
        }
        else if (input.HasWarning(InputWarning.MetadataNotStrippable))
        {
            effects.Add(new ConversionEffect(EffectCode.MetadataNotStrippable, EffectSeverity.Notice));
        }
        else
        {
            effects.Add(new ConversionEffect(EffectCode.MetadataStripped, EffectSeverity.Fine));
        }
    }

    private static AspectLevel? Level(IReadOnlyList<AspectLevel> levels, TuningAspect aspect)
    {
        foreach (var level in levels)
        {
            if (level.Aspect == aspect && level.Adjustable)
            {
                return level;
            }
        }
        return null;
    }
}
