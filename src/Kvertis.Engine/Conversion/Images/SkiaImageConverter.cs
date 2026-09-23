using System.Diagnostics;
using Kvertis.Engine.Abstractions;
using Kvertis.Engine.Conversion.Documents;
using Kvertis.Engine.Formats;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace Kvertis.Engine.Conversion.Images;

/// <summary>
/// Image → image conversion on SkiaSharp plus the operating system's image codecs (ADR-006).
/// <list type="bullet">
/// <item>Decode: Skia reads JPG, PNG, WebP, GIF, BMP, ICO and DNG. HEIC, AVIF, TIFF and the other RAW
/// variants are decoded by <see cref="ISystemImageCodec"/> into temporary PNGs first; Kvertis ships no
/// HEVC/AV1 decoder. Animated GIF/WebP: first frame. Multi-page TIFF: one output per page.</item>
/// <item>Pipeline: EXIF orientation baked into the pixels, embedded ICC profile converted to sRGB (baked in,
/// not embedded), downscale only, alpha flattened on white for outputs without transparency.</item>
/// <item>Encode: JPG, PNG, WebP by Skia; ICO by <see cref="IcoWriter"/> (PNG inside); TIFF, BMP, GIF by the
/// system encoder (<see cref="ConversionErrorCode.MissingSystemCodec"/> when there is none).</item>
/// <item>Metadata: Skia writes no EXIF/XMP, so Strip is automatic. Keep copies the EXIF segment of a JPEG
/// source into JPG/WebP output (orientation reset to upright). Other sources have nothing Kvertis keeps.</item>
/// </list>
/// Image → PDF is not handled here.
/// </summary>
public sealed partial class ImageConverter : IConverter
{
    /// <summary>Longest edge of preview images.</summary>
    public const int PreviewMaxDimension = 1024;

    /// <summary>Above this pixel count the preview estimates the final size by scaling instead of a full encode.</summary>
    public const long FullEstimateMaxPixels = 24_000_000;

    /// <summary>Most pages taken from a multi-page TIFF (names run from _p001 to _p999).</summary>
    public const int MaxPages = 999;

    internal const int MinSearchQuality = 20;
    internal const int MaxSearchQuality = 95;
    internal const int MaxSearchEncodes = 7;
    internal const int MinScalePercent = 25;

    private readonly FormatRegistry _registry;
    private readonly ISystemImageCodec _systemCodec;
    private readonly ILogger<ImageConverter> _logger;

    public ImageConverter(FormatRegistry registry, ISystemImageCodec systemCodec, ILogger<ImageConverter> logger)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _systemCodec = systemCodec ?? throw new ArgumentNullException(nameof(systemCodec));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string Name => "image";

    /// <summary>
    /// Whether the combination is handled at all. Independent of installed system codecs: a missing
    /// codec is reported as <see cref="ConversionErrorCode.MissingSystemCodec"/> by the conversion.
    /// </summary>
    public bool Supports(InputInfo input, FormatId output)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.Kind != MediaKind.Image || _registry.Get(input.Format) is not { CanRead: true } || !SkiaImaging.IsReadable(input.Format))
        {
            return false;
        }
        return _registry.Get(output) is { CanWrite: true, Kind: MediaKind.Image } && WriterFor(output) is not null;
    }

    /// <summary>
    /// True when the input format can carry an alpha channel and the output cannot (JPG, BMP). The
    /// converter then flattens onto white; the UI uses this to show the TransparencyLost hint.
    /// </summary>
    public static bool WillLoseTransparency(InputInfo input, FormatId output, FormatRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(registry);
        return registry.Get(input.Format)?.SupportsTransparency == true
               && registry.Get(output)?.SupportsTransparency != true;
    }

    public async Task<ConversionResult> ConvertAsync(
        InputInfo input,
        string outputPath,
        ConversionSettings settings,
        IProgress<ConversionProgress> progress,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(progress);
        EnsureSupported(input, settings.Output);

        var stopwatch = Stopwatch.StartNew();
        progress.Report(ConversionProgress.Start);
        Sources? sources = null;
        try
        {
            ct.ThrowIfCancellationRequested();
            var job = ImageJob.From(settings, _registry);
            RequireEncoder(job, input.Path);

            sources = await ResolveSourcesAsync(input, MaxPages, ct).ConfigureAwait(false);
            var count = sources.Frames.Count;
            var expected = ExpectedBytes(input, settings) / count + 1;
            var exif = settings.Metadata == MetadataPolicy.Keep ? ReadSourceExif(input) : null;

            using var outputs = new OutputSet();
            for (var i = 0; i < count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var path = count == 1 ? outputPath : DocumentPaths.UniquePagePath(outputPath, i);
                var frame = sources.Frames[i];
                var frameProgress = new ScaledProgress(progress, (double)i / count, 1.0 / count);
                await outputs.WriteAsync(path, expected, temp => WriteFrameAsync(frame, temp, job, exif, frameProgress, ct)).ConfigureAwait(false);
            }

            ct.ThrowIfCancellationRequested();
            progress.Report(new ConversionProgress(0.95, ConversionPhase.Finalizing));
            var result = outputs.Complete(input, stopwatch);
            progress.Report(ConversionProgress.Complete);
            LogConverted(_logger, input.Format.Id, settings.Output.Id, count, input.SizeBytes, result.OutputBytes, stopwatch.ElapsedMilliseconds);
            return result;
        }
        catch (Exception ex)
        {
            throw Wrap(ex, input.Path, "convert", ct);
        }
        finally
        {
            sources?.Dispose();
        }
    }

    public async Task<PreviewResult?> PreviewAsync(InputInfo input, ConversionSettings settings, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(settings);
        if (!Supports(input, settings.Output))
        {
            return null;
        }
        var job = ImageJob.From(settings, _registry);
        if (job.Writer == Writer.System && !SystemCanEncode(job.Output))
        {
            return null; // nothing to show without the encoder; the conversion reports MissingSystemCodec
        }

        var directory = Path.Combine(Path.GetTempPath(), "Kvertis");
        Sources? sources = null;
        try
        {
            Directory.CreateDirectory(directory);
            sources = await ResolveSourcesAsync(input, maxFrames: 1, ct).ConfigureAwait(false);
            var previewPath = Path.Combine(directory, $"{Guid.NewGuid():N}.{_registry.ExtensionFor(settings.Output)}");
            var frame = sources.Frames[0];
            var rendered = await Task.Run(() => PreviewCore(frame, job, ct), ct).ConfigureAwait(false);
            try
            {
                await WriteBytesAsync(rendered.Bytes, previewPath, job, ct).ConfigureAwait(false);
            }
            catch
            {
                TryDelete(previewPath);
                throw;
            }

            var estimate = rendered.EstimatedBytes;
            if (estimate is null)
            {
                var previewBytes = new FileInfo(previewPath).Length;
                var scaled = (long)(previewBytes * ((double)rendered.FinalPixels / Math.Max(rendered.PreviewPixels, 1)));
                estimate = job.UsesTargetSize ? Math.Min(scaled, job.TargetSizeBytes!.Value) : scaled;
            }
            return new PreviewResult(previewPath, Math.Max(estimate.Value, 1));
        }
        catch (Exception ex)
        {
            throw Wrap(ex, input.Path, "preview", ct);
        }
        finally
        {
            sources?.Dispose();
        }
    }

    private void EnsureSupported(InputInfo input, FormatId output)
    {
        if (!Supports(input, output))
        {
            throw new ConversionException(ConversionErrorCode.UnsupportedFormat, input.Path, "convert", $"{input.Format} -> {output}");
        }
    }

    private bool SystemCanEncode(FormatId format) => _systemCodec.IsAvailable && _systemCodec.CanEncode(format);

    private void RequireEncoder(ImageJob job, string path)
    {
        if (job.Writer == Writer.System && !SystemCanEncode(job.Output))
        {
            throw new ConversionException(ConversionErrorCode.MissingSystemCodec, path, "system-encode", $"no system encoder for '{job.Output}'");
        }
    }

    // ---- Sources ------------------------------------------------------------------------------

    /// <summary>One decodable frame: a file Skia may read, with the format it must contain.</summary>
    private sealed record Frame(string Path, FormatId Format);

    /// <summary>The frames to convert plus the scratch folder of system-decoded PNGs (deleted on dispose).</summary>
    private sealed class Sources(IReadOnlyList<Frame> frames, string? scratchDirectory) : IDisposable
    {
        public IReadOnlyList<Frame> Frames { get; } = frames;

        public void Dispose()
        {
            if (scratchDirectory is null)
            {
                return;
            }
            try
            {
                Directory.Delete(scratchDirectory, recursive: true);
            }
            catch (IOException)
            {
                // Best effort; the folder lives in the temp directory.
            }
            catch (UnauthorizedAccessException)
            {
                // Best effort.
            }
        }
    }

    /// <summary>
    /// Skia-readable inputs are used directly. HEIC, AVIF, TIFF and RAW that is not DNG are decoded by the
    /// system codec into PNG frames in a private temp folder; nothing else ever reads those files.
    /// </summary>
    private async Task<Sources> ResolveSourcesAsync(InputInfo input, int maxFrames, CancellationToken ct)
    {
        if (!SkiaImaging.IsSystemOnly(input.Format) && (input.Format != FormatRegistry.Raw || IsSkiaDng(input.Path)))
        {
            return new Sources([new Frame(input.Path, input.Format)], null);
        }

        if (!_systemCodec.IsAvailable || (input.Format != FormatRegistry.Raw && !_systemCodec.CanDecode(input.Format)))
        {
            // RAW is attempted even when the probe cannot confirm a decoder; a failed decode maps to MissingSystemCodec.
            throw new ConversionException(ConversionErrorCode.MissingSystemCodec, input.Path, "system-decode", $"no system decoder for '{input.Format}'");
        }

        var scratch = Path.Combine(Path.GetTempPath(), "Kvertis", "decode-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        var sources = new Sources([], scratch);
        try
        {
            var pngs = await _systemCodec.DecodeToPngFramesAsync(input.Path, scratch, maxFrames, ct).ConfigureAwait(false);
            if (pngs.Count == 0)
            {
                throw new ConversionException(ConversionErrorCode.CorruptFile, input.Path, "system-decode", "no frames");
            }
            LogSystemDecoded(_logger, input.Format.Id, pngs.Count);
            return new Sources([.. pngs.Take(maxFrames).Select(p => new Frame(p, FormatRegistry.Png))], scratch);
        }
        catch
        {
            sources.Dispose();
            throw;
        }
    }

    private static bool IsSkiaDng(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var codec = SkiaImaging.OpenCodec(stream, FormatRegistry.Raw);
            return codec is not null;
        }
        catch (IOException)
        {
            return false;
        }
    }

    /// <summary>EXIF of a JPEG source for MetadataPolicy.Keep, orientation reset (pixels get rotated). Null otherwise.</summary>
    private static byte[]? ReadSourceExif(InputInfo input)
    {
        if (input.Format != FormatRegistry.Jpg)
        {
            return null; // Non-JPEG sources: Kvertis keeps no metadata (Skia writes none, nothing is copied).
        }
        using var stream = new FileStream(input.Path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var exif = JpegSegments.ReadExif(stream);
        return exif is null ? null : JpegSegments.WithUprightOrientation(exif);
    }

    // ---- Frame conversion ---------------------------------------------------------------------

    private async Task WriteFrameAsync(Frame frame, string tempPath, ImageJob job, byte[]? exif, IProgress<ConversionProgress> progress, CancellationToken ct)
    {
        var bytes = await Task.Run(() => RenderFrame(frame, job, exif, progress, ct), ct).ConfigureAwait(false);
        await WriteBytesAsync(bytes, tempPath, job, ct).ConfigureAwait(false);
    }

    /// <summary>Writes encoded bytes; for system formats the bytes are a PNG that the system encoder turns into the target.</summary>
    private async Task WriteBytesAsync(byte[] bytes, string path, ImageJob job, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (job.Writer != Writer.System)
        {
            await File.WriteAllBytesAsync(path, bytes, ct).ConfigureAwait(false);
            return;
        }
        var png = path + ".png";
        try
        {
            await File.WriteAllBytesAsync(png, bytes, ct).ConfigureAwait(false);
            await _systemCodec.EncodeFromPngAsync(png, path, job.Output, job.Quality, ct).ConfigureAwait(false);
        }
        finally
        {
            TryDelete(png);
        }
    }

    private byte[] RenderFrame(Frame frame, ImageJob job, byte[]? exif, IProgress<ConversionProgress> progress, CancellationToken ct)
    {
        using var decoded = SkiaImaging.Decode(frame.Path, frame.Format, ct);
        progress.Report(new ConversionProgress(0.2, ConversionPhase.Converting));

        using var prepared = Prepare(decoded, job, job.MaxDimension, ct);
        progress.Report(new ConversionProgress(0.5, ConversionPhase.Converting));

        byte[] bytes;
        if (job.UsesTargetSize)
        {
            progress.Report(new ConversionProgress(0.6, ConversionPhase.Optimizing));
            bytes = FitToTarget(prepared, job, progress, ct).Bytes;
        }
        else
        {
            bytes = Encode(prepared, job, job.Quality, ct);
        }
        return AttachMetadata(bytes, job, exif, prepared.Width, prepared.Height);
    }

    private static byte[] AttachMetadata(byte[] bytes, ImageJob job, byte[]? exif, int width, int height)
    {
        if (exif is null || job.Metadata != MetadataPolicy.Keep)
        {
            return bytes;
        }
        return job.Writer switch
        {
            Writer.Jpeg => JpegSegments.InsertApp1(bytes, exif),
            Writer.WebP => WebPChunks.AddExif(bytes, exif.AsSpan(JpegSegments.ExifIdentifier.Length), width, height),
            _ => bytes, // PNG/ICO/system encoders: no EXIF carried over.
        };
    }

    private readonly record struct RenderedPreview(byte[] Bytes, long? EstimatedBytes, long FinalPixels, long PreviewPixels);

    private RenderedPreview PreviewCore(Frame frame, ImageJob job, CancellationToken ct)
    {
        using var image = SkiaImaging.Decode(frame.Path, frame.Format, ct);
        var (fw, fh) = SkiaImaging.FitWithin(image.Width, image.Height, job.EffectiveMaxDimension(job.MaxDimension));
        var finalPixels = (long)fw * fh;

        long? estimate = null;
        int? quality = null;
        if (job.Writer == Writer.System)
        {
            // BMP size is exact (24 bit rows padded to 4 bytes); TIFF/GIF are scaled from the preview encode.
            if (job.Output == FormatRegistry.Bmp)
            {
                estimate = 54 + (long)((fw * 3 + 3) & ~3) * fh;
            }
        }
        else if (finalPixels <= FullEstimateMaxPixels)
        {
            using var full = Prepare(image, job, job.MaxDimension, ct);
            if (job.UsesTargetSize)
            {
                var fit = FitToTarget(full, job, progress: null, ct);
                estimate = fit.Bytes.LongLength;
                quality = fit.Quality;
            }
            else
            {
                estimate = Encode(full, job, job.Quality, ct).LongLength;
            }
        }

        ct.ThrowIfCancellationRequested();
        var previewMax = job.MaxDimension is > 0 and var m ? Math.Min(m, PreviewMaxDimension) : PreviewMaxDimension;
        using var preview = Prepare(image, job, previewMax, ct);
        var bytes = Encode(preview, job, quality ?? job.Quality, ct);
        return new RenderedPreview(bytes, estimate, finalPixels, (long)preview.Width * preview.Height);
    }

    /// <summary>Resize (never up) and alpha handling. Always returns a new bitmap the caller disposes.</summary>
    private static SKBitmap Prepare(SKBitmap source, ImageJob job, int? maxDimension, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var (width, height) = SkiaImaging.FitWithin(source.Width, source.Height, job.EffectiveMaxDimension(maxDimension));
        var result = width == source.Width && height == source.Height
            ? source.Copy() ?? throw new ConversionException(ConversionErrorCode.Unknown, step: "copy")
            : SkiaImaging.Resize(source, width, height);
        ct.ThrowIfCancellationRequested();
        if (!job.OutputSupportsTransparency)
        {
            SkiaImaging.FlattenOnWhite(result);
        }
        return result;
    }

    /// <summary>Encodes one frame. System formats are encoded as PNG here and finished by the system encoder.</summary>
    private static byte[] Encode(SKBitmap bitmap, ImageJob job, int quality, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return job.Writer switch
        {
            Writer.Jpeg => SkiaImaging.EncodeJpeg(bitmap, quality),
            Writer.WebP => SkiaImaging.EncodeWebP(bitmap, quality, job.Lossless),
            Writer.Png => SkiaImaging.EncodePng(bitmap, maxCompression: job.Lossless),
            Writer.Ico => IcoWriter.Write(SkiaImaging.EncodePng(bitmap, maxCompression: true), bitmap.Width, bitmap.Height),
            _ => SkiaImaging.EncodePng(bitmap),
        };
    }

    internal readonly record struct FitResult(byte[] Bytes, int Quality, int ScalePercent);

    /// <summary>
    /// Finds the highest quality whose encoding fits the target (binary search, at most
    /// <see cref="MaxSearchEncodes"/> encodes); if even the lowest quality is too large, shrinks the
    /// longest edge in 10 % steps down to <see cref="MinScalePercent"/> %.
    /// </summary>
    private FitResult FitToTarget(SKBitmap image, ImageJob job, IProgress<ConversionProgress>? progress, CancellationToken ct)
    {
        var target = job.TargetSizeBytes!.Value;
        var smallest = long.MaxValue;

        var found = SearchQuality(image, job, target, ref smallest, ct);
        if (found is { } hit)
        {
            LogTargetReached(_logger, target, hit.Bytes.LongLength, hit.Quality, 100);
            return new FitResult(hit.Bytes, hit.Quality, 100);
        }

        var longest = Math.Max(image.Width, image.Height);
        var steps = ScaleSteps().ToList();
        for (var i = 0; i < steps.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var percent = steps[i];
            progress?.Report(new ConversionProgress(0.6 + 0.3 * (i + 1) / steps.Count, ConversionPhase.Optimizing));

            var edge = Math.Max(1, (int)Math.Round(longest * percent / 100.0));
            var (w, h) = SkiaImaging.FitWithin(image.Width, image.Height, edge);
            using var scaled = SkiaImaging.Resize(image, w, h);

            var atMinimum = Encode(scaled, job, MinSearchQuality, ct);
            smallest = Math.Min(smallest, atMinimum.LongLength);
            if (atMinimum.LongLength > target)
            {
                continue;
            }

            var best = SearchQuality(scaled, job, target, ref smallest, ct, low: MinSearchQuality + 1)
                       ?? (atMinimum, MinSearchQuality);
            LogTargetReached(_logger, target, best.Bytes.LongLength, best.Quality, percent);
            return new FitResult(best.Bytes, best.Quality, percent);
        }

        throw new ConversionException(
            ConversionErrorCode.TargetSizeUnreachable,
            step: "target-size",
            detail: smallest.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    private static (byte[] Bytes, int Quality)? SearchQuality(
        SKBitmap image, ImageJob job, long target, ref long smallest, CancellationToken ct, int low = MinSearchQuality)
    {
        var high = MaxSearchQuality;
        (byte[] Bytes, int Quality)? best = null;
        for (var encodes = 0; low <= high && encodes < MaxSearchEncodes; encodes++)
        {
            ct.ThrowIfCancellationRequested();
            var mid = (low + high) / 2;
            var bytes = Encode(image, job, mid, ct);
            smallest = Math.Min(smallest, bytes.LongLength);
            if (bytes.LongLength <= target)
            {
                best = (bytes, mid);
                low = mid + 1;
            }
            else
            {
                high = mid - 1;
            }
        }
        return best;
    }

    private static IEnumerable<int> ScaleSteps()
    {
        for (var percent = 90; percent > MinScalePercent; percent -= 10)
        {
            yield return percent;
        }
        yield return MinScalePercent;
    }

    private long ExpectedBytes(InputInfo input, ConversionSettings settings)
    {
        if (settings.TargetSizeBytes is { } target && _registry.Get(settings.Output)?.Lossless != true)
        {
            return target;
        }
        if (input is { Width: { } w, Height: { } h })
        {
            var bytesPerPixel = _registry.Get(settings.Output)?.Lossless == true ? 4L : 1L;
            return Math.Max(input.SizeBytes, (long)w * h * bytesPerPixel);
        }
        return input.SizeBytes * 2;
    }

    private static ConversionException Wrap(Exception ex, string path, string step, CancellationToken ct)
    {
        if (ct.IsCancellationRequested && ex is not ConversionException { Code: ConversionErrorCode.Cancelled })
        {
            return new ConversionException(ConversionErrorCode.Cancelled, path, step, inner: ex);
        }
        var translated = ConversionException.From(ex, path, step);
        return translated.FilePath is null
            ? new ConversionException(translated.Code, path, translated.Step ?? step, translated.Detail, translated.InnerException ?? translated)
            : translated;
    }

    private static void TryDelete(string? path)
    {
        if (path is null)
        {
            return;
        }
        try
        {
            File.Delete(path);
        }
        catch (Exception)
        {
            // Best effort; leftovers live in the output or temp folder.
        }
    }

    private static Writer? WriterFor(FormatId output)
    {
        if (output == FormatRegistry.Jpg)
        {
            return Writer.Jpeg;
        }
        if (output == FormatRegistry.Png)
        {
            return Writer.Png;
        }
        if (output == FormatRegistry.WebP)
        {
            return Writer.WebP;
        }
        if (output == FormatRegistry.Ico)
        {
            return Writer.Ico;
        }
        if (output == FormatRegistry.Tiff || output == FormatRegistry.Bmp || output == FormatRegistry.Gif)
        {
            return Writer.System;
        }
        return null;
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Image {From} -> {To} ({Frames} frame(s)): {InputBytes} -> {OutputBytes} bytes in {ElapsedMs} ms")]
    private static partial void LogConverted(ILogger logger, string from, string to, int frames, long inputBytes, long outputBytes, long elapsedMs);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Target size {Target} reached with {Bytes} bytes at quality {Quality}, scale {Percent} %")]
    private static partial void LogTargetReached(ILogger logger, long target, long bytes, int quality, int percent);

    [LoggerMessage(Level = LogLevel.Debug, Message = "{Format} decoded by system codec into {Frames} frame(s)")]
    private static partial void LogSystemDecoded(ILogger logger, string format, int frames);

    private enum Writer
    {
        Jpeg,
        Png,
        WebP,
        Ico,
        /// <summary>TIFF, BMP, GIF: Skia renders a PNG, the system encoder writes the target.</summary>
        System,
    }

    /// <summary>Maps a frame's 0..1 progress into its share of the whole job.</summary>
    private sealed class ScaledProgress(IProgress<ConversionProgress> inner, double offset, double scale) : IProgress<ConversionProgress>
    {
        public void Report(ConversionProgress value) =>
            inner.Report(value with { Fraction = offset + value.Fraction * scale });
    }

    /// <summary>Per-job settings resolved once from ConversionSettings.</summary>
    private sealed record ImageJob(
        FormatId Output,
        Writer Writer,
        int Quality,
        long? TargetSizeBytes,
        MetadataPolicy Metadata,
        bool Lossless,
        bool OutputSupportsTransparency,
        bool LossyOutput,
        int? MaxDimension)
    {
        public bool UsesTargetSize => TargetSizeBytes is > 0 && LossyOutput;

        /// <summary>ICO cannot exceed 256 px; combine with the requested maximum.</summary>
        public int? EffectiveMaxDimension(int? requested)
        {
            if (Writer != Writer.Ico)
            {
                return requested;
            }
            return requested is > 0 and var r ? Math.Min(r, IcoWriter.MaxDimension) : IcoWriter.MaxDimension;
        }

        public static ImageJob From(ConversionSettings settings, FormatRegistry registry)
        {
            var descriptor = registry.Get(settings.Output);
            var writer = WriterFor(settings.Output)
                         ?? throw new ConversionException(ConversionErrorCode.UnsupportedFormat, step: "convert", detail: settings.Output.Id);
            var lossless = settings.Preset == ConversionPreset.Archive || settings.GetAdvancedBool(PresetCatalog.AdvancedKeys.Lossless);
            return new ImageJob(
                settings.Output,
                writer,
                settings.QualityClamped,
                settings.TargetSizeBytes,
                settings.Metadata,
                lossless,
                descriptor?.SupportsTransparency == true,
                LossyOutput: writer is Writer.Jpeg or Writer.WebP && !lossless,
                settings.GetAdvancedInt(ConversionSettings.AdvancedKeys.MaxDimension));
        }
    }
}
