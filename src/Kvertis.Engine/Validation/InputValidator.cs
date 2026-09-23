using Kvertis.Engine.Abstractions;

namespace Kvertis.Engine.Validation;

/// <summary>Size limits and timeouts per media kind (docs/05-formate.md). Adjustable from settings.</summary>
public sealed record InputLimits(
    long MaxImageBytes = 500L * 1024 * 1024,
    long MaxAudioBytes = 2L * 1024 * 1024 * 1024,
    long MaxVideoBytes = 20L * 1024 * 1024 * 1024,
    long MaxDocumentBytes = 500L * 1024 * 1024)
{
    public static readonly InputLimits Default = new();

    public long MaxBytesFor(MediaKind kind) => kind switch
    {
        MediaKind.Image => MaxImageBytes,
        MediaKind.Audio => MaxAudioBytes,
        MediaKind.Video => MaxVideoBytes,
        MediaKind.Document => MaxDocumentBytes,
        _ => MaxImageBytes,
    };

    public static TimeSpan AnalysisTimeoutFor(MediaKind kind) => kind switch
    {
        MediaKind.Image => TimeSpan.FromSeconds(10),
        MediaKind.Audio => TimeSpan.FromSeconds(15),
        MediaKind.Video => TimeSpan.FromSeconds(30),
        MediaKind.Document => TimeSpan.FromSeconds(15),
        _ => TimeSpan.FromSeconds(10),
    };

    public static TimeSpan ConversionTimeoutFor(MediaKind kind) => kind switch
    {
        MediaKind.Image => TimeSpan.FromMinutes(5),
        MediaKind.Audio => TimeSpan.FromMinutes(30),
        MediaKind.Video => TimeSpan.FromHours(6),
        MediaKind.Document => TimeSpan.FromMinutes(10),
        _ => TimeSpan.FromMinutes(5),
    };
}

/// <summary>
/// Gate before any file reaches ffmpeg, the image library or a document library. Rejects unreadable,
/// oversized and protected files; large-but-allowed files only get a warning (the UI asks).
/// Protection detection itself lives in the probers, which set the error via ConversionException.
/// </summary>
public sealed class InputValidator : IInputValidator
{
    /// <summary>Hard multiplier above the soft limit: beyond this the file is rejected outright.</summary>
    private const int HardLimitFactor = 4;

    private readonly InputLimits _limits;

    public InputValidator(InputLimits? limits = null)
    {
        _limits = limits ?? InputLimits.Default;
    }

    public Task<ValidationOutcome> ValidateAsync(InputInfo info, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(info);
        ct.ThrowIfCancellationRequested();

        if (!File.Exists(info.Path))
        {
            return Task.FromResult(ValidationOutcome.Rejected(ConversionErrorCode.InputNotReadable));
        }
        if (info.SizeBytes <= 0)
        {
            return Task.FromResult(ValidationOutcome.Rejected(ConversionErrorCode.CorruptFile, "empty file"));
        }
        if (info.Kind == MediaKind.Unknown)
        {
            return Task.FromResult(ValidationOutcome.Rejected(ConversionErrorCode.UnsupportedFormat));
        }

        var soft = _limits.MaxBytesFor(info.Kind);
        if (info.SizeBytes > soft * HardLimitFactor)
        {
            return Task.FromResult(ValidationOutcome.Rejected(ConversionErrorCode.FileTooLarge, $"{info.SizeBytes} > {soft * HardLimitFactor}"));
        }

        return Task.FromResult(ValidationOutcome.Ok);
    }

    /// <summary>Adds the LargeFile warning when the soft limit is exceeded. Called by the detector pipeline.</summary>
    public InputInfo Annotate(InputInfo info) =>
        info.SizeBytes > _limits.MaxBytesFor(info.Kind) ? info.WithWarning(InputWarning.LargeFile) : info;
}
