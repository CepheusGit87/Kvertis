namespace Kvertis.Engine.Abstractions;

/// <summary>A request to run an external tool (ffmpeg, ffprobe). Arguments are passed as a list, never as a shell string.</summary>
public sealed record ProcessRequest(
    string ExecutablePath,
    IReadOnlyList<string> Arguments,
    TimeSpan Timeout,
    string? WorkingDirectory = null)
{
    /// <summary>When true, stdout is captured fully (ffprobe JSON). When false, only stderr lines are streamed (ffmpeg progress).</summary>
    public bool CaptureStdout { get; init; } = true;
}

public sealed record ProcessOutcome(int ExitCode, string StandardOutput, string StandardErrorTail, TimeSpan Elapsed, bool TimedOut)
{
    public bool Succeeded => ExitCode == 0 && !TimedOut;
}

/// <summary>
/// Runs external processes with timeout and cancellation. The only place in the engine that touches
/// System.Diagnostics.Process, so tests can mock it and pause can suspend it.
/// </summary>
public interface IProcessRunner
{
    /// <param name="stderrLines">Receives stderr line by line while the process runs (ffmpeg -progress).</param>
    Task<ProcessOutcome> RunAsync(ProcessRequest request, IProgress<string>? stderrLines, CancellationToken ct);
}

/// <summary>
/// Windows-only ability to freeze/resume a running child process (pause of ffmpeg jobs).
/// The platform-neutral default returns false so callers fall back to cancel-and-restart.
/// </summary>
public interface IProcessSuspender
{
    bool TrySuspend(int processId);
    bool TryResume(int processId);
}

/// <summary>
/// Which system codecs are available. On Windows this probes Media Foundation encoders and the
/// HEIF/HEVC extensions; elsewhere everything is false. Used to hide formats the machine cannot produce.
/// </summary>
public interface ISystemCodecCapabilities
{
    bool CanEncodeH264 { get; }
    bool CanEncodeHevc { get; }
    bool CanEncodeAac { get; }
    bool CanDecodeHevc { get; }
    bool CanDecodeHeif { get; }
}

/// <summary>
/// Image codecs of the operating system (on Windows: the Windows Imaging Component with the installed
/// image extensions). Covers everything the bundled image library (SkiaSharp) cannot or must not do:
/// HEIC/HEIF and AVIF (Kvertis never ships an HEVC or AV1 decoder, ADR-006), camera RAW other than
/// DNG, TIFF (multi-page) and the TIFF/BMP/GIF encoders. The platform-neutral default reports nothing
/// available; converters then fail with <see cref="ConversionErrorCode.MissingSystemCodec"/>.
/// </summary>
public interface ISystemImageCodec
{
    /// <summary>False when the platform has no system image codecs at all.</summary>
    bool IsAvailable { get; }

    /// <summary>Whether a decoder for this format is (believed to be) installed. RAW may only be known after a decode attempt.</summary>
    bool CanDecode(FormatId format);

    /// <summary>Whether an encoder for this format exists (TIFF, BMP, GIF, PNG, JPG on Windows).</summary>
    bool CanEncode(FormatId format);

    /// <summary>
    /// Decodes up to <paramref name="maxFrames"/> frames (TIFF pages, GIF frames) into PNG files inside
    /// <paramref name="outputDirectory"/>. EXIF orientation is applied, colors are converted to sRGB, no
    /// metadata is written. The caller has verified the format by magic bytes and owns (deletes) the files.
    /// Throws ConversionException(MissingSystemCodec) when no decoder is installed, CorruptFile for bad data.
    /// </summary>
    Task<IReadOnlyList<string>> DecodeToPngFramesAsync(string path, string outputDirectory, int maxFrames, CancellationToken ct);

    /// <summary>
    /// Encodes the PNG at <paramref name="pngPath"/> into <paramref name="outputPath"/> as
    /// <paramref name="format"/>. <paramref name="quality"/> (0..100) is used by lossy encoders only.
    /// Writes no metadata. Throws ConversionException(MissingSystemCodec) when no encoder exists.
    /// </summary>
    Task EncodeFromPngAsync(string pngPath, string outputPath, FormatId format, int quality, CancellationToken ct);
}

/// <summary>Location of the bundled or user-provided ffmpeg/ffprobe binaries.</summary>
public interface IFfmpegLocator
{
    string? FfmpegPath { get; }
    string? FfprobePath { get; }
    bool IsAvailable => FfmpegPath is not null && FfprobePath is not null;
}
