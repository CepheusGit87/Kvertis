using System.Threading.Channels;
using Kvertis.Engine.Abstractions;
using Kvertis.Engine.Formats;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kvertis.Queue;

/// <summary>
/// Default <see cref="IJobQueue"/> (ADR-005).
/// <para>
/// Structure: <see cref="Enqueue"/> writes into an unbounded <see cref="Channel{T}"/>. One scheduler loop
/// (started in the constructor, see <see cref="Start"/>/<see cref="StopAsync"/>) wakes on a
/// <see cref="SemaphoreSlim"/> signal, drains the channel into an ordered pending list and starts jobs while
/// the limits allow. Each running job gets its own task and <see cref="CancellationTokenSource"/>.
/// </para>
/// <para>
/// Limits: a job starts only while fewer than <see cref="MaxParallel"/> jobs run; a video job additionally
/// needs fewer than <see cref="MaxParallelVideo"/> running video jobs. Blocked video jobs do not block
/// non-video jobs behind them. The limits are plain counters guarded by the queue lock rather than
/// <see cref="SemaphoreSlim"/> permits, because a semaphore cannot shrink when the user lowers the limit at
/// runtime; lowering takes effect as running jobs finish, raising it starts jobs immediately.
/// </para>
/// <para>
/// Pause: queued jobs are simply skipped. Running jobs are handed to the <see cref="IJobPauser"/>; when it
/// cannot suspend, the job is cancelled and re-queued at the front as <see cref="JobState.Paused"/>, then
/// restarts from scratch on Resume (see <see cref="IJobPauser"/>). A job suspended in place keeps its slot.
/// </para>
/// </summary>
public sealed class JobQueue : IJobQueue, IAsyncDisposable
{
    private readonly IEstimator _estimator;
    private readonly IJobPauser _pauser;
    private readonly IJobAdmissionPolicy _policy;
    private readonly JobHistory? _history;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly TimeSpan _throttle;
    private readonly OutputPathReserver _reserver = new();
    private readonly JobRunner _runner;

    private readonly object _gate = new();
    private readonly List<ConversionJob> _jobs = [];
    private readonly Dictionary<Guid, ConversionJob> _byId = [];
    private readonly LinkedList<ConversionJob> _pending = new();
    private readonly Dictionary<Guid, RunningJob> _running = [];
    private readonly HashSet<Task> _active = [];
    private readonly Channel<ConversionJob> _intake = Channel.CreateUnbounded<ConversionJob>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
    private readonly SemaphoreSlim _wake = new(0);

    private int _maxParallel;
    private int _maxParallelVideo;
    private int _runningVideo;
    private bool _stopping;
    private CancellationTokenSource? _loopCts;
    private Task _loopTask = Task.CompletedTask;

    public JobQueue(IConverterResolver resolver, IInputValidator validator, IEstimator estimator, JobQueueOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(validator);
        ArgumentNullException.ThrowIfNull(estimator);
        options ??= new JobQueueOptions();

        _estimator = estimator;
        _pauser = options.Pauser ?? NoSuspendJobPauser.Instance;
        _policy = options.AdmissionPolicy ?? AllowAllPolicy.Instance;
        _history = options.History;
        _time = options.TimeProvider ?? TimeProvider.System;
        _logger = options.Logger ?? NullLogger.Instance;
        _throttle = options.ProgressThrottle < TimeSpan.Zero ? TimeSpan.Zero : options.ProgressThrottle;
        _maxParallel = Math.Max(1, options.MaxParallel);
        _maxParallelVideo = Math.Max(1, options.MaxParallelVideo);
        _runner = new JobRunner(
            resolver, validator, estimator, options.Registry ?? new FormatRegistry(), _reserver,
            options.SpeedProfileStore, _time, _logger, Raise);

        if (options.AutoStart)
        {
            Start();
        }
    }

    /// <inheritdoc />
    /// <remarks>Raised on queue threads or the caller's thread, never on a UI thread by design. The UI must marshal.</remarks>
    public event EventHandler<JobChangedEventArgs>? JobChanged;

    public int MaxParallel
    {
        get { lock (_gate) { return _maxParallel; } }
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            lock (_gate)
            {
                _maxParallel = value;
            }
            Wake();
        }
    }

    public int MaxParallelVideo
    {
        get { lock (_gate) { return _maxParallelVideo; } }
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            lock (_gate)
            {
                _maxParallelVideo = value;
            }
            Wake();
        }
    }

    public IReadOnlyList<ConversionJob> Jobs
    {
        get { lock (_gate) { return _jobs.ToArray(); } }
    }

    public OverallProgress Overall => OverallProgress.Compute(Jobs);

    /// <summary>Starts the scheduler loop. Called by the constructor unless <see cref="JobQueueOptions.AutoStart"/> is false. Idempotent.</summary>
    public void Start()
    {
        lock (_gate)
        {
            if (_loopCts is not null)
            {
                return;
            }
            _stopping = false;
            _loopCts = new CancellationTokenSource();
            var token = _loopCts.Token;
            _loopTask = Task.Run(() => LoopAsync(token), CancellationToken.None);
        }
        Wake();
    }

    /// <summary>
    /// Stops the scheduler, cancels running jobs (they end as <see cref="JobState.Cancelled"/>) and waits
    /// until their tasks have finished. Queued jobs stay queued; <see cref="Start"/> picks them up again.
    /// </summary>
    public async Task StopAsync()
    {
        CancellationTokenSource? loopCts;
        List<RunningJob> running;
        Task[] active;
        lock (_gate)
        {
            _stopping = true;
            loopCts = _loopCts;
            _loopCts = null;
            running = [.. _running.Values];
            foreach (var entry in running)
            {
                entry.CancelRequested = true;
            }
        }

        loopCts?.Cancel();
        await _loopTask.ConfigureAwait(false);
        loopCts?.Dispose();

        foreach (var entry in running)
        {
            if (entry.Suspended)
            {
                SafeTryResume(entry.Job);
            }
            entry.Cts.Cancel();
        }

        lock (_gate)
        {
            active = [.. _active];
        }
        await Task.WhenAll(active).ConfigureAwait(false);
    }

    public ValueTask DisposeAsync() => new(StopAsync());

    public void Enqueue(ConversionJob job)
    {
        ArgumentNullException.ThrowIfNull(job);
        EnqueueRange([job]);
    }

    public void EnqueueRange(IEnumerable<ConversionJob> jobs)
    {
        ArgumentNullException.ThrowIfNull(jobs);
        var list = jobs.ToList();
        if (list.Count == 0)
        {
            return;
        }
        foreach (var job in list)
        {
            ArgumentNullException.ThrowIfNull(job, nameof(jobs));
            if (job.State is not (JobState.Queued or JobState.Paused))
            {
                throw new ArgumentException($"Job {job.Id} is {job.State}; only new jobs can be enqueued. Use Retry for finished jobs.", nameof(jobs));
            }
        }
        if (list.Select(j => j.Id).Distinct().Count() != list.Count)
        {
            throw new ArgumentException("The same job was passed twice.", nameof(jobs));
        }

        var admission = _policy.Check(list) ?? AdmissionResult.Allowed;
        if (!admission.IsAllowed)
        {
            throw new JobAdmissionException(admission);
        }

        foreach (var job in list)
        {
            job.Estimate = JobRunner.SafeEstimate(_estimator, job, _logger);
        }

        lock (_gate)
        {
            foreach (var job in list)
            {
                if (_byId.ContainsKey(job.Id))
                {
                    throw new ArgumentException($"Job {job.Id} is already in the queue.", nameof(jobs));
                }
            }
            foreach (var job in list)
            {
                _byId.Add(job.Id, job);
                _jobs.Add(job);
                _intake.Writer.TryWrite(job);
            }
        }

        foreach (var job in list)
        {
            Raise(job, JobChangeKind.Added);
        }
        Wake();
    }

    public bool Pause(Guid jobId)
    {
        ConversionJob? job;
        RunningJob? entry;
        lock (_gate)
        {
            if (!_byId.TryGetValue(jobId, out job))
            {
                return false;
            }
            if (!_running.TryGetValue(jobId, out entry))
            {
                if (job.State != JobState.Queued)
                {
                    return job.State == JobState.Paused;
                }
                job.State = JobState.Paused;
            }
            else
            {
                if (job.State != JobState.Running || entry.Transitioning || entry.CancelRequested)
                {
                    return job.State == JobState.Paused;
                }
                entry.Transitioning = true;
            }
        }

        if (entry is null)
        {
            Raise(job, JobChangeKind.StateChanged);
            return true;
        }

        var suspended = SafeTryPause(job);
        var cancel = false;
        var changed = false;
        lock (_gate)
        {
            entry.Transitioning = false;
            if (_running.ContainsKey(jobId) && !entry.CancelRequested && job.State == JobState.Running)
            {
                if (suspended)
                {
                    entry.Suspended = true;
                }
                else
                {
                    // Fallback: cancel now, re-queue at the front when the cancellation lands (see Finish).
                    entry.PauseRequested = true;
                    cancel = true;
                }
                job.State = JobState.Paused;
                changed = true;
            }
        }

        if (cancel)
        {
            entry.Cts.Cancel();
        }
        if (changed)
        {
            Raise(job, JobChangeKind.StateChanged);
        }
        return changed;
    }

    public bool Resume(Guid jobId)
    {
        ConversionJob? job;
        RunningJob? entry;
        lock (_gate)
        {
            if (!_byId.TryGetValue(jobId, out job) || job.State != JobState.Paused)
            {
                return false;
            }
            if (!_running.TryGetValue(jobId, out entry))
            {
                job.State = JobState.Queued;
            }
            else if (entry.PauseRequested)
            {
                // Cancellation for the fallback is still in flight; restart right after it lands.
                entry.ResumeRequested = true;
                return true;
            }
            else if (!entry.Suspended || entry.Transitioning)
            {
                return false;
            }
            else
            {
                entry.Transitioning = true;
            }
        }

        if (entry is null)
        {
            Raise(job, JobChangeKind.StateChanged);
            Wake();
            return true;
        }

        var resumed = SafeTryResume(job);
        var cancel = false;
        var changed = false;
        lock (_gate)
        {
            entry.Transitioning = false;
            entry.Suspended = false;
            if (_running.ContainsKey(jobId) && !entry.CancelRequested)
            {
                if (resumed)
                {
                    job.State = JobState.Running;
                    changed = true;
                }
                else
                {
                    // The process cannot be thawed: restart the job from scratch as the next one to run.
                    entry.PauseRequested = true;
                    entry.ResumeRequested = true;
                    cancel = true;
                }
            }
        }

        if (cancel)
        {
            entry.Cts.Cancel();
        }
        if (changed)
        {
            Raise(job, JobChangeKind.StateChanged);
        }
        return resumed || cancel;
    }

    public bool Cancel(Guid jobId)
    {
        ConversionJob? job;
        RunningJob? entry;
        lock (_gate)
        {
            if (!_byId.TryGetValue(jobId, out job) || job.State.IsFinished())
            {
                return false;
            }
            if (_running.TryGetValue(jobId, out entry))
            {
                if (entry.CancelRequested)
                {
                    return true;
                }
                entry.CancelRequested = true;
            }
            else
            {
                _pending.Remove(job);
                MarkCancelled(job);
            }
        }

        if (entry is null)
        {
            Raise(job, JobChangeKind.StateChanged);
            return true;
        }

        if (entry.Suspended)
        {
            SafeTryResume(job); // Let the process observe the kill promptly.
        }
        entry.Cts.Cancel();
        return true;
    }

    public void PauseAll()
    {
        foreach (var job in Jobs)
        {
            if (job.State is JobState.Queued or JobState.Running)
            {
                Pause(job.Id);
            }
        }
    }

    public void ResumeAll()
    {
        foreach (var job in Jobs)
        {
            if (job.State == JobState.Paused)
            {
                Resume(job.Id);
            }
        }
    }

    public void CancelAll()
    {
        // Pending jobs first, so none of them grabs a slot freed by a cancelled running job.
        var jobs = Jobs;
        foreach (var job in jobs.Where(j => j.State != JobState.Running))
        {
            Cancel(job.Id);
        }
        foreach (var job in jobs)
        {
            Cancel(job.Id);
        }
    }

    public bool Remove(Guid jobId)
    {
        ConversionJob? job;
        lock (_gate)
        {
            if (!_byId.TryGetValue(jobId, out job) || _running.ContainsKey(jobId))
            {
                return false;
            }
            RemoveLocked(job);
        }
        Raise(job, JobChangeKind.Removed);
        return true;
    }

    public ConversionJob? Retry(Guid jobId)
    {
        ConversionJob? old;
        lock (_gate)
        {
            if (!_byId.TryGetValue(jobId, out old)
                || _running.ContainsKey(jobId)
                || old.State is not (JobState.Failed or JobState.Cancelled))
            {
                return null;
            }
        }

        var fresh = old.CloneAsNew(_time.GetUtcNow());
        Enqueue(fresh); // May throw JobAdmissionException; the old job then stays.
        Remove(old.Id);
        return fresh;
    }

    public void ClearFinished()
    {
        List<ConversionJob> removed;
        lock (_gate)
        {
            removed = _jobs.Where(j => j.State.IsFinished() && !_running.ContainsKey(j.Id)).ToList();
            foreach (var job in removed)
            {
                RemoveLocked(job);
            }
        }
        foreach (var job in removed)
        {
            Raise(job, JobChangeKind.Removed);
        }
    }

    private void RemoveLocked(ConversionJob job)
    {
        _byId.Remove(job.Id);
        _jobs.Remove(job);
        _pending.Remove(job);
    }

    private void MarkCancelled(ConversionJob job)
    {
        job.State = JobState.Cancelled;
        job.Error = ConversionErrorCode.Cancelled;
        job.FinishedAt = _time.GetUtcNow();
    }

    private void Wake()
    {
        // One outstanding signal is enough: the loop re-evaluates everything on each wake.
        if (_wake.CurrentCount == 0)
        {
            _wake.Release();
        }
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        while (true)
        {
            try
            {
                await _wake.WaitAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            try
            {
                Pump();
            }
            catch (Exception ex)
            {
                // Never let the scheduler die; the next wake retries.
                _logger.LogError(ex, "Job scheduler iteration failed");
            }
        }
    }

    /// <summary>Drains the intake channel and starts as many pending jobs as the limits allow.</summary>
    private void Pump()
    {
        var started = new List<RunningJob>();
        lock (_gate)
        {
            while (_intake.Reader.TryRead(out var incoming))
            {
                if (_byId.ContainsKey(incoming.Id) && !incoming.State.IsFinished() && !_running.ContainsKey(incoming.Id))
                {
                    _pending.AddLast(incoming);
                }
            }

            if (_stopping)
            {
                return;
            }

            var node = _pending.First;
            while (node is not null && _running.Count < _maxParallel)
            {
                var next = node.Next;
                var job = node.Value;
                if (job.State.IsFinished())
                {
                    _pending.Remove(node);
                }
                else if (job.State == JobState.Queued && (!job.IsVideo || _runningVideo < _maxParallelVideo))
                {
                    _pending.Remove(node);
                    started.Add(StartLocked(job));
                }
                node = next;
            }
        }

        foreach (var entry in started)
        {
            Raise(entry.Job, JobChangeKind.StateChanged);
            _ = Task.Run(() => ExecuteAsync(entry), CancellationToken.None);
        }
    }

    private RunningJob StartLocked(ConversionJob job)
    {
        var entry = new RunningJob(job, new ThrottledProgress(job, _time, _throttle, Raise));
        _running.Add(job.Id, entry);
        _active.Add(entry.Done.Task);
        if (entry.IsVideo)
        {
            _runningVideo++;
        }
        job.State = JobState.Running;
        job.StartedAt = _time.GetUtcNow();
        job.FinishedAt = null;
        job.Error = ConversionErrorCode.None;
        job.ErrorDetail = null;
        return entry;
    }

    private async Task ExecuteAsync(RunningJob entry)
    {
        var job = entry.Job;
        try
        {
            JobOutcome outcome;
            try
            {
                outcome = await _runner.RunAsync(job, entry.Progress, entry.Cts.Token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                outcome = JobOutcome.Failed(ConversionErrorCode.Unknown, ex.Message);
            }

            // Before Finish: once the state is final, observers may already call Pause/Resume again.
            try
            {
                _pauser.OnJobFinished(job);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Pauser cleanup for job {JobId} failed", job.Id);
            }

            var record = Finish(entry, outcome);

            if (record && _history is not null)
            {
                try
                {
                    await _history.RecordAsync(job).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Recording job {JobId} in the history failed", job.Id);
                }
            }

            Raise(job, JobChangeKind.StateChanged);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Finishing job {JobId} failed", job.Id);
        }
        finally
        {
            lock (_gate)
            {
                _active.Remove(entry.Done.Task);
            }
            entry.Done.TrySetResult();
            Wake();
        }
    }

    /// <summary>Applies the outcome to the job and frees its slot. Returns true when the job belongs in the history.</summary>
    private bool Finish(RunningJob entry, JobOutcome outcome)
    {
        var job = entry.Job;
        lock (_gate)
        {
            _running.Remove(job.Id);
            if (entry.IsVideo)
            {
                _runningVideo--;
            }
            entry.Progress.Close();
            _reserver.Release(job.OutputPath);

            var interrupted = entry.CancelRequested || entry.PauseRequested;
            var kind = outcome.Kind == JobOutcomeKind.Failed && interrupted ? JobOutcomeKind.Cancelled : outcome.Kind;

            switch (kind)
            {
                case JobOutcomeKind.Completed:
                    job.Result = outcome.Result;
                    job.OutputPath = outcome.Result?.OutputPath ?? job.OutputPath;
                    job.Progress = ConversionProgress.Complete;
                    job.State = JobState.Completed;
                    job.FinishedAt = _time.GetUtcNow();
                    return true;

                case JobOutcomeKind.Failed:
                    job.Error = outcome.Error;
                    job.ErrorDetail = outcome.Detail;
                    job.State = JobState.Failed;
                    job.FinishedAt = _time.GetUtcNow();
                    return true;

                default:
                    if (entry.PauseRequested && !entry.CancelRequested && !_stopping && _byId.ContainsKey(job.Id))
                    {
                        // Pause fallback: back to the front, restart from scratch later. Progress keeps its last value.
                        job.OutputPath = null;
                        job.StartedAt = null;
                        job.State = entry.ResumeRequested ? JobState.Queued : JobState.Paused;
                        _pending.AddFirst(job);
                    }
                    else
                    {
                        MarkCancelled(job);
                    }
                    return false;
            }
        }
    }

    private bool SafeTryPause(ConversionJob job)
    {
        try
        {
            return _pauser.TryPause(job);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Suspending job {JobId} failed", job.Id);
            return false;
        }
    }

    private bool SafeTryResume(ConversionJob job)
    {
        try
        {
            return _pauser.TryResume(job);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Resuming job {JobId} failed", job.Id);
            return false;
        }
    }

    private void Raise(ConversionJob job, JobChangeKind kind)
    {
        var handler = JobChanged;
        if (handler is null)
        {
            return;
        }
        try
        {
            handler(this, new JobChangedEventArgs(job, kind));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "A JobChanged handler threw for job {JobId}", job.Id);
        }
    }

    private sealed class RunningJob
    {
        public RunningJob(ConversionJob job, ThrottledProgress progress)
        {
            Job = job;
            Progress = progress;
            IsVideo = job.IsVideo;
        }

        public ConversionJob Job { get; }

        public ThrottledProgress Progress { get; }

        public bool IsVideo { get; }

        // Not disposed on purpose: Cancel() may race with job completion, and a CTS without timers or
        // links holds no resources that need deterministic cleanup.
        public CancellationTokenSource Cts { get; } = new();

        public TaskCompletionSource Done { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        // All flags below are guarded by the queue lock.
        public bool Suspended { get; set; }

        public bool PauseRequested { get; set; }

        public bool ResumeRequested { get; set; }

        public bool CancelRequested { get; set; }

        public bool Transitioning { get; set; }
    }
}
