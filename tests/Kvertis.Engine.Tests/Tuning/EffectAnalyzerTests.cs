using Kvertis.Engine.Abstractions;
using Kvertis.Engine.Formats;
using Kvertis.Engine.Tuning;
using Shouldly;
using Xunit;

namespace Kvertis.Engine.Tests.Tuning;

public sealed class EffectAnalyzerTests
{
    private static readonly FormatRegistry Registry = TuningSamples.Registry;

    private static IReadOnlyList<EffectCode> Codes(InputInfo input, ConversionSettings settings) =>
        [.. EffectAnalyzer.Analyze(input, settings, Registry).Select(e => e.Code)];

    [Fact]
    public void Effects_are_codes_with_numbers_and_never_text()
    {
        var settings = GradeMapper.Apply(new QualityGrade(30), new ConversionSettings(FormatRegistry.Jpg), TuningSamples.Photo, Registry);

        var effects = EffectAnalyzer.Analyze(TuningSamples.Photo, settings, Registry);

        effects.ShouldNotBeEmpty();
        effects.ShouldAllBe(e => Enum.IsDefined(e.Code) && Enum.IsDefined(e.Severity));
        var resolution = effects.Single(e => e.Code == EffectCode.ResolutionReduced);
        resolution.Detail.SourcePixels.ShouldBe(4032);
        resolution.Detail.Pixels.ShouldBe(1280);
    }

    [Fact]
    public void Worst_first_and_Worst_agree()
    {
        var settings = GradeMapper.Apply(QualityGrade.Lowest, new ConversionSettings(FormatRegistry.Jpg), TuningSamples.Photo, Registry);

        var effects = EffectAnalyzer.Analyze(TuningSamples.Photo, settings, Registry);

        effects[0].Severity.ShouldBe(EffectSeverity.Warning);
        EffectAnalyzer.Worst(effects).ShouldBe(EffectSeverity.Warning);
        for (var i = 1; i < effects.Count; i++)
        {
            ((int)effects[i].Severity).ShouldBeLessThanOrEqualTo((int)effects[i - 1].Severity);
        }
    }

    [Fact]
    public void Worst_of_an_empty_list_is_fine()
    {
        EffectAnalyzer.Worst([]).ShouldBe(EffectSeverity.Fine);
    }

    [Fact]
    public void A_high_grade_keeps_resolution_and_reports_near_original_quality()
    {
        var settings = GradeMapper.Apply(QualityGrade.Highest, new ConversionSettings(FormatRegistry.Jpg), TuningSamples.Photo, Registry);

        var codes = Codes(TuningSamples.Photo, settings);

        codes.ShouldContain(EffectCode.FullResolutionKept);
        codes.ShouldContain(EffectCode.NearOriginalQuality);
        codes.ShouldContain(EffectCode.MetadataStripped);
        codes.ShouldNotContain(EffectCode.ResolutionReduced);
    }

    [Fact]
    public void Lossless_and_palette_outputs_get_their_own_codes()
    {
        Codes(TuningSamples.Photo, new ConversionSettings(FormatRegistry.Png)).ShouldContain(EffectCode.LosslessOutput);
        Codes(TuningSamples.Photo, new ConversionSettings(FormatRegistry.Gif)).ShouldContain(EffectCode.PaletteLimited);
        Codes(TuningSamples.Song, new ConversionSettings(FormatRegistry.Flac)).ShouldContain(EffectCode.LosslessOutput);
    }

    [Fact]
    public void Input_warnings_become_effects()
    {
        var animated = TuningSamples.Photo
            .WithWarning(InputWarning.TransparencyLost)
            .WithWarning(InputWarning.AnimationDropped);

        var codes = Codes(animated, new ConversionSettings(FormatRegistry.Jpg));

        codes.ShouldContain(EffectCode.TransparencyLost);
        codes.ShouldContain(EffectCode.AnimationDropped);
    }

    [Fact]
    public void Metadata_that_cannot_be_stripped_is_reported_instead_of_stripping()
    {
        var input = TuningSamples.Clip.WithWarning(InputWarning.MetadataNotStrippable);

        var codes = Codes(input, new ConversionSettings(FormatRegistry.Mp4));

        codes.ShouldContain(EffectCode.MetadataNotStrippable);
        codes.ShouldNotContain(EffectCode.MetadataStripped);
        Codes(TuningSamples.Clip, new ConversionSettings(FormatRegistry.Mp4, Metadata: MetadataPolicy.Keep))
            .ShouldContain(EffectCode.MetadataKept);
    }

    [Fact]
    public void Audio_bitrate_bands_have_their_own_codes()
    {
        Codes(TuningSamples.Song, GradeMapper.Apply(QualityGrade.Lowest, new ConversionSettings(FormatRegistry.Mp3), TuningSamples.Song, Registry))
            .ShouldContain(EffectCode.BitrateSpeechOnly);
        Codes(TuningSamples.Song, GradeMapper.Apply(new QualityGrade(45), new ConversionSettings(FormatRegistry.Mp3), TuningSamples.Song, Registry))
            .ShouldContain(EffectCode.BitrateSlightlyDull);
        Codes(TuningSamples.Song, GradeMapper.Apply(QualityGrade.Highest, new ConversionSettings(FormatRegistry.Mp3), TuningSamples.Song, Registry))
            .ShouldContain(EffectCode.BitrateLikeOriginal);
    }

    [Fact]
    public void An_audio_only_output_of_a_video_drops_the_picture()
    {
        var codes = Codes(TuningSamples.Clip, new ConversionSettings(FormatRegistry.Mp3));

        codes.ShouldContain(EffectCode.VideoTrackDropped);
        codes.ShouldNotContain(EffectCode.ResolutionReduced);
    }

    [Fact]
    public void Low_video_quality_warns_about_blocky_motion_and_a_reduced_frame_rate()
    {
        var settings = GradeMapper.Apply(QualityGrade.Lowest, new ConversionSettings(FormatRegistry.WebM), TuningSamples.Clip, Registry);

        var codes = Codes(TuningSamples.Clip, settings);

        codes.ShouldContain(EffectCode.MotionBlocky);
        codes.ShouldContain(EffectCode.FrameRateReduced);
        codes.ShouldContain(EffectCode.VisibleArtifacts);
    }

    [Fact]
    public void Documents_and_models_report_what_they_lose()
    {
        Codes(TuningSamples.Report, new ConversionSettings(FormatRegistry.Txt)).ShouldContain(EffectCode.FormattingLost);
        Codes(TuningSamples.Report, new ConversionSettings(FormatRegistry.Markdown)).ShouldContain(EffectCode.StructureKept);
        Codes(TuningSamples.Report, new ConversionSettings(FormatRegistry.Html)).ShouldContain(EffectCode.LayoutSimplified);
        Codes(TuningSamples.Part, new ConversionSettings(FormatRegistry.Stl))
            .ShouldBe([EffectCode.ColorsAndMaterialsDropped, EffectCode.UnitUnknown, EffectCode.MetadataStripped]);
    }

    [Fact]
    public void Every_registry_combination_is_analyzed_without_throwing()
    {
        var inputs = new[] { TuningSamples.Photo, TuningSamples.TinyImage, TuningSamples.Song, TuningSamples.Clip, TuningSamples.Report, TuningSamples.Part };

        foreach (var input in inputs)
        {
            foreach (var descriptor in Registry.All.Where(d => d.CanWrite))
            {
                foreach (var grade in new[] { 0, 50, 100 })
                {
                    var settings = GradeMapper.Apply(new QualityGrade(grade), new ConversionSettings(descriptor.Id), input, Registry);
                    var effects = EffectAnalyzer.Analyze(input, settings, Registry);
                    effects.ShouldAllBe(e => Enum.IsDefined(e.Code));
                    EffectAnalyzer.Worst(effects).ShouldBeOneOf(EffectSeverity.Fine, EffectSeverity.Notice, EffectSeverity.Warning);
                }
            }
        }
    }

    [Fact]
    public void Null_arguments_are_rejected()
    {
        var settings = new ConversionSettings(FormatRegistry.Jpg);
        Should.Throw<ArgumentNullException>(() => EffectAnalyzer.Analyze(null!, settings, Registry));
        Should.Throw<ArgumentNullException>(() => EffectAnalyzer.Analyze(TuningSamples.Photo, null!, Registry));
        Should.Throw<ArgumentNullException>(() => EffectAnalyzer.Analyze(TuningSamples.Photo, settings, null!));
        Should.Throw<ArgumentNullException>(() => EffectAnalyzer.Worst(null!));
    }
}
