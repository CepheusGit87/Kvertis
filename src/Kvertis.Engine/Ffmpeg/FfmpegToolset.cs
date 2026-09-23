using System.Diagnostics;
using Kvertis.Engine.Abstractions;
using Kvertis.Engine.Formats;
using Kvertis.Engine.IO;
using Kvertis.Engine.Probing;
using Kvertis.Engine.Validation;

namespace Kvertis.Engine.Ffmpeg;

/// <summary>Everything an ffmpeg-based converter needs to know before it starts the process.</summary>
public sealed record FfmpegJobContext(string FfmpegPath, FfmpegFeatures Features, MediaInfo? Media);

/// <summary>
/// Shared plumbing of the audio and video converters: locating ffmpeg, the one-time license check,
/// the feature probe, cached ffprobe data, and running ffmpeg with progress and error mapping.
/// Register as singleton.
/// </summary>
public sealed class FfmpegToolset
{
    public FfmpegToolset(
        IFfmpegLocator locator,
        IProcessRunner runner,
        ISystemCodecCapabilities codecs,
        IFfmpegFeatures features,
        FfmpegCompliance compliance,
        FfprobeReader reader,
        MediaInfoCache cache,
        FormatRegistry registry)
    {
        Locator = locator;
        Runner = runner;
        Codecs = codecs;
        Features = features;
        Compliance = compliance;
        Reader = reader;
        Cache = cache;
        Registry = registry;
    }

    public IFfmpegLocator Locator { get; }
    public IProcessRunner Runner { get; }
    public ISystemCodecCapabilities Codecs { get; }
    public IFfmpegFeatures Features { get; }
    public FfmpegCompliance Compliance { get; }
    public FfprobeReader Reader { get; }
    public MediaInfoCache Cache { get; }
    public FormatRegistry Registry { get; }

    /// <summary>Fails with ToolMissing when ffmpeg is absent or not compliant; returns features and probe data.</summary>
    public async Task<FfmpegJobContext> PrepareAsync(InputInfo input, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(input);
        var ffmpeg = Locator.FfmpegPath
                     ?? throw new ConversionException(ConversionErrorCode.ToolMissing, input.Path, "prepare", "ffmpeg not found");
        await Compliance.EnsureCompliantAsync(ct).ConfigureAwait(false);
        var features = await Features.GetAsync(ct).ConfigureAwait(false);
        var media = await GetMediaInfoAsync(input, ct).ConfigureAwait(false);
        if (media is { IsEncrypted: true })
        {
            throw new ConversionException(ConversionErrorCode.ProtectedFile, input.Path, "prepare", "encrypted stream");
        }
        return new FfmpegJobContext(ffmpeg, features, media);
    }

    /// <summary>Cached ffprobe data, or a fresh probe; null when ffprobe cannot read the file.</summary>
    public async Task<MediaInfo?> GetMediaInfoAsync(InputInfo input, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (Cache.TryGet(input.Path, out var cached))
        {
            return cached;
        }
        if (Locator.FfprobePath is null)
        {
            return null;
        }
        try
        {
            var media = await Reader.ReadAsync(input.Path, input.Kind, ct).ConfigureAwait(false);
            Cache.Set(input.Path, media);
            return media;
        }
        catch (ConversionException ex) when (ex.Code is ConversionErrorCode.ProtectedFile or ConversionErrorCode.Cancelled)
        {
            throw;
        }
        catch (ConversionException)
        {
            return null; // ffmpeg itself will report what is wrong with the file.
        }
    }

    /// <summary>
    /// Runs ffmpeg with the conversion timeout of the input's kind and maps failures to error codes.
    /// With <paramref name="media"/> describing an HEVC source decoded through the system (<c>-hwaccel</c>),
    /// stderr is watched for ffmpeg's silent software fallback, which is refused (ADR-003) with
    /// MissingSystemCodec "hevc software fallback refused".
    /// </summary>
    public async Task RunFfmpegAsync(string ffmpegPath, IReadOnlyList<string> arguments, InputInfo input, IProgress<string>? stderrLines, string step, CancellationToken ct, MediaInfo? media = null)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(arguments);
        var request = new ProcessRequest(ffmpegPath, arguments, InputLimits.ConversionTimeoutFor(input.Kind)) { CaptureStdout = false };

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var guard = NeedsHevcGuard(media, arguments) ? new HevcFallbackGuard(stderrLines, linked) : null;
        ProcessOutcome outcome;
        try
        {
            outcome = await Runner.RunAsync(request, (IProgress<string>?)guard ?? stderrLines, linked.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (guard is { Triggered: true } && !ct.IsCancellationRequested
                                   && ex is OperationCanceledException or ConversionException { Code: ConversionErrorCode.Cancelled })
        {
            throw HevcFallbackGuard.Refused(input.Path, step);
        }
        if (guard is { Triggered: true })
        {
            throw HevcFallbackGuard.Refused(input.Path, step);
        }
        if (!outcome.Succeeded)
        {
            throw FfmpegErrorMapper.Map(outcome, input.Path, step);
        }
    }

    /// <summary>True when the run decodes an HEVC source through D3D11VA and must not fall back to software.</summary>
    internal static bool NeedsHevcGuard(MediaInfo? media, IReadOnlyList<string> arguments) =>
        media is { IsHevc: true } && arguments.Contains("-hwaccel");

    /// <summary>
    /// Full conversion pipeline: prepare, build arguments, write to the temp file, commit.
    /// <paramref name="buildArguments"/> receives the temp path and must not start anything.
    /// </summary>
    internal async Task<ConversionResult> ConvertAsync(
        InputInfo input,
        string outputPath,
        ConversionSettings settings,
        IProgress<ConversionProgress> progress,
        Func<FfmpegJobContext, string, IReadOnlyList<string>> buildArguments,
        CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            progress.Report(ConversionProgress.Start);
            var context = await PrepareAsync(input, ct).ConfigureAwait(false);

            // Build first: codec and target-size errors surface before any file or process is touched.
            var tempPath = outputPath + ConversionOutput.TempSuffix;
            var arguments = buildArguments(context, tempPath);

            var expected = FfmpegArguments.EstimateOutputBytes(input, context.Media, settings, Registry);
            using var output = ConversionOutput.Begin(outputPath, expected);
            Debug.Assert(output.TempPath == tempPath, "temp path convention changed");

            progress.Report(new ConversionProgress(FfmpegProgressParser.ConvertStart, ConversionPhase.Converting));
            var parser = new FfmpegProgressParser(context.Media?.Duration ?? input.Duration, progress);
            await RunFfmpegAsync(context.FfmpegPath, arguments, input, parser, "convert", ct, context.Media).ConfigureAwait(false);

            progress.Report(new ConversionProgress(FfmpegProgressParser.ConvertEnd, ConversionPhase.Finalizing));
            var bytes = output.Commit();
            progress.Report(ConversionProgress.Complete);
            return new ConversionResult(outputPath, input.SizeBytes, bytes, stopwatch.Elapsed);
        }
        catch (Exception ex)
        {
            throw ConversionException.From(ex, input.Path, "convert");
        }
    }

    /// <summary>A unique temp file for previews; the caller of PreviewAsync deletes it.</summary>
    internal static string NewPreviewPath(string extension) =>
        Path.Combine(Path.GetTempPath(), "kvertis-preview-" + Guid.NewGuid().ToString("N") + "." + extension);

    internal static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception)
        {
            // Best effort.
        }
    }
}
