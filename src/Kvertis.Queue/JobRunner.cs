using Kvertis.Engine.Abstractions;
using Kvertis.Engine.Formats;
using Kvertis.Engine.Naming;
using Kvertis.Engine.Probing;
using Microsoft.Extensions.Logging;

namespace Kvertis.Queue;

internal enum JobOutcomeKind
{
    Completed,
    Failed,
    Cancelled,
}

internal sealed record JobOutcome(JobOutcomeKind Kind, ConversionResult? Result = null, ConversionErrorCode Error = ConversionErrorCode.None, string? Detail = null)
{
    public static JobOutcome Completed(ConversionResult result) => new(JobOutcomeKind.Completed, result);

    public static JobOutcome Failed(ConversionErrorCode error, string? detail) =>
        new(JobOutcomeKind.Failed, Error: error == ConversionErrorCode.None ? ConversionErrorCode.Unknown : error, Detail: detail);

    public static readonly JobOutcome Cancelled = new(JobOutcomeKind.Cancelled, Error: ConversionErrorCode.Cancelled);
}

/// <summary>
/// Executes one job: validate, pick converter, resolve output path, estimate, convert, record speed.
/// Never throws; every failure becomes a <see cref="JobOutcome"/>. State transitions are left to the queue.
/// </summary>
internal sealed class JobRunner
{
    private readonly IConverterResolver _resolver;
    private readonly IInputValidator _validator;
    private readonly IEstimator _estimator;
    private readonly FormatRegistry _registry;
    private readonly OutputPathReserver _reserver;
    private readonly ISpeedProfileStore? _speedStore;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly Action<ConversionJob, JobChangeKind> _raise;

    public JobRunner(
        IConverterResolver resolver,
        IInputValidator validator,
        IEstimator estimator,
        FormatRegistry registry,
        OutputPathReserver reserver,
        ISpeedProfileStore? speedStore,
        TimeProvider time,
        ILogger logger,
        Action<ConversionJob, JobChangeKind> raise)
    {
        _resolver = resolver;
        _validator = validator;
        _estimator = estimator;
        _registry = registry;
        _reserver = reserver;
        _speedStore = speedStore;
        _time = time;
        _logger = logger;
        _raise = raise;
    }

    /// <summary>Re-detects audio/video inputs without cached probe data before routing. Optional.</summary>
    public IFormatDetector? FormatDetector { get; init; }

    /// <summary>The shared probe cache; see <see cref="FormatDetector"/>.</summary>
    public MediaInfoCache? MediaInfo { get; init; }

    public async Task<JobOutcome> RunAsync(ConversionJob job, ThrottledProgress progress, CancellationToken ct)
    {
        // Scoped to this async flow: the value is restored when this method returns to its caller.
        JobExecutionContext.Set(job);
        try
        {
            progress.Report(new ConversionProgress(0, ConversionPhase.Analyzing));

            var validation = await _validator.ValidateAsync(job.Input, ct).ConfigureAwait(false);
            if (validation is null || !validation.IsAccepted)
            {
                return JobOutcome.Failed(validation?.Error ?? ConversionErrorCode.Unknown, validation?.Detail);
            }
            ct.ThrowIfCancellationRequested();

            var input = await EnsureProbedAsync(job.Input, ct).ConfigureAwait(false);
            var converter = _resolver.Resolve(input, job.Settings.Output);
            if (converter is null)
            {
                return JobOutcome.Failed(ConversionErrorCode.UnsupportedFormat, $"{input.Format} -> {job.Settings.Output}");
            }

            try
            {
                Directory.CreateDirectory(job.OutputDirectory);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                return JobOutcome.Failed(ConversionErrorCode.OutputNotWritable, ex.Message);
            }

            var fileName = OutputNamePattern.Render(
                job.NamePattern, job.Input.Path, _registry.ExtensionFor(job.Settings.Output), _time.GetLocalNow(), job.BatchIndex);
            job.OutputPath = _reserver.Reserve(job.OutputDirectory, fileName);
            job.Estimate = SafeEstimate(_estimator, job, _logger);
            _raise(job, JobChangeKind.Details);

            var result = await converter.ConvertAsync(input, job.OutputPath, job.Settings, progress, ct).ConfigureAwait(false);
            if (result is null)
            {
                return JobOutcome.Failed(ConversionErrorCode.Unknown, $"Converter '{converter.Name}' returned no result");
            }

            try
            {
                _estimator.Record(job.Input, job.Settings, result);
                _speedStore?.RequestSave();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Recording the speed of job {JobId} failed", job.Id);
            }

            return JobOutcome.Completed(result);
        }
        catch (OperationCanceledException)
        {
            return JobOutcome.Cancelled;
        }
        catch (ConversionException ce) when (ce.Code == ConversionErrorCode.Cancelled)
        {
            // ProcessRunner reports a cancelled ffmpeg run this way.
            return JobOutcome.Cancelled;
        }
        catch (ConversionException ce)
        {
            return JobOutcome.Failed(ce.Code, ce.Detail ?? ce.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Job {JobId} failed with an unexpected exception", job.Id);
            return JobOutcome.Failed(ConversionErrorCode.Unknown, ex.GetType().Name + ": " + ex.Message);
        }
        finally
        {
            JobExecutionContext.Set(null);
        }
    }

    /// <summary>
    /// The converters route audio/video by the cached ffprobe data (ADR-015). When the entry is gone (evicted, or
    /// the file's size/mtime changed since it was added) the input is detected again, which probes and refills
    /// the cache; otherwise an MKV with H.264 would resolve to the ffmpeg converter and fail instead of going to
    /// the system transcoder.
    /// </summary>
    private async Task<InputInfo> EnsureProbedAsync(InputInfo input, CancellationToken ct)
    {
        if (FormatDetector is null || MediaInfo is null
            || input.Kind is not (MediaKind.Audio or MediaKind.Video)
            || MediaInfo.TryGet(input.Path, out _))
        {
            return input;
        }
        _logger.LogDebug("No cached probe data for {Path}; detecting again", input.Path);
        return await FormatDetector.DetectAsync(input.Path, ct).ConfigureAwait(false);
    }

    public static Estimate SafeEstimate(IEstimator estimator, ConversionJob job, ILogger logger)
    {
        try
        {
            return estimator.Estimate(job.Input, job.Settings) ?? Estimate.Unknown;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Estimating job {JobId} failed", job.Id);
            return Estimate.Unknown;
        }
    }
}

/// <summary>
/// Progress sink handed to the converter. Updates the job synchronously and raises
/// <see cref="JobChangeKind.Progress"/> at most once per throttle interval, plus always on phase change.
/// Ignores reports once closed or while the job is not running (a paused job keeps its last value).
/// </summary>
internal sealed class ThrottledProgress : IProgress<ConversionProgress>
{
    private readonly ConversionJob _job;
    private readonly TimeProvider _time;
    private readonly TimeSpan _throttle;
    private readonly Action<ConversionJob, JobChangeKind> _raise;
    private readonly object _gate = new();
    private ConversionPhase? _lastPhase;
    private long _lastRaised;
    private bool _closed;

    public ThrottledProgress(ConversionJob job, TimeProvider time, TimeSpan throttle, Action<ConversionJob, JobChangeKind> raise)
    {
        _job = job;
        _time = time;
        _throttle = throttle;
        _raise = raise;
    }

    public void Report(ConversionProgress value)
    {
        if (value is null)
        {
            return;
        }

        bool raise;
        lock (_gate)
        {
            if (_closed || _job.State != JobState.Running)
            {
                return;
            }
            var fraction = double.IsNaN(value.Fraction) ? 0 : Math.Clamp(value.Fraction, 0, 1);
            _job.Progress = value with { Fraction = fraction };
            var now = _time.GetTimestamp();
            raise = _lastPhase != value.Phase || _time.GetElapsedTime(_lastRaised, now) >= _throttle;
            if (raise)
            {
                _lastPhase = value.Phase;
                _lastRaised = now;
            }
        }

        if (raise)
        {
            _raise(_job, JobChangeKind.Progress);
        }
    }

    public void Close()
    {
        lock (_gate)
        {
            _closed = true;
        }
    }
}
