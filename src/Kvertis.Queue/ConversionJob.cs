using Kvertis.Engine.Abstractions;
using Kvertis.Engine.Naming;

namespace Kvertis.Queue;

/// <summary>
/// One file to convert: immutable request data plus status that only the queue changes.
/// All getters are thread-safe; the UI reads them after marshalling a <see cref="IJobQueue.JobChanged"/> event.
/// </summary>
public sealed class ConversionJob
{
    private readonly object _sync = new();
    private JobState _state = JobState.Queued;
    private ConversionProgress _progress = new(0, ConversionPhase.Queued);
    private ConversionResult? _result;
    private ConversionErrorCode _error = ConversionErrorCode.None;
    private string? _errorDetail;
    private Estimate _estimate = Estimate.Unknown;
    private DateTimeOffset? _startedAt;
    private DateTimeOffset? _finishedAt;
    private string? _outputPath;

    public ConversionJob(
        InputInfo input,
        ConversionSettings settings,
        string outputDirectory,
        string namePattern = OutputNamePattern.Default,
        int? batchIndex = null,
        DateTimeOffset? createdAt = null)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);

        Id = Guid.NewGuid();
        Input = input;
        Settings = settings;
        OutputDirectory = outputDirectory;
        NamePattern = string.IsNullOrWhiteSpace(namePattern) ? OutputNamePattern.Default : namePattern;
        BatchIndex = batchIndex;
        CreatedAt = createdAt ?? DateTimeOffset.UtcNow;
    }

    public Guid Id { get; }

    public InputInfo Input { get; }

    public ConversionSettings Settings { get; }

    public string OutputDirectory { get; }

    public string NamePattern { get; }

    public int? BatchIndex { get; }

    public DateTimeOffset CreatedAt { get; }

    /// <summary>True for video inputs; these also count against <see cref="IJobQueue.MaxParallelVideo"/>.</summary>
    public bool IsVideo => Input.Kind == MediaKind.Video;

    public JobState State
    {
        get { lock (_sync) { return _state; } }
        internal set { lock (_sync) { _state = value; } }
    }

    /// <summary>Last reported progress. Keeps its value while the job is paused.</summary>
    public ConversionProgress Progress
    {
        get { lock (_sync) { return _progress; } }
        internal set { lock (_sync) { _progress = value; } }
    }

    public ConversionResult? Result
    {
        get { lock (_sync) { return _result; } }
        internal set { lock (_sync) { _result = value; } }
    }

    public ConversionErrorCode Error
    {
        get { lock (_sync) { return _error; } }
        internal set { lock (_sync) { _error = value; } }
    }

    /// <summary>Diagnostic detail (tool output tail, exception message). Never shown untranslated as the main message.</summary>
    public string? ErrorDetail
    {
        get { lock (_sync) { return _errorDetail; } }
        internal set { lock (_sync) { _errorDetail = value; } }
    }

    public Estimate Estimate
    {
        get { lock (_sync) { return _estimate; } }
        internal set { lock (_sync) { _estimate = value; } }
    }

    public DateTimeOffset? StartedAt
    {
        get { lock (_sync) { return _startedAt; } }
        internal set { lock (_sync) { _startedAt = value; } }
    }

    public DateTimeOffset? FinishedAt
    {
        get { lock (_sync) { return _finishedAt; } }
        internal set { lock (_sync) { _finishedAt = value; } }
    }

    /// <summary>
    /// Final output path. Resolved when the job starts (<see cref="OutputNamePattern.Render"/> plus
    /// collision numbering "_1", "_2"), null before. Reset to null when a job is re-queued for a restart.
    /// </summary>
    public string? OutputPath
    {
        get { lock (_sync) { return _outputPath; } }
        internal set { lock (_sync) { _outputPath = value; } }
    }

    /// <summary>Creates a fresh job with the same request data and a new id (used by Retry).</summary>
    public ConversionJob CloneAsNew(DateTimeOffset? createdAt = null) =>
        new(Input, Settings, OutputDirectory, NamePattern, BatchIndex, createdAt);

    public override string ToString() => $"{Id:N} {Input.Path} -> {Settings.Output} [{State}]";
}
