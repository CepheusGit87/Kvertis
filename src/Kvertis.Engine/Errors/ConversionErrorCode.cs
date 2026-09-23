namespace Kvertis.Engine.Abstractions;

/// <summary>
/// Every failure the engine can report. The UI maps each code to a localized message with a
/// suggested fix (Strings/*/Resources.resw, keys Error_{Code}_Title / Error_{Code}_Body).
/// Adding a code here requires DE and EN resource entries.
/// </summary>
public enum ConversionErrorCode
{
    None = 0,
    /// <summary>Input format not recognized or no converter for the requested output.</summary>
    UnsupportedFormat,
    /// <summary>File is empty, truncated or fails to parse.</summary>
    CorruptFile,
    /// <summary>Password-protected or DRM-protected input. Never bypassed.</summary>
    ProtectedFile,
    /// <summary>The needed Windows codec/extension (HEVC, HEIF) is not installed.</summary>
    MissingSystemCodec,
    /// <summary>Not enough free space in the target folder for the estimated output.</summary>
    InsufficientDiskSpace,
    /// <summary>Target file exists and overwriting was not allowed.</summary>
    OutputExists,
    /// <summary>Target folder is read-only or the file could not be created.</summary>
    OutputNotWritable,
    /// <summary>Analysis or conversion exceeded its timeout.</summary>
    Timeout,
    /// <summary>Cancelled by the user.</summary>
    Cancelled,
    /// <summary>ffmpeg/ffprobe binary missing or not executable.</summary>
    ToolMissing,
    /// <summary>External tool exited with an error.</summary>
    ToolFailed,
    /// <summary>The requested target size is below what the format can reach.</summary>
    TargetSizeUnreachable,
    /// <summary>Input exceeds the hard size limit for its category.</summary>
    FileTooLarge,
    /// <summary>Input file does not exist or cannot be read.</summary>
    InputNotReadable,
    Unknown,
}
