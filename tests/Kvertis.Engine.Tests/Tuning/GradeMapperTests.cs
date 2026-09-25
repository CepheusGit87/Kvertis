using Kvertis.Engine.Abstractions;
using Kvertis.Engine.Conversion.Video;
using Kvertis.Engine.Formats;
using Kvertis.Engine.Tuning;
using Shouldly;
using Xunit;

namespace Kvertis.Engine.Tests.Tuning;

public sealed class GradeMapperTests
{
    /// <summary>
    /// Steps lose information, so the round trip lands in the middle of the step the grade fell into. The
    /// bound is set by the coarsest ladder: the system AAC encoder accepts four bitrates for the whole
    /// range, so a quarter of the scale maps to one value. Finer ladders stay well below it.
    /// </summary>
    private const int RoundTripTolerance = 20;

    /// <summary>Bound for the ladders that are not cut down by a format (MP3, Opus, JPG, WebP, WebM).</summary>
    private const int FineRoundTripTolerance = 12;

    private static readonly FormatRegistry Registry = TuningSamples.Registry;

    [Theory]
    [MemberData(nameof(TuningSamples.GradedPairs), MemberType = typeof(TuningSamples))]
    public void Round_trip_stays_within_the_tolerance(InputInfo input, FormatId output)
    {
        var baseline = new ConversionSettings(output);

        for (var grade = 0; grade <= 100; grade++)
        {
            var settings = GradeMapper.Apply(new QualityGrade(grade), baseline, input, Registry);
            var back = GradeMapper.GradeOf(settings, input, Registry).Clamped;

            Math.Abs(back - grade).ShouldBeLessThanOrEqualTo(
                RoundTripTolerance,
                $"{input.Kind} -> {output}: grade {grade} came back as {back}");
        }
    }

    [Theory]
    [InlineData("mp3")]
    [InlineData("opus")]
    public void Round_trip_of_a_fine_ladder_is_tighter(string output)
    {
        var baseline = new ConversionSettings(FormatId.Parse(output));

        for (var grade = 0; grade <= 100; grade++)
        {
            var settings = GradeMapper.Apply(new QualityGrade(grade), baseline, TuningSamples.Song, Registry);
            var back = GradeMapper.GradeOf(settings, TuningSamples.Song, Registry).Clamped;

            Math.Abs(back - grade).ShouldBeLessThanOrEqualTo(
                FineRoundTripTolerance,
                $"{output}: grade {grade} came back as {back}");
        }
    }

    [Theory]
    [MemberData(nameof(TuningSamples.GradedPairs), MemberType = typeof(TuningSamples))]
    public void Round_trip_never_runs_backwards(InputInfo input, FormatId output)
    {
        var baseline = new ConversionSettings(output);
        var previous = -1;

        for (var grade = 0; grade <= 100; grade++)
        {
            var settings = GradeMapper.Apply(new QualityGrade(grade), baseline, input, Registry);
            var back = GradeMapper.GradeOf(settings, input, Registry).Clamped;
            back.ShouldBeGreaterThanOrEqualTo(previous, $"{input.Kind} -> {output} at grade {grade}");
            previous = back;
        }
    }

    [Theory]
    [MemberData(nameof(TuningSamples.GradedPairs), MemberType = typeof(TuningSamples))]
    public void Apply_is_deterministic(InputInfo input, FormatId output)
    {
        var baseline = new ConversionSettings(output);

        for (var grade = 0; grade <= 100; grade += 7)
        {
            var first = GradeMapper.Apply(new QualityGrade(grade), baseline, input, Registry);
            var second = GradeMapper.Apply(new QualityGrade(grade), baseline, input, Registry);
            first.Quality.ShouldBe(second.Quality);
            Advanced(first).ShouldBe(Advanced(second));
        }
    }

    [Fact]
    public void Edges_clamp_and_stay_inside_the_allowed_ranges()
    {
        var baseline = new ConversionSettings(FormatRegistry.Jpg);

        var below = GradeMapper.Apply(new QualityGrade(-40), baseline, TuningSamples.Photo, Registry);
        var lowest = GradeMapper.Apply(QualityGrade.Lowest, baseline, TuningSamples.Photo, Registry);
        var highest = GradeMapper.Apply(QualityGrade.Highest, baseline, TuningSamples.Photo, Registry);
        var above = GradeMapper.Apply(new QualityGrade(180), baseline, TuningSamples.Photo, Registry);

        Advanced(below).ShouldBe(Advanced(lowest));
        Advanced(above).ShouldBe(Advanced(highest));
        lowest.Quality.ShouldBe(30);
        highest.Quality.ShouldBe(100);
        // Grade 0 shrinks the longest edge, grade 100 keeps the source size ("0" = keep).
        lowest.GetAdvancedInt(ConversionSettings.AdvancedKeys.MaxDimension).ShouldBe(640);
        highest.GetAdvancedInt(ConversionSettings.AdvancedKeys.MaxDimension).ShouldBe(0);
    }

    [Fact]
    public void Grade_zero_and_hundred_map_to_the_band_words()
    {
        QualityGrade.Lowest.Band.ShouldBe(GradeBand.HeavilyReduced);
        QualityGrade.Highest.Band.ShouldBe(GradeBand.Excellent);
        QualityGrade.Default.Band.ShouldBe(GradeBand.Good);
        new QualityGrade(-5).Clamped.ShouldBe(0);
        new QualityGrade(500).Clamped.ShouldBe(100);
    }

    [Fact]
    public void Apply_keeps_everything_it_does_not_own()
    {
        var baseline = new ConversionSettings(
            FormatRegistry.Jpg,
            Quality: 55,
            TargetSizeBytes: 1_000_000,
            Metadata: MetadataPolicy.Keep,
            Preset: ConversionPreset.Email,
            Advanced: new Dictionary<string, string>(StringComparer.Ordinal) { ["dpi"] = "150" });

        var applied = GradeMapper.Apply(new QualityGrade(60), baseline, TuningSamples.Photo, Registry);

        applied.Output.ShouldBe(FormatRegistry.Jpg);
        applied.TargetSizeBytes.ShouldBe(1_000_000);
        applied.Metadata.ShouldBe(MetadataPolicy.Keep);
        applied.Preset.ShouldBe(ConversionPreset.Email);
        applied.GetAdvanced("dpi").ShouldBe("150");
        applied.Quality.ShouldNotBe(55);
    }

    [Fact]
    public void Quality_is_linear_over_the_grade()
    {
        GradeMapper.QualityForGrade(0).ShouldBe(30);
        GradeMapper.QualityForGrade(50).ShouldBe(65);
        GradeMapper.QualityForGrade(100).ShouldBe(100);
        GradeMapper.GradeForQuality(GradeMapper.QualityForGrade(37)).ShouldBe(37);
    }

    [Fact]
    public void Images_never_get_upscaled_beyond_the_source()
    {
        for (var grade = 0; grade <= 100; grade += 5)
        {
            var settings = GradeMapper.Apply(new QualityGrade(grade), new ConversionSettings(FormatRegistry.Jpg), TuningSamples.TinyImage, Registry);
            var max = settings.GetAdvancedInt(ConversionSettings.AdvancedKeys.MaxDimension) ?? 0;
            // 0 = keep; anything else must be below the 320 px source.
            (max == 0 || max < 320).ShouldBeTrue($"grade {grade} asked for {max} px");
        }
    }

    [Fact]
    public void Documents_and_models_have_no_grade_and_no_aspects()
    {
        GradeMapper.SupportsGrade(MediaKind.Document, FormatRegistry.Txt, Registry).ShouldBeFalse();
        GradeMapper.SupportsGrade(MediaKind.Model3D, FormatRegistry.Stl, Registry).ShouldBeFalse();
        GradeMapper.SupportsGrade(MediaKind.Unknown, FormatRegistry.Txt, Registry).ShouldBeFalse();

        GradeMapper.Aspects(new ConversionSettings(FormatRegistry.Txt), TuningSamples.Report, Registry).ShouldBeEmpty();
        GradeMapper.Aspects(new ConversionSettings(FormatRegistry.Stl), TuningSamples.Part, Registry).ShouldBeEmpty();

        var settings = new ConversionSettings(FormatRegistry.Txt, Quality: 42);
        GradeMapper.Apply(QualityGrade.Lowest, settings, TuningSamples.Report, Registry).ShouldBe(settings);
    }

    [Fact]
    public void Lossless_audio_has_no_grade_but_reports_a_fixed_sound_aspect()
    {
        GradeMapper.SupportsGrade(MediaKind.Audio, FormatRegistry.Flac, Registry).ShouldBeFalse();
        GradeMapper.SupportsGrade(MediaKind.Audio, FormatRegistry.Mp3, Registry).ShouldBeTrue();

        var aspects = GradeMapper.Aspects(new ConversionSettings(FormatRegistry.Wav), TuningSamples.Song, Registry);

        aspects.Count.ShouldBe(1);
        aspects[0].Aspect.ShouldBe(TuningAspect.Sound);
        aspects[0].Adjustable.ShouldBeFalse();
        aspects[0].Detail.Lossless.ShouldBeTrue();
    }

    [Fact]
    public void Lossless_images_keep_sharpness_but_fix_the_detail()
    {
        GradeMapper.SupportsGrade(MediaKind.Image, FormatRegistry.Png, Registry).ShouldBeTrue();

        var aspects = GradeMapper.Aspects(new ConversionSettings(FormatRegistry.Png), TuningSamples.Photo, Registry);

        aspects.Select(a => a.Aspect).ShouldBe([TuningAspect.Sharpness, TuningAspect.Detail]);
        aspects[0].Adjustable.ShouldBeTrue();
        aspects[1].Adjustable.ShouldBeFalse();
        GradeMapper.Apply(QualityGrade.Lowest, new ConversionSettings(FormatRegistry.Png), TuningSamples.Photo, Registry)
            .Quality.ShouldBe(100);
    }

    [Fact]
    public void Gif_output_fixes_the_detail_because_of_the_palette()
    {
        var aspects = GradeMapper.Aspects(new ConversionSettings(FormatRegistry.Gif), TuningSamples.Photo, Registry);

        aspects.Single(a => a.Aspect == TuningAspect.Detail).Adjustable.ShouldBeFalse();
        aspects.Single(a => a.Aspect == TuningAspect.Sharpness).Adjustable.ShouldBeTrue();
    }

    [Fact]
    public void Video_has_four_aspects_and_an_audio_only_output_has_only_sound()
    {
        var video = GradeMapper.Aspects(new ConversionSettings(FormatRegistry.WebM), TuningSamples.Clip, Registry);
        video.Select(a => a.Aspect).ShouldBe([TuningAspect.Sharpness, TuningAspect.Detail, TuningAspect.Motion, TuningAspect.Sound]);
        video.ShouldAllBe(a => a.Adjustable);

        var audioOnly = GradeMapper.Aspects(new ConversionSettings(FormatRegistry.Mp3), TuningSamples.Clip, Registry);
        audioOnly.Where(a => a.Adjustable).Select(a => a.Aspect).ShouldBe([TuningAspect.Sound]);
        audioOnly.Where(a => !a.Adjustable).ShouldAllBe(a => a.Value == 0);
    }

    [Fact]
    public void Aac_outputs_only_use_bitrates_the_system_encoder_accepts()
    {
        for (var grade = 0; grade <= 100; grade += 5)
        {
            var settings = GradeMapper.Apply(new QualityGrade(grade), new ConversionSettings(FormatRegistry.M4a), TuningSamples.Song, Registry);
            var kbps = settings.GetAdvancedInt(ConversionSettings.AdvancedKeys.AudioBitrateKbps);
            kbps.ShouldNotBeNull();
            TranscodePlan.AacBitrateSteps.ShouldContain(kbps!.Value);
        }
    }

    [Fact]
    public void The_audio_track_of_a_video_stays_below_the_ceiling()
    {
        var settings = GradeMapper.Apply(QualityGrade.Highest, new ConversionSettings(FormatRegistry.WebM), TuningSamples.Clip, Registry);

        settings.GetAdvancedInt(ConversionSettings.AdvancedKeys.AudioBitrateKbps).ShouldBe(192);
    }

    [Fact]
    public void WithAspect_changes_one_zone_and_leaves_the_others()
    {
        var baseline = GradeMapper.Apply(QualityGrade.Default, new ConversionSettings(FormatRegistry.WebM), TuningSamples.Clip, Registry);

        var changed = GradeMapper.WithAspect(baseline, TuningAspect.Sound, 10, TuningSamples.Clip, Registry);

        changed.Quality.ShouldBe(baseline.Quality);
        changed.GetAdvanced(ConversionSettings.AdvancedKeys.MaxDimension)
            .ShouldBe(baseline.GetAdvanced(ConversionSettings.AdvancedKeys.MaxDimension));
        changed.GetAdvancedInt(ConversionSettings.AdvancedKeys.AudioBitrateKbps)!.Value
            .ShouldBeLessThan(baseline.GetAdvancedInt(ConversionSettings.AdvancedKeys.AudioBitrateKbps)!.Value);
        GradeMapper.GradeOf(changed, TuningSamples.Clip, Registry).Clamped
            .ShouldBeLessThan(GradeMapper.GradeOf(baseline, TuningSamples.Clip, Registry).Clamped);
    }

    [Fact]
    public void WithAspect_on_a_fixed_aspect_returns_the_settings_unchanged()
    {
        var baseline = new ConversionSettings(FormatRegistry.Png);

        GradeMapper.WithAspect(baseline, TuningAspect.Detail, 10, TuningSamples.Photo, Registry).ShouldBe(baseline);
        GradeMapper.WithAspect(baseline, TuningAspect.Motion, 10, TuningSamples.Photo, Registry).ShouldBe(baseline);
        GradeMapper.WithAspect(new ConversionSettings(FormatRegistry.Txt), TuningAspect.Detail, 10, TuningSamples.Report, Registry)
            .ShouldBe(new ConversionSettings(FormatRegistry.Txt));
    }

    [Fact]
    public void Every_registry_combination_maps_without_throwing()
    {
        var inputs = new[] { TuningSamples.Photo, TuningSamples.TinyImage, TuningSamples.Song, TuningSamples.Clip, TuningSamples.Report, TuningSamples.Part };

        foreach (var input in inputs)
        {
            foreach (var descriptor in Registry.All.Where(d => d.CanWrite))
            {
                var baseline = new ConversionSettings(descriptor.Id);
                foreach (var grade in new[] { 0, 33, 80, 100 })
                {
                    var settings = GradeMapper.Apply(new QualityGrade(grade), baseline, input, Registry);
                    var aspects = GradeMapper.Aspects(settings, input, Registry);
                    aspects.ShouldAllBe(a => a.Value >= 0 && a.Value <= 100);
                    GradeMapper.GradeOf(settings, input, Registry).Clamped.ShouldBeInRange(0, 100);
                    GradeMapper.SupportsGrade(input.Kind, descriptor.Id, Registry);
                    foreach (var aspect in Enum.GetValues<TuningAspect>())
                    {
                        GradeMapper.WithAspect(settings, aspect, grade, input, Registry).ShouldNotBeNull();
                    }
                }
            }
        }
    }

    [Fact]
    public void Null_arguments_are_rejected()
    {
        var settings = new ConversionSettings(FormatRegistry.Jpg);
        Should.Throw<ArgumentNullException>(() => GradeMapper.Apply(QualityGrade.Default, null!, TuningSamples.Photo, Registry));
        Should.Throw<ArgumentNullException>(() => GradeMapper.Apply(QualityGrade.Default, settings, null!, Registry));
        Should.Throw<ArgumentNullException>(() => GradeMapper.Apply(QualityGrade.Default, settings, TuningSamples.Photo, null!));
        Should.Throw<ArgumentNullException>(() => GradeMapper.Aspects(null!, TuningSamples.Photo, Registry));
        Should.Throw<ArgumentNullException>(() => GradeMapper.SupportsGrade(MediaKind.Image, FormatRegistry.Jpg, null!));
    }

    private static string Advanced(ConversionSettings settings) =>
        settings.Advanced is null
            ? string.Empty
            : string.Join(";", settings.Advanced.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => $"{p.Key}={p.Value}"));
}
