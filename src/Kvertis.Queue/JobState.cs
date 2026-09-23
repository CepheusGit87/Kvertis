namespace Kvertis.Queue;

/// <summary>Lifecycle of a <see cref="ConversionJob"/>.</summary>
public enum JobState
{
    /// <summary>Waiting for a free slot.</summary>
    Queued = 0,
    /// <summary>Currently analyzing or converting.</summary>
    Running,
    /// <summary>Paused by the user: never started, suspended in place, or waiting to restart (see <see cref="IJobPauser"/>).</summary>
    Paused,
    /// <summary>Finished successfully; <see cref="ConversionJob.Result"/> is set.</summary>
    Completed,
    /// <summary>Finished with an error; <see cref="ConversionJob.Error"/> is set.</summary>
    Failed,
    /// <summary>Cancelled by the user or by shutdown.</summary>
    Cancelled,
}

/// <summary>What changed about a job in a <see cref="IJobQueue.JobChanged"/> notification.</summary>
public enum JobChangeKind
{
    /// <summary>The job was added to the queue.</summary>
    Added = 0,
    /// <summary><see cref="ConversionJob.State"/> changed (including final states).</summary>
    StateChanged,
    /// <summary><see cref="ConversionJob.Progress"/> changed. Throttled; phase changes are always reported.</summary>
    Progress,
    /// <summary>Output path or estimate was resolved while the job is starting.</summary>
    Details,
    /// <summary>The job was removed from the queue.</summary>
    Removed,
}

/// <summary>Payload of <see cref="IJobQueue.JobChanged"/>.</summary>
public sealed class JobChangedEventArgs : EventArgs
{
    public JobChangedEventArgs(ConversionJob job, JobChangeKind changeKind)
    {
        Job = job;
        ChangeKind = changeKind;
    }

    public ConversionJob Job { get; }

    public JobChangeKind ChangeKind { get; }
}

internal static class JobStateExtensions
{
    public static bool IsFinished(this JobState state) =>
        state is JobState.Completed or JobState.Failed or JobState.Cancelled;
}
