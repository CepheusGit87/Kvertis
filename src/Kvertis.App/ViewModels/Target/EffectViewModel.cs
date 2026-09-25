using Kvertis.App.Services;
using Kvertis.Engine.Tuning;

namespace Kvertis.App.ViewModels.Target;

/// <summary>
/// One line of "Was sich ändert". The engine delivers a code and numbers (ADR-004); the text and the symbol
/// are chosen here.
/// </summary>
public sealed class EffectViewModel
{
    private EffectViewModel(EffectSeverity severity, string glyph, string text)
    {
        Severity = severity;
        Glyph = glyph;
        Text = text;
    }

    public EffectSeverity Severity { get; }

    /// <summary>Segoe Fluent Icons: check mark, information, warning. Never colour alone (docs/06-design.md).</summary>
    public string Glyph { get; }

    public string Text { get; }

    /// <summary>Brush key of the symbol, mint / muted / amber.</summary>
    public string BrushKey => Severity switch
    {
        EffectSeverity.Warning => "KvVideoBrush",
        EffectSeverity.Notice => "KvMutedBrush",
        _ => "KvMintBrush",
    };

    public static EffectViewModel Create(ILocalizer loc, ConversionEffect effect)
    {
        ArgumentNullException.ThrowIfNull(loc);
        ArgumentNullException.ThrowIfNull(effect);

        var key = "Effect_" + effect.Code.ToString() + "_Text";
        var detail = effect.Detail;
        var text = effect.Code switch
        {
            EffectCode.ResolutionReduced => loc.Format(key, detail.Pixels ?? 0, detail.SourcePixels ?? 0),
            EffectCode.FrameRateReduced => loc.Format(key, detail.FrameRate ?? 0),
            EffectCode.BitrateSpeechOnly or EffectCode.BitrateSlightlyDull or EffectCode.BitrateLikeOriginal =>
                loc.Format(key, detail.Kbps ?? 0),
            _ => loc.Get(key),
        };
        var glyph = effect.Severity switch
        {
            EffectSeverity.Warning => "",
            EffectSeverity.Notice => "",
            _ => "",
        };
        return new EffectViewModel(effect.Severity, glyph, text);
    }
}
