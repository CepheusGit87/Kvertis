using Kvertis.Engine.Abstractions;
using Kvertis.Engine.Probing;
using Microsoft.Extensions.Logging;

namespace Kvertis.Queue;

/// <summary>Optional collaborators and limits for <see cref="JobQueue"/>. Everything has a sensible default.</summary>
public sealed class JobQueueOptions
{
    public static readonly TimeSpan DefaultProgressThrottle = TimeSpan.FromMilliseconds(200);

    /// <summary>Default: number of logical processors, at least 1.</summary>
    public int MaxParallel { get; init; } = Math.Max(1, Environment.ProcessorCount);

    /// <summary>Default: half the logical processors, at least 1 (ADR-005).</summary>
    public int MaxParallelVideo { get; init; } = Math.Max(1, Environment.ProcessorCount / 2);

    /// <summary>Default <see cref="NoSuspendJobPauser"/>: running jobs are paused by cancel-and-restart.</summary>
    public IJobPauser? Pauser { get; init; }

    /// <summary>Default <see cref="AllowAllPolicy"/>.</summary>
    public IJobAdmissionPolicy? AdmissionPolicy { get; init; }

    /// <summary>Receives completed and failed jobs. Optional.</summary>
    public JobHistory? History { get; init; }

    /// <summary>Notified after each <c>IEstimator.Record</c>. Optional.</summary>
    public ISpeedProfileStore? SpeedProfileStore { get; init; }

    /// <summary>Minimum interval between two progress notifications of one job. Phase changes bypass it.</summary>
    public TimeSpan ProgressThrottle { get; init; } = DefaultProgressThrottle;

    /// <summary>Maps output formats to file extensions. Default: a new <see cref="Kvertis.Engine.Formats.FormatRegistry"/>.</summary>
    public Kvertis.Engine.Formats.FormatRegistry? Registry { get; init; }

    /// <summary>
    /// Re-detects audio/video inputs whose probe data is no longer in <see cref="MediaInfo"/> (evicted, or the file
    /// changed) right before the converter is chosen, so routing (ffmpeg vs. system transcoder, ADR-015) never
    /// runs on missing codec data. Optional; both must be set to take effect.
    /// </summary>
    public IFormatDetector? FormatDetector { get; init; }

    /// <summary>The shared ffprobe cache that the converters route by. See <see cref="FormatDetector"/>.</summary>
    public MediaInfoCache? MediaInfo { get; init; }

    public TimeProvider? TimeProvider { get; init; }

    public ILogger? Logger { get; init; }

    /// <summary>When true (default) the scheduler starts in the constructor. Tests may start it later with <see cref="JobQueue.Start"/>.</summary>
    public bool AutoStart { get; init; } = true;
}
