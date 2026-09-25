using Kvertis.Engine.Abstractions;
using Kvertis.Queue;

namespace Kvertis.App.Services;

/// <summary>
/// One file that step 1 detected. <paramref name="ThumbnailPath"/> is the source file itself for images and
/// videos (the view decodes it) and null otherwise; <paramref name="RejectedWith"/> is not
/// <see cref="ConversionErrorCode.None"/> when the file cannot be converted at all.
/// </summary>
public sealed record StagedFile(InputInfo Input, string? ThumbnailPath, ConversionErrorCode RejectedWith = ConversionErrorCode.None)
{
    public bool IsUsable => RejectedWith == ConversionErrorCode.None && Input.Kind != MediaKind.Unknown;
}

/// <summary>One file of the plan: everything step 3 needs to build a <see cref="ConversionJob"/>.</summary>
public sealed record PlannedConversion(InputInfo Input, ConversionSettings Settings, string NamePattern);

/// <summary>A file left out of the plan and why. Reason keys are the ones of the freemium policy.</summary>
public sealed record SkippedFile(InputInfo Input, string Reason);

/// <summary>The result of step 2 (ADR-020). Pure data; the queue never sees it.</summary>
public sealed record TargetPlan(IReadOnlyList<PlannedConversion> Items, IReadOnlyList<SkippedFile> Skipped)
{
    public static readonly TargetPlan Empty = new([], []);
}

/// <summary>
/// State that travels through the three steps (ADR-020). One instance per window; "Neue Runde" resets it.
/// Every setter raises <see cref="Changed"/> so the step header and the pages can react.
/// </summary>
public interface IWorkflowSession
{
    /// <summary>Step 1: the detected inputs, in the order the user added them.</summary>
    IReadOnlyList<StagedFile> Staged { get; }

    /// <summary>Step 2 result, null until the user continued from the target page.</summary>
    TargetPlan? Plan { get; set; }

    /// <summary>
    /// Step 3: the location that holds for every file. Null until it was chosen in this round; the convert
    /// page then takes the default from the settings (ADR-021).
    /// </summary>
    OutputLocation? Location { get; set; }

    /// <summary>Step 3: per-file exceptions ("Ändern"), keyed by <c>InputInfo.Path</c>. Empty by default.</summary>
    IReadOnlyDictionary<string, OutputLocation> OwnLocations { get; }

    /// <summary>Set by "Anpassen aus dem Verlauf", cleared by <see cref="Reset"/>.</summary>
    HistoryEntry? Previous { get; set; }

    event EventHandler? Changed;

    /// <summary>Sets or (with null) removes the exception for one file. Raises <see cref="Changed"/>.</summary>
    void SetOwnLocation(string inputPath, OutputLocation? location);

    /// <summary>Replaces the staged files. Step 1 calls it whenever its own list changed.</summary>
    void SetStaged(IReadOnlyList<StagedFile> files);

    void Reset();
}

/// <inheritdoc cref="IWorkflowSession"/>
public sealed class WorkflowSession : IWorkflowSession
{
    private readonly Dictionary<string, OutputLocation> _ownLocations = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<StagedFile> _staged = [];
    private TargetPlan? _plan;
    private OutputLocation? _location;
    private HistoryEntry? _previous;

    public IReadOnlyList<StagedFile> Staged => _staged;

    public TargetPlan? Plan
    {
        get => _plan;
        set
        {
            _plan = value;
            Raise();
        }
    }

    public OutputLocation? Location
    {
        get => _location;
        set
        {
            _location = value;
            Raise();
        }
    }

    public IReadOnlyDictionary<string, OutputLocation> OwnLocations => _ownLocations;

    public HistoryEntry? Previous
    {
        get => _previous;
        set
        {
            _previous = value;
            Raise();
        }
    }

    public event EventHandler? Changed;

    public void SetStaged(IReadOnlyList<StagedFile> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        _staged = files;
        Raise();
    }

    public void SetOwnLocation(string inputPath, OutputLocation? location)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);
        if (location is null)
        {
            _ownLocations.Remove(inputPath);
        }
        else
        {
            _ownLocations[inputPath] = location;
        }
        Raise();
    }

    public void Reset()
    {
        _staged = [];
        _plan = null;
        _previous = null;
        _location = null;
        _ownLocations.Clear();
        Raise();
    }

    private void Raise() => Changed?.Invoke(this, EventArgs.Empty);
}
