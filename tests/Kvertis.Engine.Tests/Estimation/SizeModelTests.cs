using Kvertis.Engine.Abstractions;
using Kvertis.Engine.Estimation;
using Kvertis.Engine.Formats;
using Kvertis.Engine.Tests.Tuning;
using Kvertis.Engine.Tuning;
using Shouldly;
using Xunit;

namespace Kvertis.Engine.Tests.Estimation;

public sealed class SizeModelTests
{
    private static readonly FormatRegistry Registry = TuningSamples.Registry;

    private static Estimator NewEstimator() => new(new SpeedProfile(), Registry);

    [Theory]
    [MemberData(nameof(TuningSamples.GradedPairs), MemberType = typeof(TuningSamples))]
    public void Default_settings_are_the_reference_and_stay_at_one(InputInfo input, FormatId output)
    {
        SizeModel.Factor(input, new ConversionSettings(output), Registry).ShouldBe(1.0, 1e-9);
    }

    [Fact]
    public void Untouched_inputs_estimate_exactly_as_before_the_model()
    {
        // Backwards compatibility: settings without advanced keys and the default quality must not move.
        var settings = new ConversionSettings(FormatRegistry.Mp3);

        var estimate = NewEstimator().Estimate(TuningSamples.Song, settings);

        estimate.OutputBytes.ShouldBe((long)(TuningSamples.Song.SizeBytes * 1.0));
    }

    [Theory]
    [MemberData(nameof(TuningSamples.GradedPairs), MemberType = typeof(TuningSamples))]
    public void The_factor_is_finite_and_positive_for_every_grade(InputInfo input, FormatId output)
    {
        for (var grade = 0; grade <= 100; grade += 5)
        {
            var settings = GradeMapper.Apply(new QualityGrade(grade), new ConversionSettings(output), input, Registry);
            var factor = SizeModel.Factor(input, settings, Registry);

            double.IsFinite(factor).ShouldBeTrue();
            factor.ShouldBeGreaterThan(0);
        }
    }

    [Theory]
    [MemberData(nameof(TuningSamples.GradedPairs), MemberType = typeof(TuningSamples))]
    public void The_estimate_grows_with_the_grade(InputInfo input, FormatId output)
    {
        var estimator = NewEstimator();
        long previous = 0;

        for (var grade = 0; grade <= 100; grade += 5)
        {
            var settings = GradeMapper.Apply(new QualityGrade(grade), new ConversionSettings(output), input, Registry);
            var bytes = estimator.Estimate(input, settings).OutputBytes;

            bytes.ShouldBeGreaterThanOrEqualTo(previous, $"{input.Kind} -> {output} at grade {grade}");
            previous = bytes;
        }
    }

    [Fact]
    public void The_estimate_reacts_to_quality_resolution_and_bitrate()
    {
        var estimator = NewEstimator();

        var high = estimator.Estimate(TuningSamples.Photo, new ConversionSettings(FormatRegistry.Jpg, Quality: 95));
        var low = estimator.Estimate(TuningSamples.Photo, new ConversionSettings(FormatRegistry.Jpg, Quality: 40));
        low.OutputBytes.ShouldBeLessThan(high.OutputBytes);

        var small = estimator.Estimate(
            TuningSamples.Photo,
            new ConversionSettings(FormatRegistry.Jpg, Advanced: new Dictionary<string, string>(StringComparer.Ordinal) { ["maxDimension"] = "1008" }));
        // A quarter of the longest edge is a sixteenth of the area.
        small.OutputBytes.ShouldBeInRange(
            (long)(estimator.Estimate(TuningSamples.Photo, new ConversionSettings(FormatRegistry.Jpg)).OutputBytes / 16.5),
            (long)(estimator.Estimate(TuningSamples.Photo, new ConversionSettings(FormatRegistry.Jpg)).OutputBytes / 15.5));

        var quiet = estimator.Estimate(
            TuningSamples.Song,
            new ConversionSettings(FormatRegistry.Mp3, Advanced: new Dictionary<string, string>(StringComparer.Ordinal) { ["audioBitrateKbps"] = "96" }));
        quiet.OutputBytes.ShouldBe(estimator.Estimate(TuningSamples.Song, new ConversionSettings(FormatRegistry.Mp3)).OutputBytes / 2);
    }

    [Fact]
    public void A_target_size_still_caps_the_estimate()
    {
        var estimate = NewEstimator().Estimate(TuningSamples.Photo, new ConversionSettings(FormatRegistry.Jpg, TargetSizeBytes: 100_000));

        estimate.OutputBytes.ShouldBeLessThanOrEqualTo(100_000);
    }

    [Fact]
    public void Record_learns_the_ratio_at_the_reference_settings()
    {
        var profile = new SpeedProfile();
        var estimator = new Estimator(profile, Registry);
        var settings = new ConversionSettings(FormatRegistry.Jpg, Quality: 40);
        var referenceFactor = SizeModel.Factor(TuningSamples.Photo, settings, Registry);
        var result = new ConversionResult("/out/photo.jpg", TuningSamples.Photo.SizeBytes, 800_000, TimeSpan.FromSeconds(2));

        estimator.Record(TuningSamples.Photo, settings, result);

        var entry = profile.Get(Estimator.KeyFor(TuningSamples.Photo, settings));
        entry.ShouldNotBeNull();
        entry!.SizeRatio.ShouldBe(result.SizeRatio / referenceFactor, 1e-9);
        // Estimating the same settings again reproduces the measured size.
        estimator.Estimate(TuningSamples.Photo, settings).OutputBytes.ShouldBeInRange(799_998L, 800_002L);
    }

    [Fact]
    public void Documents_and_models_are_left_alone()
    {
        SizeModel.Factor(TuningSamples.Report, new ConversionSettings(FormatRegistry.Txt, Quality: 10), Registry).ShouldBe(1.0);
        SizeModel.Factor(TuningSamples.Part, new ConversionSettings(FormatRegistry.Stl, Quality: 10), Registry).ShouldBe(1.0);
    }

    [Fact]
    public void Null_arguments_are_rejected()
    {
        var settings = new ConversionSettings(FormatRegistry.Jpg);
        Should.Throw<ArgumentNullException>(() => SizeModel.Factor(null!, settings, Registry));
        Should.Throw<ArgumentNullException>(() => SizeModel.Factor(TuningSamples.Photo, null!, Registry));
        Should.Throw<ArgumentNullException>(() => SizeModel.Factor(TuningSamples.Photo, settings, null!));
    }
}
