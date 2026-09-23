namespace Kvertis.Queue;

/// <summary>
/// The conversion job queue (ADR-005). Platform-neutral and thread-safe.
/// <para>
/// Threading: <see cref="JobChanged"/> is raised on the queue's own threads (thread pool: the scheduler
/// loop and job tasks) or synchronously on the caller's thread for operations such as <see cref="Pause"/>.
/// Handlers must be quick and must not block; a UI must marshal to its dispatcher before touching controls.
/// Exceptions thrown by handlers are logged and swallowed.
/// </para>
/// </summary>
public interface IJobQueue
{
    /// <summary>Maximum number of jobs running at the same time. Changes apply to new starts; running jobs are not stopped.</summary>
    int MaxParallel { get; set; }

    /// <summary>Maximum number of video jobs running at the same time (ffmpeg is multithreaded itself). Also bounded by <see cref="MaxParallel"/>.</summary>
    int MaxParallelVideo { get; set; }

    /// <summary>Snapshot of all jobs in insertion order.</summary>
    IReadOnlyList<ConversionJob> Jobs { get; }

    /// <summary>Aggregate progress over <see cref="Jobs"/>, computed on each call.</summary>
    OverallProgress Overall { get; }

    event EventHandler<JobChangedEventArgs>? JobChanged;

    /// <summary>Adds a job. Throws <see cref="JobAdmissionException"/> (an <see cref="InvalidOperationException"/>) when the admission policy rejects it.</summary>
    void Enqueue(ConversionJob job);

    /// <summary>Adds several jobs atomically: the admission policy sees them together and either all or none are enqueued.</summary>
    void EnqueueRange(IEnumerable<ConversionJob> jobs);

    /// <summary>Pauses a queued or running job. Returns false when the job is unknown or already finished.</summary>
    bool Pause(Guid jobId);

    /// <summary>Resumes a paused job. Returns false when the job is unknown or not paused.</summary>
    bool Resume(Guid jobId);

    /// <summary>Cancels a job that has not finished. Returns false when the job is unknown or already finished.</summary>
    bool Cancel(Guid jobId);

    void PauseAll();

    void ResumeAll();

    void CancelAll();

    /// <summary>Removes a job that is not running (queued, paused before start, or finished). Returns false otherwise.</summary>
    bool Remove(Guid jobId);

    /// <summary>
    /// Re-enqueues a failed or cancelled job with the same input and settings as a new job (new id).
    /// The old job is removed from the list. Returns null when the job is unknown or not failed/cancelled.
    /// </summary>
    ConversionJob? Retry(Guid jobId);

    /// <summary>Removes all completed, failed and cancelled jobs.</summary>
    void ClearFinished();
}
