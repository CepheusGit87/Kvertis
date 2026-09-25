using Kvertis.Engine.Abstractions;
using Kvertis.Engine.Conversion;
using Kvertis.Engine.Tuning;
using Shouldly;
using Xunit;

namespace Kvertis.Engine.Tests.Tuning;

public sealed class SizeMarksTests
{
    private const long MiB = 1024L * 1024;

    [Fact]
    public void Image_marks_come_from_the_preset_catalog_in_ascending_order()
    {
        var marks = SizeMarks.For(MediaKind.Image);

        marks.ShouldBe([new SizeMark(ConversionPreset.Messenger, 1 * MiB), new SizeMark(ConversionPreset.Email, 2 * MiB)]);
    }

    [Fact]
    public void Video_marks_come_from_the_preset_catalog()
    {
        var marks = SizeMarks.For(MediaKind.Video);

        marks.ShouldBe([new SizeMark(ConversionPreset.Messenger, 16 * MiB), new SizeMark(ConversionPreset.Email, 20 * MiB)]);
    }

    [Theory]
    [InlineData(MediaKind.Audio)]
    [InlineData(MediaKind.Document)]
    [InlineData(MediaKind.Model3D)]
    [InlineData(MediaKind.Unknown)]
    public void Kinds_without_a_prescribed_size_have_no_marks(MediaKind kind)
    {
        SizeMarks.For(kind).ShouldBeEmpty();
    }

    [Fact]
    public void Every_mark_matches_its_preset_definition()
    {
        foreach (var kind in Enum.GetValues<MediaKind>())
        {
            foreach (var mark in SizeMarks.For(kind))
            {
                mark.Bytes.ShouldBe(PresetCatalog.Get(mark.Preset, kind)!.TargetSizeBytes!.Value);
                mark.Preset.ShouldNotBe(ConversionPreset.None);
            }
        }
    }
}
