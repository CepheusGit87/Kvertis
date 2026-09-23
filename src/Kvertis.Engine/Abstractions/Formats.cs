namespace Kvertis.Engine.Abstractions;

/// <summary>Coarse media category. Drives which converters and limits apply.</summary>
public enum MediaKind
{
    Unknown = 0,
    Image,
    Audio,
    Video,
    Document,
    /// <summary>3D models (meshes): STL, OBJ, PLY, 3MF, glTF/GLB. Own parsers only (ADR-016).</summary>
    Model3D,
}

/// <summary>
/// Stable, lowercase identifier of a format ("png", "mp4", "pdf").
/// Used as a key in the registry, in settings and in the history file. Never shown to users directly.
/// </summary>
public readonly record struct FormatId
{
    public string Id { get; }

    public FormatId(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        Id = id.Trim().ToLowerInvariant();
    }

    public override string ToString() => Id;

    public static implicit operator string(FormatId format) => format.Id;

    public static FormatId Parse(string id) => new(id);
}

/// <summary>Non-fatal findings about an input file that the UI should tell the user about.</summary>
public enum InputWarning
{
    /// <summary>The file's extension does not match its detected content.</summary>
    ExtensionMismatch,
    /// <summary>Variable frame rate video; the converter will force a constant frame rate.</summary>
    VariableFrameRate,
    /// <summary>Interlaced video source.</summary>
    Interlaced,
    /// <summary>Animated image; only the first frame is converted in phase 1.</summary>
    AnimationDropped,
    /// <summary>Source has transparency; the target format cannot store it.</summary>
    TransparencyLost,
    /// <summary>Multiple audio tracks; only the first is used in phase 1.</summary>
    MultipleAudioTracks,
    /// <summary>Duration could not be read from metadata; estimates use file size.</summary>
    DurationUnknown,
    /// <summary>The file exceeds the soft size limit for its category; the UI asks before continuing.</summary>
    LargeFile,
    /// <summary>
    /// The input is converted by the system transcoder, which cannot remove container metadata (location, camera
    /// data) even when metadata stripping is on.
    /// </summary>
    MetadataNotStrippable,
}

/// <summary>Everything the engine learned about an input file. Immutable.</summary>
public sealed record InputInfo(
    string Path,
    FormatId Format,
    MediaKind Kind,
    long SizeBytes,
    TimeSpan? Duration,
    int? Width,
    int? Height,
    int? PageCount,
    IReadOnlyList<InputWarning> Warnings)
{
    public bool HasWarning(InputWarning warning) => Warnings.Contains(warning);

    public InputInfo WithWarning(InputWarning warning) =>
        Warnings.Contains(warning) ? this : this with { Warnings = [.. Warnings, warning] };
}
