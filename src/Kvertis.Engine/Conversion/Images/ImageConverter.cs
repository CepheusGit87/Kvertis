using System.Diagnostics;
using ImageMagick;
using Kvertis.Engine.Abstractions;
using Kvertis.Engine.Formats;
using Kvertis.Engine.IO;
using Microsoft.Extensions.Logging;

namespace Kvertis.Engine.Conversion.Images;

/// <summary>
/// Image → image conversion with Magick.NET (PNG, JPG, WebP, GIF, BMP, TIFF, ICO). Image → PDF is not
/// handled here. HEIC input is decoded by the operating system via <see cref="IHeicDecoder"/> first and
/// never reaches Magick.NET's own HEIC delegate (ADR-006). Animated sources: first frame only.
/// </summary>
public sealed partial class ImageConverter : IConverter
{
    /// <summary>Longest edge of preview images.</summary>
    public const int PreviewMaxDimension = 1024;

    /// <summary>Above this pixel count the preview estimates the final size by scaling instead of a full encode.</summary>
    public const long FullEstimateMaxPixels = 24_000_000;

    internal const int MinSearchQuality = 20;
    internal const int MaxSearchQuality = 95;
    internal const int MaxSearchEncodes = 7;
    internal const int MinScalePercent = 25;
    private const int IcoMaxDimension = 256;

    // PNG "quality" in ImageMagick is zlib level * 10 + filter type; 5 = adaptive filtering.
    private const uint PngQualityArchive = 95;
    private const uint PngQualityDefault = 65;

    private readonly FormatRegistry _registry;
    private readonly IHeicDecoder _heicDecoder;
    private readonly ILogger<ImageConverter> _logger;

    public ImageConverter(FormatRegistry registry, IHeicDecoder heicDecoder, ILogger<ImageConverter> logger)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _heicDecoder = heicDecoder ?? throw new ArgumentNullException(nameof(heicDecoder));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string Name => "image";

    public bool Supports(InputInfo input, FormatId output)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.Kind != MediaKind.Image || _registry.Get(input.Format) is not { CanRead: true })
        {
            return false;
        }
        if (input.Format != FormatRegistry.Heic && MagickSupport.ReadFormatFor(input.Format, input.Path) is null)
        {
            return false;
        }
        return _registry.Get(output) is { CanWrite: true, Kind: MediaKind.Image } && MagickSupport.WriteFormatFor(output) is not null;
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
        string? decodedHeic = null;
        try
        {
            ct.ThrowIfCancellationRequested();
            using var output = ConversionOutput.Begin(outputPath, ExpectedBytes(input, settings));

            var directory = Path.GetDirectoryName(Path.GetFullPath(outputPath))!;
            (var sourcePath, var sourceFormat, decodedHeic) = await ResolveSourceAsync(input, directory, ct).ConfigureAwait(false);
            var job = ImageJob.From(settings, _registry);

            await Task.Run(() => ConvertCore(sourcePath, sourceFormat, output.TempPath, job, progress, ct), ct).ConfigureAwait(false);

            ct.ThrowIfCancellationRequested();
            progress.Report(new ConversionProgress(0.95, ConversionPhase.Finalizing));
            var bytes = output.Commit();
            progress.Report(ConversionProgress.Complete);
            LogConverted(_logger, input.Format.Id, settings.Output.Id, input.SizeBytes, bytes, stopwatch.ElapsedMilliseconds);
            return new ConversionResult(outputPath, input.SizeBytes, bytes, stopwatch.Elapsed);
        }
        catch (Exception ex)
        {
            throw Wrap(ex, input.Path, "convert", ct);
        }
        finally
        {
            TryDelete(decodedHeic);
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

        var directory = Path.Combine(Path.GetTempPath(), "Kvertis");
        string? decodedHeic = null;
        try
        {
            Directory.CreateDirectory(directory);
            (var sourcePath, var sourceFormat, decodedHeic) = await ResolveSourceAsync(input, directory, ct).ConfigureAwait(false);
            var job = ImageJob.From(settings, _registry);
            var previewPath = Path.Combine(directory, $"{Guid.NewGuid():N}.{_registry.ExtensionFor(settings.Output)}");
            return await Task.Run(() => PreviewCore(sourcePath, sourceFormat, previewPath, job, ct), ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            throw Wrap(ex, input.Path, "preview", ct);
        }
        finally
        {
            TryDelete(decodedHeic);
        }
    }

    private void EnsureSupported(InputInfo input, FormatId output)
    {
        if (!Supports(input, output))
        {
            throw new ConversionException(ConversionErrorCode.UnsupportedFormat, input.Path, "convert", $"{input.Format} -> {output}");
        }
    }

    /// <summary>For HEIC, decodes via the system into a temporary PNG; everything else is read directly.</summary>
    private async Task<(string Path, FormatId Format, string? TempFile)> ResolveSourceAsync(InputInfo input, string tempDirectory, CancellationToken ct)
    {
        if (input.Format != FormatRegistry.Heic)
        {
            return (input.Path, input.Format, null);
        }
        if (!_heicDecoder.IsAvailable)
        {
            throw new ConversionException(ConversionErrorCode.MissingSystemCodec, input.Path, "heic-decode", "HEIF image extension not available");
        }

        var tempPng = Path.Combine(tempDirectory, $".kvertis-heic-{Guid.NewGuid():N}.png");
        try
        {
            await _heicDecoder.DecodeToPngAsync(input.Path, tempPng, ct).ConfigureAwait(false);
        }
        catch
        {
            TryDelete(tempPng);
            throw;
        }
        LogHeicDecoded(_logger, input.Path);
        return (tempPng, FormatRegistry.Png, tempPng);
    }

    private void ConvertCore(string sourcePath, FormatId sourceFormat, string tempPath, ImageJob job, IProgress<ConversionProgress> progress, CancellationToken ct)
    {
        using var image = Load(sourcePath, sourceFormat, ct);
        progress.Report(new ConversionProgress(0.2, ConversionPhase.Converting));

        Prepare(image, job, job.MaxDimension, ct);
        ct.ThrowIfCancellationRequested();
        progress.Report(new ConversionProgress(0.5, ConversionPhase.Converting));

        if (job.UsesTargetSize)
        {
            progress.Report(new ConversionProgress(0.6, ConversionPhase.Optimizing));
            var fit = FitToTarget(image, job, progress, ct);
            File.WriteAllBytes(tempPath, fit.Bytes);
            return;
        }

        ApplyEncoding(image, job, job.Quality);
        ct.ThrowIfCancellationRequested();
        using var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None);
        image.Write(stream, job.WriteFormat);
    }

    private PreviewResult PreviewCore(string sourcePath, FormatId sourceFormat, string previewPath, ImageJob job, CancellationToken ct)
    {
        using var image = Load(sourcePath, sourceFormat, ct);
        var finalPixels = PixelsAfterResize(image.Width, image.Height, job.EffectiveMaxDimension(job.MaxDimension));

        long? estimate = null;
        int? quality = null;
        if (finalPixels <= FullEstimateMaxPixels)
        {
            using var full = image.Clone();
            Prepare(full, job, job.MaxDimension, ct);
            if (job.UsesTargetSize)
            {
                var fit = FitToTarget(full, job, progress: null, ct);
                estimate = fit.Bytes.LongLength;
                quality = fit.Quality;
            }
            else
            {
                ApplyEncoding(full, job, job.Quality);
                estimate = Encode(full, job.WriteFormat, ct).LongLength;
            }
        }

        ct.ThrowIfCancellationRequested();
        var previewMax = job.MaxDimension is > 0 and var m ? Math.Min(m, PreviewMaxDimension) : PreviewMaxDimension;
        Prepare(image, job, previewMax, ct);
        ApplyEncoding(image, job, quality ?? job.Quality);
        var previewBytes = Encode(image, job.WriteFormat, ct);
        File.WriteAllBytes(previewPath, previewBytes);

        if (estimate is null)
        {
            var previewPixels = Math.Max((long)image.Width * image.Height, 1);
            var scaled = (long)(previewBytes.LongLength * ((double)finalPixels / previewPixels));
            estimate = job.UsesTargetSize ? Math.Min(scaled, job.TargetSizeBytes!.Value) : scaled;
        }
        return new PreviewResult(previewPath, Math.Max(estimate.Value, 1));
    }

    private static MagickImage Load(string path, FormatId format, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var settings = MagickSupport.ReadSettings(format, path, firstFrameOnly: true);
        var image = new MagickImage(path, settings);
        image.Progress += (_, e) =>
        {
            if (ct.IsCancellationRequested)
            {
                e.Cancel = true;
            }
        };
        return image;
    }

    /// <summary>Geometry, metadata and alpha handling. Encoder parameters are applied separately.</summary>
    private static void Prepare(IMagickImage<ushort> image, ImageJob job, int? maxDimension, CancellationToken ct)
    {
        image.AutoOrient(); // bakes the EXIF orientation into pixels before EXIF may be stripped
        image.ResetPage();  // first GIF frame may carry a canvas offset
        ct.ThrowIfCancellationRequested();

        ResizeDown(image, job.EffectiveMaxDimension(maxDimension));
        ct.ThrowIfCancellationRequested();

        if (job.Metadata == MetadataPolicy.Strip)
        {
            StripKeepingIcc(image);
        }

        if (!job.OutputSupportsTransparency && image.HasAlpha)
        {
            image.BackgroundColor = MagickColors.White;
            image.Alpha(AlphaOption.Remove);
            image.Alpha(AlphaOption.Off);
        }
    }

    /// <summary>Removes EXIF/GPS, XMP, IPTC and comments; re-attaches the ICC profile so colors stay correct.</summary>
    internal static void StripKeepingIcc(IMagickImage<ushort> image)
    {
        var icc = image.GetProfile("icc");
        image.Strip();
        if (icc is not null)
        {
            image.SetProfile(icc);
            // Strip() also tells the PNG writer to drop iCCP along with the text chunks. Keep dropping
            // the metadata chunks, but let the color profile through again. zTXt must not be listed:
            // ImageMagick ties it to the ICC chunk. EXIF/XMP are already gone, so no zTXt profile remains.
            image.SetArtifact("png:exclude-chunk", "eXIf,iTXt,tEXt,date");
        }
    }

    private static void ResizeDown(IMagickImage<ushort> image, int? maxDimension)
    {
        if (maxDimension is not { } max || max <= 0)
        {
            return;
        }
        var longest = Math.Max(image.Width, image.Height);
        if (longest <= (uint)max)
        {
            return; // never upscale
        }
        image.Resize(new MagickGeometry((uint)max, (uint)max));
    }

    private static long PixelsAfterResize(uint width, uint height, int? maxDimension)
    {
        var longest = Math.Max(width, height);
        if (maxDimension is > 0 and var max && longest > max)
        {
            var scale = (double)max / longest;
            return (long)Math.Round(width * scale) * (long)Math.Round(height * scale);
        }
        return (long)width * height;
    }

    private static void ApplyEncoding(IMagickImage<ushort> image, ImageJob job, int quality)
    {
        image.Format = job.WriteFormat;
        switch (job.WriteFormat)
        {
            case MagickFormat.Jpeg:
                image.Quality = (uint)Math.Clamp(quality, 1, 100);
                break;
            case MagickFormat.WebP:
                image.Quality = (uint)Math.Clamp(quality, 1, 100);
                if (job.Lossless)
                {
                    image.Settings.SetDefine(MagickFormat.WebP, "lossless", true);
                }
                break;
            case MagickFormat.Png:
                image.Quality = job.Lossless ? PngQualityArchive : PngQualityDefault;
                // Without this the writer replaces a standard sRGB profile by an sRGB chunk and the ICC data is gone.
                image.Settings.SetDefine(MagickFormat.Png, "preserve-iCCP", true);
                break;
            case MagickFormat.Gif:
                if (image.TotalColors > 256)
                {
                    image.Quantize(new QuantizeSettings { Colors = 256 });
                }
                break;
            case MagickFormat.Tiff:
                image.Settings.Compression = CompressionMethod.LZW;
                break;
        }
    }

    private static byte[] Encode(IMagickImage<ushort> image, MagickFormat format, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var stream = new MemoryStream();
        image.Write(stream, format);
        return stream.ToArray();
    }

    private static byte[] EncodeAt(IMagickImage<ushort> image, ImageJob job, int quality, CancellationToken ct)
    {
        ApplyEncoding(image, job, quality);
        return Encode(image, job.WriteFormat, ct);
    }

    internal readonly record struct FitResult(byte[] Bytes, int Quality, int ScalePercent);

    /// <summary>
    /// Finds the highest quality whose encoding fits the target (binary search, at most
    /// <see cref="MaxSearchEncodes"/> encodes); if even the lowest quality is too large, shrinks the
    /// longest edge in 10 % steps down to <see cref="MinScalePercent"/> %.
    /// </summary>
    private FitResult FitToTarget(IMagickImage<ushort> image, ImageJob job, IProgress<ConversionProgress>? progress, CancellationToken ct)
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

            using var scaled = image.Clone();
            var edge = Math.Max(1u, (uint)Math.Round(longest * percent / 100.0));
            scaled.Resize(new MagickGeometry(edge, edge));

            var atMinimum = EncodeAt(scaled, job, MinSearchQuality, ct);
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
        IMagickImage<ushort> image, ImageJob job, long target, ref long smallest, CancellationToken ct, int low = MinSearchQuality)
    {
        var high = MaxSearchQuality;
        (byte[] Bytes, int Quality)? best = null;
        for (var encodes = 0; low <= high && encodes < MaxSearchEncodes; encodes++)
        {
            ct.ThrowIfCancellationRequested();
            var mid = (low + high) / 2;
            var bytes = EncodeAt(image, job, mid, ct);
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
        var translated = MagickSupport.Translate(ex, path, step);
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

    [LoggerMessage(Level = LogLevel.Debug, Message = "Image {From} -> {To}: {InputBytes} -> {OutputBytes} bytes in {ElapsedMs} ms")]
    private static partial void LogConverted(ILogger logger, string from, string to, long inputBytes, long outputBytes, long elapsedMs);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Target size {Target} reached with {Bytes} bytes at quality {Quality}, scale {Percent} %")]
    private static partial void LogTargetReached(ILogger logger, long target, long bytes, int quality, int percent);

    [LoggerMessage(Level = LogLevel.Debug, Message = "HEIC decoded by system codec: {Path}")]
    private static partial void LogHeicDecoded(ILogger logger, string path);

    /// <summary>Per-job settings resolved once from ConversionSettings.</summary>
    private sealed record ImageJob(
        FormatId Output,
        MagickFormat WriteFormat,
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
            if (WriteFormat != MagickFormat.Ico)
            {
                return requested;
            }
            return requested is > 0 and var r ? Math.Min(r, IcoMaxDimension) : IcoMaxDimension;
        }

        public static ImageJob From(ConversionSettings settings, FormatRegistry registry)
        {
            var descriptor = registry.Get(settings.Output);
            var writeFormat = MagickSupport.WriteFormatFor(settings.Output)
                              ?? throw new ConversionException(ConversionErrorCode.UnsupportedFormat, step: "convert", detail: settings.Output.Id);
            var lossless = settings.Preset == ConversionPreset.Archive || settings.GetAdvancedBool(PresetCatalog.AdvancedKeys.Lossless);
            return new ImageJob(
                settings.Output,
                writeFormat,
                settings.QualityClamped,
                settings.TargetSizeBytes,
                settings.Metadata,
                lossless,
                descriptor?.SupportsTransparency == true,
                LossyOutput: writeFormat is MagickFormat.Jpeg or MagickFormat.WebP && !lossless,
                settings.GetAdvancedInt(ConversionSettings.AdvancedKeys.MaxDimension));
        }
    }
}
