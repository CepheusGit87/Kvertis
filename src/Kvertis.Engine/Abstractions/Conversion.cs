namespace Kvertis.Engine.Abstractions;

/// <summary>What happens to EXIF/GPS/XMP metadata. ICC profiles are always kept so colors stay correct.</summary>
public enum MetadataPolicy
{
    /// <summary>Default. Removes EXIF, GPS, XMP, comments.</summary>
    Strip = 0,
    Keep,
}

/// <summary>Named target presets shown to lay users. No third-party product names, ever.</summary>
public enum ConversionPreset
{
    None = 0,
    Messenger,
    Email,
    SocialMedia,
    Website,
    Archive,
}

/// <summary>
/// User-facing settings for one conversion. Quality is a 0..100 slider that each converter maps to
/// its own parameters. TargetSizeBytes, when set, overrides Quality: the converter searches for the
/// best quality that fits.
/// </summary>
public sealed record ConversionSettings(
    FormatId Output,
    int Quality = 80,
    long? TargetSizeBytes = null,
    MetadataPolicy Metadata = MetadataPolicy.Strip,
    ConversionPreset Preset = ConversionPreset.None,
    IReadOnlyDictionary<string, string>? Advanced = null)
{
    public static class AdvancedKeys
    {
        /// <summary>Longest edge in pixels for images / video height for video ("1080"). "0" = keep.</summary>
        public const string MaxDimension = "maxDimension";
        /// <summary>Audio bitrate in kbit/s ("128").</summary>
        public const string AudioBitrateKbps = "audioBitrateKbps";
        /// <summary>Audio sample rate in Hz ("44100").</summary>
        public const string SampleRateHz = "sampleRateHz";
        /// <summary>"true" to deinterlace video.</summary>
        public const string Deinterlace = "deinterlace";
        /// <summary>Video frame rate ("30"). Empty = keep.</summary>
        public const string FrameRate = "frameRate";
        /// <summary>Page size for text-to-PDF ("A4", "Letter").</summary>
        public const string PageSize = "pageSize";
        /// <summary>Raster DPI for PDF-to-image ("150").</summary>
        public const string Dpi = "dpi";
    }

    public string? GetAdvanced(string key) =>
        Advanced is not null && Advanced.TryGetValue(key, out var v) ? v : null;

    public int? GetAdvancedInt(string key) =>
        int.TryParse(GetAdvanced(key), out var v) ? v : null;

    public bool GetAdvancedBool(string key) =>
        string.Equals(GetAdvanced(key), "true", StringComparison.OrdinalIgnoreCase);

    public int QualityClamped => Math.Clamp(Quality, 0, 100);
}

public enum ConversionPhase
{
    Queued = 0,
    Analyzing,
    Converting,
    Optimizing,
    Finalizing,
    Done,
}

/// <summary>Progress report. Fraction is 0..1 across the whole job.</summary>
public sealed record ConversionProgress(double Fraction, ConversionPhase Phase, TimeSpan? Remaining = null)
{
    public static readonly ConversionProgress Start = new(0, ConversionPhase.Analyzing);
    public static readonly ConversionProgress Complete = new(1, ConversionPhase.Done, TimeSpan.Zero);
}

public sealed record ConversionResult(
    string OutputPath,
    long InputBytes,
    long OutputBytes,
    TimeSpan Elapsed)
{
    public double SizeRatio => InputBytes == 0 ? 1 : (double)OutputBytes / InputBytes;
}

/// <summary>
/// A preview produced before the real conversion: a temporary output file (reduced, or a short excerpt)
/// plus the estimated final size. The caller deletes the file when done.
/// </summary>
public sealed record PreviewResult(string PreviewPath, long EstimatedOutputBytes, TimeSpan? ExcerptDuration = null);

/// <summary>A converter turns one input into one output file of a given format.</summary>
public interface IConverter
{
    /// <summary>Short stable name for logs and speed profiles ("image", "audio", "video", "pdf", "office", "text").</summary>
    string Name { get; }

    bool Supports(InputInfo input, FormatId output);

    /// <summary>
    /// Converts <paramref name="input"/> into <paramref name="outputPath"/>. The caller guarantees
    /// the directory exists and the path is free. Implementations write to a temporary file next to
    /// the target and rename on success (see ConversionOutput helper).
    /// </summary>
    Task<ConversionResult> ConvertAsync(
        InputInfo input,
        string outputPath,
        ConversionSettings settings,
        IProgress<ConversionProgress> progress,
        CancellationToken ct);

    /// <summary>Optional. Returns null when the converter cannot preview this combination.</summary>
    Task<PreviewResult?> PreviewAsync(InputInfo input, ConversionSettings settings, CancellationToken ct);
}

/// <summary>Detects format and basic properties of a file from its content, never from its extension alone.</summary>
public interface IFormatDetector
{
    Task<InputInfo> DetectAsync(string path, CancellationToken ct);
}

/// <summary>
/// Optional deep probe for one media kind (ffprobe for audio/video, image header reader, PDF reader).
/// Returns the enriched info, or the input unchanged when it cannot help. Must never throw for
/// corrupt files; throw ConversionException(CorruptFile/ProtectedFile) only when certain.
/// </summary>
public interface IMediaProber
{
    bool Supports(MediaKind kind);
    Task<InputInfo> ProbeAsync(InputInfo info, CancellationToken ct);
}

public sealed record ValidationOutcome(bool IsAccepted, ConversionErrorCode Error, string? Detail = null)
{
    public static readonly ValidationOutcome Ok = new(true, ConversionErrorCode.None);
    public static ValidationOutcome Rejected(ConversionErrorCode error, string? detail = null) => new(false, error, detail);
}

public interface IInputValidator
{
    Task<ValidationOutcome> ValidateAsync(InputInfo info, CancellationToken ct);
}

public sealed record Estimate(TimeSpan Duration, long OutputBytes, double Confidence)
{
    public static readonly Estimate Unknown = new(TimeSpan.Zero, 0, 0);
}

public interface IEstimator
{
    Estimate Estimate(InputInfo input, ConversionSettings settings);
    void Record(InputInfo input, ConversionSettings settings, ConversionResult result);
}

/// <summary>Resolves which converter handles a job.</summary>
public interface IConverterResolver
{
    IConverter? Resolve(InputInfo input, FormatId output);
    IReadOnlyList<IConverter> All { get; }
}
