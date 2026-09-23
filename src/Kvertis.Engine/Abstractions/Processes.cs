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
/// Decodes HEIC/HEIF into an uncompressed image using the operating system's image codecs.
/// Kvertis never ships an HEVC decoder (see ADR-006).
/// </summary>
public interface IHeicDecoder
{
    bool IsAvailable { get; }

    /// <summary>Decodes to a PNG file at <paramref name="outputPngPath"/>. Throws ConversionException(MissingSystemCodec) when unavailable.</summary>
    Task DecodeToPngAsync(string heicPath, string outputPngPath, CancellationToken ct);
}

/// <summary>Location of the bundled or user-provided ffmpeg/ffprobe binaries.</summary>
public interface IFfmpegLocator
{
    string? FfmpegPath { get; }
    string? FfprobePath { get; }
    bool IsAvailable => FfmpegPath is not null && FfprobePath is not null;
}
