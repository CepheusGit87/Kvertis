using Kvertis.Engine.Abstractions;

namespace Kvertis.Engine.Ffmpeg;

/// <summary>Turns a failed ffmpeg/ffprobe run into a <see cref="ConversionException"/> with the most specific code.</summary>
public static class FfmpegErrorMapper
{
    public static ConversionException Map(ProcessOutcome outcome, string? filePath, string step)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        var stderr = outcome.StandardErrorTail ?? string.Empty;

        if (outcome.TimedOut)
        {
            return new ConversionException(ConversionErrorCode.Timeout, filePath, step, stderr);
        }

        return new ConversionException(Classify(stderr), filePath, step, stderr);
    }

    /// <summary>Maps well-known ffmpeg stderr fragments to an error code; falls back to ToolFailed.</summary>
    public static ConversionErrorCode Classify(string stderr)
    {
        ArgumentNullException.ThrowIfNull(stderr);
        if (stderr.Contains("No space left", StringComparison.OrdinalIgnoreCase))
        {
            return ConversionErrorCode.InsufficientDiskSpace;
        }
        if (stderr.Contains("DRM", StringComparison.Ordinal) || stderr.Contains("encrypted", StringComparison.OrdinalIgnoreCase))
        {
            return ConversionErrorCode.ProtectedFile;
        }
        if (stderr.Contains("Unknown encoder", StringComparison.OrdinalIgnoreCase)
            || stderr.Contains("Encoder not found", StringComparison.OrdinalIgnoreCase))
        {
            return ConversionErrorCode.MissingSystemCodec;
        }
        if (stderr.Contains("Invalid data found when processing input", StringComparison.OrdinalIgnoreCase)
            || stderr.Contains("moov atom not found", StringComparison.OrdinalIgnoreCase))
        {
            return ConversionErrorCode.CorruptFile;
        }
        return ConversionErrorCode.ToolFailed;
    }
}
