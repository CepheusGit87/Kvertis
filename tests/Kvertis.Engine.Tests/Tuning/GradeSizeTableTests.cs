using Kvertis.Engine.Abstractions;
using Kvertis.Engine.Estimation;
using Kvertis.Engine.Formats;
using Kvertis.Engine.Tuning;
using Shouldly;
using Xunit;

namespace Kvertis.Engine.Tests.Tuning;

public sealed class GradeSizeTableTests
{
    private static readonly FormatRegistry Registry = TuningSamples.Registry;

    private static GradeSizeTable Build(InputInfo input, FormatId output) =>
        GradeSizeTableBuilder.Build(input, new ConversionSettings(output), new Estimator(new SpeedProfile(), Registry), Registry);

    [Theory]
    [MemberData(nameof(TuningSamples.GradedPairs), MemberType = typeof(TuningSamples))]
    public void A_higher_grade_never_estimates_a_smaller_file(InputInfo input, FormatId output)
    {
        var table = Build(input, output);

        table.Points.Count.ShouldBe(GradeSizeTable.StepCount);
        for (var i = 1; i < table.Points.Count; i++)
        {
            table.Points[i].Bytes.ShouldBeGreaterThanOrEqualTo(table.Points[i - 1].Bytes);
            table.Points[i].Grade.Clamped.ShouldBe(i * GradeSizeTable.StepSize);
        }

        long previous = 0;
        for (var grade = 0; grade <= 100; grade++)
        {
            var bytes = table.BytesAt(new QualityGrade(grade));
            bytes.ShouldBeGreaterThanOrEqualTo(previous, $"{input.Kind} -> {output} at grade {grade}");
            previous = bytes;
        }
    }

    [Fact]
    public void The_table_is_deterministic()
    {
        var first = Build(TuningSamples.Photo, FormatRegistry.Jpg);
        var second = Build(TuningSamples.Photo, FormatRegistry.Jpg);

        first.Points.ShouldBe(second.Points);
    }

    [Fact]
    public void Edges_clamp_and_the_ends_match_the_points()
    {
        var table = Build(TuningSamples.Photo, FormatRegistry.Jpg);

        table.BytesAt(QualityGrade.Lowest).ShouldBe(table.MinBytes);
        table.BytesAt(QualityGrade.Highest).ShouldBe(table.MaxBytes);
        table.BytesAt(new QualityGrade(-30)).ShouldBe(table.MinBytes);
        table.BytesAt(new QualityGrade(300)).ShouldBe(table.MaxBytes);
        table.GradeForBytes(0).ShouldBe(QualityGrade.Lowest);
        table.GradeForBytes(long.MaxValue).ShouldBe(QualityGrade.Highest);
        table.MinBytes.ShouldBeLessThan(table.MaxBytes);
    }

    [Fact]
    public void Bytes_and_grade_are_inverse_within_one_step()
    {
        var table = Build(TuningSamples.Photo, FormatRegistry.Jpg);

        for (var grade = 0; grade <= 100; grade += 5)
        {
            var bytes = table.BytesAt(new QualityGrade(grade));
            var back = table.GradeForBytes(bytes).Clamped;
            Math.Abs(back - grade).ShouldBeLessThanOrEqualTo(GradeSizeTable.StepSize, $"grade {grade} came back as {back}");
        }
    }

    [Fact]
    public void Severity_gets_worse_towards_the_low_end()
    {
        var table = Build(TuningSamples.Photo, FormatRegistry.Jpg);

        table.Points[0].Worst.ShouldBe(EffectSeverity.Warning);
        table.Points[^1].Worst.ShouldBe(EffectSeverity.Fine);
        table.WorstAt(QualityGrade.Lowest).ShouldBe(EffectSeverity.Warning);
        table.WorstAt(QualityGrade.Highest).ShouldBe(EffectSeverity.Fine);
    }

    [Fact]
    public void A_target_size_never_breaks_the_monotony()
    {
        var baseline = new ConversionSettings(FormatRegistry.Jpg, TargetSizeBytes: 500_000);

        var table = GradeSizeTableBuilder.Build(TuningSamples.Photo, baseline, new Estimator(new SpeedProfile(), Registry), Registry);

        for (var i = 1; i < table.Points.Count; i++)
        {
            table.Points[i].Bytes.ShouldBeGreaterThanOrEqualTo(table.Points[i - 1].Bytes);
        }
    }

    [Fact]
    public void A_table_needs_exactly_the_agreed_number_of_points()
    {
        Should.Throw<ArgumentException>(() => new GradeSizeTable([new GradeSizePoint(QualityGrade.Lowest, 1, EffectSeverity.Fine)]));
        Should.Throw<ArgumentNullException>(() => new GradeSizeTable(null!));
    }

    [Fact]
    public void Null_arguments_are_rejected()
    {
        var estimator = new Estimator(new SpeedProfile(), Registry);
        var settings = new ConversionSettings(FormatRegistry.Jpg);
        Should.Throw<ArgumentNullException>(() => GradeSizeTableBuilder.Build(null!, settings, estimator, Registry));
        Should.Throw<ArgumentNullException>(() => GradeSizeTableBuilder.Build(TuningSamples.Photo, null!, estimator, Registry));
        Should.Throw<ArgumentNullException>(() => GradeSizeTableBuilder.Build(TuningSamples.Photo, settings, null!, Registry));
        Should.Throw<ArgumentNullException>(() => GradeSizeTableBuilder.Build(TuningSamples.Photo, settings, estimator, null!));
    }
}
