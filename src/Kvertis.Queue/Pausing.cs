using System.Collections.Concurrent;
using Kvertis.Engine.Abstractions;
using Kvertis.Engine.Processes;

namespace Kvertis.Queue;

/// <summary>
/// Freezes and thaws a <em>running</em> job in place. Queued jobs never reach the pauser; the queue
/// simply does not start them.
/// <para>
/// Fallback contract: when <see cref="TryPause"/> returns false, <see cref="JobQueue"/> cancels the
/// job's token, discards the partial output (the converter's temp file) and puts the job back at the
/// FRONT of the pending list with state <see cref="JobState.Paused"/>. Its progress keeps the last
/// reported value until it restarts. On Resume it becomes <see cref="JobState.Queued"/> and restarts
/// from scratch as the next job to start.
/// </para>
/// </summary>
public interface IJobPauser
{
    /// <summary>Suspends the running job. Returns false when this job cannot be suspended in place.</summary>
    bool TryPause(ConversionJob job);

    /// <summary>Resumes a job previously suspended by <see cref="TryPause"/>. Returns false on failure.</summary>
    bool TryResume(ConversionJob job);

    /// <summary>Called once when a job ends for any reason, so the pauser can forget per-job state.</summary>
    void OnJobFinished(ConversionJob job)
    {
    }
}

/// <summary>Never suspends; every pause of a running job uses the cancel-and-restart fallback.</summary>
public sealed class NoSuspendJobPauser : IJobPauser
{
    public static readonly NoSuspendJobPauser Instance = new();

    public bool TryPause(ConversionJob job) => false;

    public bool TryResume(ConversionJob job) => false;
}

/// <summary>
/// The job currently executing on this async flow. Set by the queue around validation and conversion,
/// so code that only sees the engine (for example the <see cref="ProcessRunner.ProcessStarted"/> handler)
/// can tell which job started a process.
/// </summary>
public static class JobExecutionContext
{
    private static readonly AsyncLocal<ConversionJob?> CurrentJob = new();

    public static ConversionJob? Current => CurrentJob.Value;

    internal static void Set(ConversionJob? job) => CurrentJob.Value = job;
}

/// <summary>
/// Default pauser for ffmpeg-based jobs: suspends the job's external process through
/// <see cref="IProcessSuspender"/> (NtSuspendProcess on Windows, ADR-005).
/// <para>
/// Process ids are learned from <see cref="ProcessRunner.ProcessStarted"/> when the injected runner is a
/// <see cref="ProcessRunner"/>; the job is identified via <see cref="JobExecutionContext.Current"/>, which
/// flows into the runner because the converter calls it on the job's async flow. Other runners can feed
/// ids through <see cref="OnProcessStarted(int)"/> and <see cref="OnProcessExited(int)"/>.
/// </para>
/// <para>
/// A process id is forgotten as soon as the process exits (<see cref="ProcessRunner.ProcessExited"/>) or the
/// job finishes: Windows reuses ids, so a stale id could otherwise suspend an unrelated process. While a
/// process is suspended the runner's timeout does not count (<see cref="ProcessRunner.NotifySuspended"/>).
/// </para>
/// <para>
/// Returns false (so the queue uses the cancel-and-restart fallback described on <see cref="IJobPauser"/>)
/// when the job has not started a process (image and document converters work in-process), or when the
/// suspender refuses (non-Windows: <c>NullProcessSuspender</c>).
/// </para>
/// </summary>
public sealed class ProcessSuspendJobPauser : IJobPauser, IDisposable
{
    private readonly IProcessSuspender _suspender;
    private readonly ProcessRunner? _processRunner;
    private readonly ConcurrentDictionary<Guid, int> _processIds = new();

    public ProcessSuspendJobPauser(IProcessSuspender suspender, IProcessRunner? processRunner)
    {
        ArgumentNullException.ThrowIfNull(suspender);
        _suspender = suspender;
        _processRunner = processRunner as ProcessRunner;
        if (_processRunner is not null)
        {
            _processRunner.ProcessStarted += HandleProcessStarted;
            _processRunner.ProcessExited += HandleProcessExited;
        }
    }

    /// <summary>
    /// Records that the job on the current async flow started <paramref name="processId"/>. The last
    /// started process of a job is the one that gets suspended.
    /// </summary>
    public void OnProcessStarted(int processId)
    {
        if (JobExecutionContext.Current is { } job)
        {
            _processIds[job.Id] = processId;
        }
    }

    /// <summary>Forgets <paramref name="processId"/> for whichever job started it; it must never be suspended again.</summary>
    public void OnProcessExited(int processId)
    {
        foreach (var entry in _processIds)
        {
            if (entry.Value == processId)
            {
                _processIds.TryRemove(entry);
            }
        }
    }

    public bool TryPause(ConversionJob job)
    {
        ArgumentNullException.ThrowIfNull(job);
        if (!_processIds.TryGetValue(job.Id, out var pid))
        {
            return false;
        }
        // Tell the runner first so its timeout cannot expire while the process is frozen.
        _processRunner?.NotifySuspended(pid);
        if (_suspender.TrySuspend(pid))
        {
            return true;
        }
        _processRunner?.NotifyResumed(pid);
        return false;
    }

    public bool TryResume(ConversionJob job)
    {
        ArgumentNullException.ThrowIfNull(job);
        if (!_processIds.TryGetValue(job.Id, out var pid) || !_suspender.TryResume(pid))
        {
            return false;
        }
        _processRunner?.NotifyResumed(pid);
        return true;
    }

    public void OnJobFinished(ConversionJob job)
    {
        ArgumentNullException.ThrowIfNull(job);
        _processIds.TryRemove(job.Id, out _);
    }

    public void Dispose()
    {
        if (_processRunner is not null)
        {
            _processRunner.ProcessStarted -= HandleProcessStarted;
            _processRunner.ProcessExited -= HandleProcessExited;
        }
    }

    private void HandleProcessStarted(object? sender, int processId) => OnProcessStarted(processId);

    private void HandleProcessExited(object? sender, int processId) => OnProcessExited(processId);
}
