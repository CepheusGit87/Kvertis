using Kvertis.Engine.Abstractions;

namespace Kvertis.Queue.Tests;

public sealed class OverallProgressTests
{
    private static ConversionJob Job(JobState state, double fraction = 0, double? seconds = null, ConversionResult? result = null, TimeSpan? reportedRemaining = null)
    {
        var input = new InputInfo("/in/x.jpg", new FormatId("jpg"), MediaKind.Image, 1000, null, null, null, null, []);
        return new ConversionJob(input, new ConversionSettings(new FormatId("png")), "/out")
        {
            State = state,
            Progress = new ConversionProgress(fraction, ConversionPhase.Converting, reportedRemaining),
            Estimate = seconds is { } s ? new Estimate(TimeSpan.FromSeconds(s), 1, 0.5) : Estimate.Unknown,
            Result = result,
        };
    }

    [Fact]
    public void Empty_queue_is_zero()
    {
        var p = OverallProgress.Compute([]);
        p.Total.ShouldBe(0);
        p.Fraction.ShouldBe(0);
        p.Remaining.ShouldBe(TimeSpan.Zero);
    }

    [Fact]
    public void Fraction_is_weighted_by_estimated_duration()
    {
        var jobs = new[]
        {
            Job(JobState.Completed, 1, seconds: 30, result: new ConversionResult("/out/a.png", 1000, 250, TimeSpan.FromSeconds(28))),
            Job(JobState.Running, 0.5, seconds: 60),
            Job(JobState.Queued, 0, seconds: 10),
        };

        var p = OverallProgress.Compute(jobs);

        p.Fraction.ShouldBe((30 + 30) / 100.0, 1e-9);
        p.Remaining.ShouldBe(TimeSpan.FromSeconds(30 + 10));
        p.Total.ShouldBe(3);
        p.Completed.ShouldBe(1);
        p.Running.ShouldBe(1);
        p.Pending.ShouldBe(1);
        p.BytesIn.ShouldBe(1000);
        p.BytesOut.ShouldBe(250);
    }

    [Fact]
    public void Unknown_estimates_weigh_one_and_make_remaining_unknown()
    {
        var jobs = new[]
        {
            Job(JobState.Failed),
            Job(JobState.Cancelled),
            Job(JobState.Running, 0.5),
            Job(JobState.Queued, seconds: 1),
        };

        var p = OverallProgress.Compute(jobs);

        p.Fraction.ShouldBe((1 + 1 + 0.5 + 0) / 4.0, 1e-9);
        p.Remaining.ShouldBeNull();
        p.Failed.ShouldBe(1);
        p.Cancelled.ShouldBe(1);
        p.Done.ShouldBe(2);
        p.BytesIn.ShouldBe(0);
    }

    [Fact]
    public void Converter_reported_remaining_time_wins_for_running_jobs()
    {
        var p = OverallProgress.Compute([Job(JobState.Running, 0.2, seconds: 100, reportedRemaining: TimeSpan.FromSeconds(7))]);
        p.Remaining.ShouldBe(TimeSpan.FromSeconds(7));
    }

    [Fact]
    public void Paused_jobs_keep_their_progress_in_the_fraction()
    {
        var p = OverallProgress.Compute([Job(JobState.Paused, 0.4, seconds: 10)]);
        p.Fraction.ShouldBe(0.4, 1e-9);
        p.Pending.ShouldBe(1);
        p.Remaining.ShouldBe(TimeSpan.FromSeconds(6));
    }
}
