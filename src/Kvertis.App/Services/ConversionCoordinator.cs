using Kvertis.Engine.Abstractions;
using Kvertis.Queue;

namespace Kvertis.App.Services;

/// <summary>Where one round of step 3 stands (ADR-021).</summary>
public enum RoundState
{
    /// <summary>Nothing enqueued yet; "Umwandeln" is the action.</summary>
    Ready = 0,
    /// <summary>At least one job of the round is queued or running.</summary>
    Running,
    /// <summary>Every unfinished job of the round is paused.</summary>
    Paused,
    /// <summary>Every job of the round reached a final state.</summary>
    Finished,
}

/// <summary>One file of a round that did not succeed.</summary>
public sealed record RoundFailure(ConversionJob Job, ConversionErrorCode Error);

/// <summary>Summary of one round for the closing report. Elapsed = first StartedAt to last FinishedAt.</summary>
public sealed record RoundReport(
    int Total,
    int Completed,
    int Failed,
    int Cancelled,
    long BytesIn,
    long BytesOut,
    TimeSpan Elapsed,
    IReadOnlyList<RoundFailure> Failures);

/// <summary>What changed in the round. <see cref="Job"/> is null for a change of the round itself.</summary>
public sealed class RoundChangedEventArgs : EventArgs
{
    public RoundChangedEventArgs(ConversionJob? job, JobChangeKind? kind)
    {
        Job = job;
        Kind = kind;
    }

    public ConversionJob? Job { get; }

    public JobChangeKind? Kind { get; }
}

/// <summary>
/// Owns the jobs of the current round (ADR-021): builds them from the plan, enqueues them, filters
/// <see cref="IJobQueue.JobChanged"/> to its own ids and re-raises them on the UI thread. The only place in
/// the app that talks to the queue about conversions.
/// </summary>
public interface IConversionCoordinator
{
    RoundState State { get; }

    /// <summary>Plan order; a job replaced by <see cref="RelocateWaiting"/> keeps its slot.</summary>
    IReadOnlyList<ConversionJob> Jobs { get; }

    OverallProgress Overall { get; }

    /// <summary>Set once the round reached <see cref="RoundState.Finished"/>.</summary>
    RoundReport? Report { get; }

    /// <summary>Raised on the UI thread through <see cref="IUiDispatcher"/>.</summary>
    event EventHandler<RoundChangedEventArgs>? Changed;

    /// <summary>
    /// Builds one <see cref="ConversionJob"/> per preview and enqueues them together. Requires
    /// <see cref="RoundState.Ready"/> and no preview that still needs a folder. Throws
    /// <see cref="JobAdmissionException"/> (nothing was enqueued) and <see cref="ArgumentException"/>.
    /// </summary>
    void Start(IReadOnlyList<TargetPathPreview> previews);

    /// <summary>
    /// After the start: replaces jobs that are still waiting and whose directory changed, keeping input,
    /// settings, name pattern and batch index. Running and finished jobs are untouched. Returns how many
    /// jobs were replaced.
    /// </summary>
    int RelocateWaiting(IReadOnlyList<TargetPathPreview> previews);

    void PauseAll();

    void ResumeAll();

    void CancelAll();

    /// <summary>
    /// Forgets the round: removes its jobs from the queue and goes back to Ready. Only allowed once the round
    /// is over; a running or paused round has to be cancelled first.
    /// </summary>
    /// <exception cref="InvalidOperationException">The round is still running or paused.</exception>
    void Reset();
}

/// <inheritdoc cref="IConversionCoordinator"/>
public sealed class ConversionCoordinator : IConversionCoordinator, IDisposable
{
    private readonly IJobQueue _queue;
    private readonly IUiDispatcher _ui;
    private readonly TimeProvider _time;
    private readonly object _gate = new();
    private readonly List<ConversionJob> _jobs = [];
    private readonly HashSet<Guid> _ids = [];
    private RoundState _state = RoundState.Ready;
    private RoundReport? _report;
    private bool _disposed;

    public ConversionCoordinator(IJobQueue queue, IUiDispatcher ui, TimeProvider? time = null)
    {
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
        _ui = ui ?? throw new ArgumentNullException(nameof(ui));
        _time = time ?? TimeProvider.System;
        _queue.JobChanged += OnJobChanged;
    }

    public RoundState State
    {
        get { lock (_gate) { return _state; } }
    }

    public IReadOnlyList<ConversionJob> Jobs
    {
        get { lock (_gate) { return _jobs.ToList(); } }
    }

    public OverallProgress Overall => OverallProgress.Compute(Jobs);

    public RoundReport? Report
    {
        get { lock (_gate) { return _report; } }
    }

    public event EventHandler<RoundChangedEventArgs>? Changed;

    public void Start(IReadOnlyList<TargetPathPreview> previews)
    {
        ArgumentNullException.ThrowIfNull(previews);
        if (previews.Count == 0)
        {
            throw new InvalidOperationException("A round needs at least one file.");
        }
        if (State != RoundState.Ready)
        {
            throw new InvalidOperationException("The round already started.");
        }
        if (previews.Any(p => p.NeedsFolder))
        {
            throw new InvalidOperationException("Every file needs an output folder before the round starts.");
        }

        var jobs = previews
            .Select(p => new ConversionJob(
                p.Item.Input,
                p.Item.Settings,
                p.Directory ?? throw new ArgumentException("Preview without a directory.", nameof(previews)),
                p.Item.NamePattern,
                p.BatchIndex))
            .ToList();

        lock (_gate)
        {
            _jobs.Clear();
            _ids.Clear();
            _jobs.AddRange(jobs);
            foreach (var job in jobs)
            {
                _ids.Add(job.Id);
            }
            _report = null;
        }
        try
        {
            _queue.EnqueueRange(jobs);
        }
        catch (Exception)
        {
            // Nothing was enqueued (admission, bad folder, queue stopped): the round has to stay Ready,
            // otherwise it would be stuck in "Running" for ever.
            lock (_gate)
            {
                _jobs.Clear();
                _ids.Clear();
                _state = RoundState.Ready;
            }
            Raise(null, null);
            throw;
        }
        UpdateState(null, null);
    }

    public int RelocateWaiting(IReadOnlyList<TargetPathPreview> previews)
    {
        ArgumentNullException.ThrowIfNull(previews);
        var replacements = new List<ConversionJob>();
        var replaced = new List<(int Slot, ConversionJob Old)>();
        lock (_gate)
        {
            if (_jobs.Count == 0)
            {
                return 0;
            }
            for (var i = 0; i < _jobs.Count; i++)
            {
                var old = _jobs[i];
                var preview = previews.FirstOrDefault(p =>
                    string.Equals(p.Item.Input.Path, old.Input.Path, StringComparison.OrdinalIgnoreCase));
                if (preview is null || preview.NeedsFolder || preview.Directory is not { } directory)
                {
                    continue;
                }
                // Only jobs that have not started: a running or finished job keeps the path it already uses.
                var waiting = old.State is JobState.Queued or JobState.Paused && old.StartedAt is null;
                if (!waiting || string.Equals(old.OutputDirectory, directory, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                if (!_queue.Remove(old.Id))
                {
                    continue;
                }
                var fresh = new ConversionJob(old.Input, old.Settings, directory, old.NamePattern, old.BatchIndex);
                _jobs[i] = fresh;
                _ids.Remove(old.Id);
                _ids.Add(fresh.Id);
                replacements.Add(fresh);
                replaced.Add((i, old));
            }
        }
        if (replacements.Count == 0)
        {
            return 0;
        }
        try
        {
            _queue.EnqueueRange(replacements);
        }
        catch (Exception)
        {
            // The replacements never reached the queue. Put the files back with their old folder so the round
            // can still finish; a file that cannot be enqueued at all leaves the round rather than hanging.
            RestoreAfterFailedRelocate(replaced);
            UpdateState(null, null);
            throw;
        }
        UpdateState(null, null);
        return replacements.Count;
    }

    private void RestoreAfterFailedRelocate(IReadOnlyList<(int Slot, ConversionJob Old)> replaced)
    {
        var retry = new List<ConversionJob>();
        lock (_gate)
        {
            foreach (var (slot, old) in replaced)
            {
                _ids.Remove(_jobs[slot].Id);
                var back = new ConversionJob(
                    old.Input, old.Settings, old.OutputDirectory, old.NamePattern, old.BatchIndex);
                _jobs[slot] = back;
                _ids.Add(back.Id);
                retry.Add(back);
            }
        }
        try
        {
            _queue.EnqueueRange(retry);
        }
        catch (Exception)
        {
            lock (_gate)
            {
                foreach (var job in retry)
                {
                    _ids.Remove(job.Id);
                    _jobs.Remove(job);
                }
            }
        }
    }

    public void PauseAll() => ForEachUnfinished(id => _queue.Pause(id));

    public void ResumeAll() => ForEachUnfinished(id => _queue.Resume(id));

    public void CancelAll() => ForEachUnfinished(id => _queue.Cancel(id));

    public void Reset()
    {
        List<ConversionJob> mine;
        lock (_gate)
        {
            if (_state is RoundState.Running or RoundState.Paused)
            {
                throw new InvalidOperationException("Cancel the round before resetting it.");
            }
            mine = _jobs.ToList();
            _jobs.Clear();
            _ids.Clear();
            _report = null;
            _state = RoundState.Ready;
        }
        foreach (var job in mine)
        {
            _queue.Remove(job.Id);
        }
        Raise(null, null);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _queue.JobChanged -= OnJobChanged;
    }

    private void ForEachUnfinished(Action<Guid> action)
    {
        foreach (var job in Jobs)
        {
            if (job.State is JobState.Queued or JobState.Running or JobState.Paused)
            {
                action(job.Id);
            }
        }
    }

    private void OnJobChanged(object? sender, JobChangedEventArgs e)
    {
        lock (_gate)
        {
            if (!_ids.Contains(e.Job.Id))
            {
                // Someone else's job (there is none after stage C); never ours to report.
                return;
            }
        }
        UpdateState(e.Job, e.ChangeKind);
    }

    /// <summary>Derives the round state from the own jobs and reports the change on the UI thread.</summary>
    private void UpdateState(ConversionJob? job, JobChangeKind? kind)
    {
        lock (_gate)
        {
            var state = Derive(_jobs);
            if (state == RoundState.Finished && _state != RoundState.Finished && _jobs.Count > 0)
            {
                _report = BuildReport(_jobs);
            }
            _state = state;
        }
        Raise(job, kind);
    }

    private void Raise(ConversionJob? job, JobChangeKind? kind) =>
        _ui.Post(() => Changed?.Invoke(this, new RoundChangedEventArgs(job, kind)));

    private static RoundState Derive(IReadOnlyList<ConversionJob> jobs)
    {
        if (jobs.Count == 0)
        {
            return RoundState.Ready;
        }
        var unfinished = jobs.Where(j => j.State is JobState.Queued or JobState.Running or JobState.Paused).ToList();
        if (unfinished.Count == 0)
        {
            return RoundState.Finished;
        }
        return unfinished.All(j => j.State == JobState.Paused) ? RoundState.Paused : RoundState.Running;
    }

    private RoundReport BuildReport(IReadOnlyList<ConversionJob> jobs)
    {
        var overall = OverallProgress.Compute(jobs);
        var started = jobs.Select(j => j.StartedAt).Where(t => t is not null).Select(t => t!.Value).ToList();
        var finished = jobs.Select(j => j.FinishedAt).Where(t => t is not null).Select(t => t!.Value).ToList();
        // Cancelled jobs may carry no FinishedAt; the clock closes the gap then.
        var elapsed = started.Count > 0
            ? (finished.Count > 0 ? finished.Max() : _time.GetUtcNow()) - started.Min()
            : TimeSpan.Zero;
        if (elapsed < TimeSpan.Zero)
        {
            elapsed = TimeSpan.Zero;
        }
        var failures = jobs
            .Where(j => j.State == JobState.Failed)
            .Select(j => new RoundFailure(j, j.Error))
            .ToList();
        return new RoundReport(
            overall.Total, overall.Completed, overall.Failed, overall.Cancelled,
            overall.BytesIn, overall.BytesOut, elapsed, failures);
    }
}
