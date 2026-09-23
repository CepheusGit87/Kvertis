namespace Kvertis.Queue;

/// <summary>
/// Aggregate over all jobs in the queue.
/// <see cref="Fraction"/> weighs each job by its estimated duration (seconds; 1 when unknown), counting
/// finished jobs (completed, failed, cancelled) as 1 and queued jobs as 0.
/// <see cref="Remaining"/> sums the remaining estimated time of running, paused and queued jobs as if they
/// ran one after another (an upper bound when several run in parallel); null when any of them has no estimate.
/// <see cref="BytesIn"/>/<see cref="BytesOut"/> cover completed jobs only.
/// </summary>
public sealed record OverallProgress(
    int Total,
    int Completed,
    int Failed,
    int Cancelled,
    int Running,
    int Pending,
    double Fraction,
    TimeSpan? Remaining,
    long BytesIn,
    long BytesOut)
{
    public static readonly OverallProgress Empty = new(0, 0, 0, 0, 0, 0, 0, TimeSpan.Zero, 0, 0);

    /// <summary>Jobs that reached a final state.</summary>
    public int Done => Completed + Failed + Cancelled;

    public static OverallProgress Compute(IEnumerable<ConversionJob> jobs)
    {
        ArgumentNullException.ThrowIfNull(jobs);

        int total = 0, completed = 0, failed = 0, cancelled = 0, running = 0, pending = 0;
        double weightSum = 0, doneWeight = 0;
        var remaining = TimeSpan.Zero;
        var remainingKnown = true;
        long bytesIn = 0, bytesOut = 0;

        foreach (var job in jobs)
        {
            total++;
            var state = job.State;
            var estimate = job.Estimate;
            var progress = job.Progress;
            var weight = estimate.Duration > TimeSpan.Zero ? estimate.Duration.TotalSeconds : 1.0;
            weightSum += weight;

            double fraction;
            switch (state)
            {
                case JobState.Completed:
                    completed++;
                    fraction = 1;
                    if (job.Result is { } result)
                    {
                        bytesIn += result.InputBytes;
                        bytesOut += result.OutputBytes;
                    }
                    break;
                case JobState.Failed:
                    failed++;
                    fraction = 1;
                    break;
                case JobState.Cancelled:
                    cancelled++;
                    fraction = 1;
                    break;
                case JobState.Running:
                    running++;
                    fraction = Math.Clamp(progress.Fraction, 0, 1);
                    break;
                default: // Queued, Paused
                    pending++;
                    fraction = state == JobState.Paused ? Math.Clamp(progress.Fraction, 0, 1) : 0;
                    break;
            }

            doneWeight += weight * fraction;

            if (!state.IsFinished())
            {
                if (state == JobState.Running && progress.Remaining is { } reported)
                {
                    remaining += reported;
                }
                else if (estimate.Duration > TimeSpan.Zero)
                {
                    remaining += estimate.Duration * (1 - fraction);
                }
                else
                {
                    remainingKnown = false;
                }
            }
        }

        var overall = weightSum > 0 ? doneWeight / weightSum : 0;
        return new OverallProgress(
            total, completed, failed, cancelled, running, pending,
            overall,
            remainingKnown ? remaining : null,
            bytesIn, bytesOut);
    }
}
