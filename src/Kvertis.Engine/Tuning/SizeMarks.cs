using Kvertis.Engine.Abstractions;
using Kvertis.Engine.Conversion;

namespace Kvertis.Engine.Tuning;

/// <summary>One mark on the size bar: a preset that prescribes a target size for this media kind.</summary>
public sealed record SizeMark(ConversionPreset Preset, long Bytes);

/// <summary>
/// Marks of the size bar, read from <see cref="PresetCatalog"/>. Labels come from resources via
/// <see cref="ConversionPreset"/>; the engine returns no text.
/// </summary>
public static class SizeMarks
{
    /// <summary>Presets with a target size for this kind, ascending by size. Empty when the kind has none.</summary>
    public static IReadOnlyList<SizeMark> For(MediaKind kind)
    {
        var marks = new List<SizeMark>();
        foreach (var preset in Enum.GetValues<ConversionPreset>())
        {
            if (preset == ConversionPreset.None)
            {
                continue;
            }
            if (PresetCatalog.Get(preset, kind)?.TargetSizeBytes is { } bytes and > 0)
            {
                marks.Add(new SizeMark(preset, bytes));
            }
        }
        marks.Sort((a, b) => a.Bytes.CompareTo(b.Bytes));
        return marks;
    }
}
