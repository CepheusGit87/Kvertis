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
        // The system transcoder shares the cache and uses this probe when detection left no entry.
        cache.AttachProber(GetMediaInfoAsync);
    }

    public IFfmpegLocator Locator { get; }
    public IProcessRunner Runner { get; }
    public ISystemCodecCapabilities Codecs { get; }
    public IFfmpegFeatures Features { get; }
    public FfmpegCompliance Compliance { get; }
    public FfprobeReader Reader { get; }
    public MediaInfoCache Cache { get; }
    public FormatRegistry Registry { get; }

    /// <summary>
    /// Fails with ToolMissing when ffmpeg is absent or not compliant; returns features and probe data.
    /// Probes first and refuses inputs that need system decoding (<see cref="EnsureNoSystemDecoding"/>)
    /// before ffmpeg itself is started for anything, including the compliance check.
    /// </summary>
    public async Task<FfmpegJobContext> PrepareAsync(InputInfo input, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(input);
        var ffmpeg = Locator.FfmpegPath
                     ?? throw new ConversionException(ConversionErrorCode.ToolMissing, input.Path, "prepare", "ffmpeg not found");
        var media = await GetMediaInfoAsync(input, ct).ConfigureAwait(false);
        if (media is { IsEncrypted: true })
        {
            throw new ConversionException(ConversionErrorCode.ProtectedFile, input.Path, "prepare", "encrypted stream");
        }
        EnsureNoSystemDecoding(input, media);
        await Compliance.EnsureCompliantAsync(ct).ConfigureAwait(false);
        var features = await Features.GetAsync(ct).ConfigureAwait(false);
        return new FfmpegJobContext(ffmpeg, features, media);
    }

    /// <summary>
    /// ADR-015: inputs with a patent-encumbered stream never reach ffmpeg (no decode, no stream copy, no
    /// sound-track extraction). Without probe data (ffprobe missing or failed) ffmpeg may only open containers
    /// that cannot carry such streams (<see cref="EncumberedCodecs.IsPatentFreeContainer"/>: WebM, OGG, Opus,
    /// FLAC, WAV, AIFF, MP3); MKV and every other container are refused because their codecs are unknown.
    /// Throws UnsupportedFormat "requires system decoding".
    /// </summary>
    public static void EnsureNoSystemDecoding(InputInfo input, MediaInfo? media)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!MayDecodeWithFfmpeg(input, media))
        {
            throw new ConversionException(ConversionErrorCode.UnsupportedFormat, input.Path, "prepare", RequiresSystemDecodingDetail);
        }
    }

    /// <summary>
    /// The ADR-015 decoder rule as a predicate: with probe data, no encumbered stream; without probe data, a
    /// patent-free container only.
    /// </summary>
    public static bool MayDecodeWithFfmpeg(InputInfo input, MediaInfo? media)
    {
        ArgumentNullException.ThrowIfNull(input);
        return media is null ? EncumberedCodecs.IsPatentFreeContainer(input.Format) : !media.RequiresSystemDecoding;
    }

    /// <summary>Error detail for inputs that only Media Foundation may decode.</summary>
    public const string RequiresSystemDecodingDetail = "requires system decoding";

    /// <summary>
    /// True when cached probe data says the input needs system decoding. Used by the converters' synchronous
    /// <c>Supports</c>; without cached data it returns false and <see cref="PrepareAsync"/> re-checks after probing.
    /// </summary>
    public bool IsKnownToRequireSystemDecoding(InputInfo input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return Cache.TryGet(input.Path, out var media) && media.RequiresSystemDecoding;
    }

    /// <summary>
    /// Routing check for the converters' synchronous <c>Supports</c>: <see cref="MayDecodeWithFfmpeg"/> on the
    /// cached probe data. Without cached data only patent-free containers qualify; the job runner re-probes
    /// before resolving, so an evicted entry does not misroute a job.
    /// </summary>
    public bool MayUseFfmpeg(InputInfo input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return MayDecodeWithFfmpeg(input, Cache.TryGet(input.Path, out var media) ? media : null);
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

    /// <summary>Runs ffmpeg with the conversion timeout of the input's kind and maps failures to error codes.</summary>
    public async Task RunFfmpegAsync(string ffmpegPath, IReadOnlyList<string> arguments, InputInfo input, IProgress<string>? stderrLines, string step, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(arguments);
        var request = new ProcessRequest(ffmpegPath, arguments, InputLimits.ConversionTimeoutFor(input.Kind)) { CaptureStdout = false };
        var outcome = await Runner.RunAsync(request, stderrLines, ct).ConfigureAwait(false);
        if (!outcome.Succeeded)
        {
            throw FfmpegErrorMapper.Map(outcome, input.Path, step);
        }
    }

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
            await RunFfmpegAsync(context.FfmpegPath, arguments, input, parser, "convert", ct).ConfigureAwait(false);

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
