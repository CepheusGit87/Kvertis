using Kvertis.Engine.Abstractions;
using Kvertis.Engine.Formats;

namespace Kvertis.Engine.Tuning;

/// <summary>One support point of a <see cref="GradeSizeTable"/>.</summary>
public sealed record GradeSizePoint(QualityGrade Grade, long Bytes, EffectSeverity Worst);

/// <summary>
/// Estimated output size for each grade step of one file, precomputed so that dragging the ring or the size
/// bar never calls the estimator (ADR-019).
/// </summary>
public sealed record GradeSizeTable
{
    /// <summary>21 points: grade 0, 5, 10, … 100.</summary>
    public const int StepCount = 21;

    /// <summary>Grade distance between two points.</summary>
    public const int StepSize = 5;

    public GradeSizeTable(IReadOnlyList<GradeSizePoint> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        if (points.Count != StepCount)
        {
            throw new ArgumentException($"expected {StepCount} points, got {points.Count}", nameof(points));
        }
        Points = points;
    }

    /// <summary>Ascending by grade. Bytes are monotone: a higher grade never estimates smaller.</summary>
    public IReadOnlyList<GradeSizePoint> Points { get; }

    public long MinBytes => Points[0].Bytes;

    public long MaxBytes => Points[^1].Bytes;

    /// <summary>Estimated bytes at a grade, linearly interpolated between the two neighbouring points.</summary>
    public long BytesAt(QualityGrade grade)
    {
        var position = grade.Clamped / (double)StepSize;
        var index = Math.Min((int)Math.Floor(position), StepCount - 2);
        var fraction = position - index;
        var low = Points[index].Bytes;
        var high = Points[index + 1].Bytes;
        return (long)Math.Round(low + ((high - low) * fraction), MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// The lowest grade whose estimate reaches <paramref name="bytes"/>. Clamps to 0 and 100 outside the
    /// range; on a flat stretch it returns the lower end, so the bar never jumps forward on its own.
    /// </summary>
    public QualityGrade GradeForBytes(long bytes)
    {
        if (bytes <= MinBytes)
        {
            return QualityGrade.Lowest;
        }
        if (bytes >= MaxBytes)
        {
            return QualityGrade.Highest;
        }
        for (var i = 0; i < StepCount - 1; i++)
        {
            var low = Points[i].Bytes;
            var high = Points[i + 1].Bytes;
            if (bytes > high || high == low)
            {
                continue;
            }
            var fraction = (bytes - low) / (double)(high - low);
            return new QualityGrade(Math.Clamp(
                (int)Math.Round((i + fraction) * StepSize, MidpointRounding.AwayFromZero),
                0,
                100));
        }
        return QualityGrade.Highest;
    }

    /// <summary>Worst severity of the effects at a grade, taken from the nearest point.</summary>
    public EffectSeverity WorstAt(QualityGrade grade) =>
        Points[Math.Clamp((int)Math.Round(grade.Clamped / (double)StepSize, MidpointRounding.AwayFromZero), 0, StepCount - 1)].Worst;
}

/// <summary>Builds a <see cref="GradeSizeTable"/> from the estimator. Deterministic for a given speed profile.</summary>
public static class GradeSizeTableBuilder
{
    /// <summary>
    /// Runs <see cref="GradeMapper.Apply"/> and <see cref="IEstimator.Estimate"/> for every step. Cheap
    /// arithmetic, but call it off the UI thread for large batches.
    /// </summary>
    public static GradeSizeTable Build(InputInfo input, ConversionSettings baseline, IEstimator estimator, FormatRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(estimator);
        ArgumentNullException.ThrowIfNull(registry);

        var points = new List<GradeSizePoint>(GradeSizeTable.StepCount);
        long running = 0;
        for (var i = 0; i < GradeSizeTable.StepCount; i++)
        {
            var grade = new QualityGrade(i * GradeSizeTable.StepSize);
            var settings = GradeMapper.Apply(grade, baseline, input, registry);
            var bytes = Math.Max(estimator.Estimate(input, settings).OutputBytes, 1);
            // A learned speed profile or a target size can make a step dip; the bar must never run backwards.
            running = Math.Max(running, bytes);
            points.Add(new GradeSizePoint(grade, running, EffectAnalyzer.Worst(EffectAnalyzer.Analyze(input, settings, registry))));
        }
        return new GradeSizeTable(points);
    }
}
