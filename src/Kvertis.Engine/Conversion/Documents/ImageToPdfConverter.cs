using System.Diagnostics;
using Kvertis.Engine.Abstractions;
using Kvertis.Engine.Conversion.Images;
using Kvertis.Engine.Formats;
using Kvertis.Engine.Platform;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using SkiaSharp;

namespace Kvertis.Engine.Conversion.Documents;

/// <summary>
/// Image (JPG, PNG, TIFF) → PDF. One page per image or TIFF frame, A4 portrait or landscape by the
/// image's aspect ratio, image scaled to fit inside a 1 cm margin. Upright JPEG data is embedded without
/// re-encoding; with MetadataPolicy.Strip its EXIF/XMP/IPTC segments are removed first. TIFF pages are
/// decoded by the system codec (<see cref="ISystemImageCodec"/>); without it the conversion fails with
/// MissingSystemCodec.
/// </summary>
public sealed class ImageToPdfConverter : IConverter
{
    private const double A4Short = 595.28;
    private const double A4Long = 841.89;
    private const double Margin = 1 / 2.54 * 72;
    private const int ReencodeJpegQuality = 92;

    private readonly ISystemImageCodec _systemCodec;

    public ImageToPdfConverter(ISystemImageCodec? systemCodec = null)
    {
        _systemCodec = systemCodec ?? NullSystemImageCodec.Instance;
    }

    public string Name => "image-pdf";

    public bool Supports(InputInfo input, FormatId output)
    {
        ArgumentNullException.ThrowIfNull(input);
        return output == FormatRegistry.Pdf
               && (input.Format == FormatRegistry.Jpg || input.Format == FormatRegistry.Png || input.Format == FormatRegistry.Tiff);
    }

    public Task<ConversionResult> ConvertAsync(InputInfo input, string outputPath, ConversionSettings settings, IProgress<ConversionProgress> progress, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        if (!Supports(input, settings.Output))
        {
            throw new ConversionException(ConversionErrorCode.UnsupportedFormat, input.Path, "image-pdf", $"{input.Format} -> {settings.Output}");
        }
        return DocumentTasks.RunAsync(() => ConvertCoreAsync(input, outputPath, settings, progress, ct), input.Path, "image-pdf");
    }

    public Task<PreviewResult?> PreviewAsync(InputInfo input, ConversionSettings settings, CancellationToken ct) =>
        Task.FromResult<PreviewResult?>(null);

    private async Task<ConversionResult> ConvertCoreAsync(InputInfo input, string outputPath, ConversionSettings settings, IProgress<ConversionProgress>? progress, CancellationToken ct)
    {
        var watch = Stopwatch.StartNew();
        progress?.Report(ConversionProgress.Start);
        ct.ThrowIfCancellationRequested();
        var strip = settings.Metadata == MetadataPolicy.Strip;

        var frames = await LoadFramesAsync(input, strip, ct).ConfigureAwait(false);
        using var outputs = new OutputSet();
        try
        {
            outputs.Write(outputPath, input.SizeBytes + 64 * 1024, temp =>
            {
                using var document = new PdfDocument();
                document.Info.Creator = "Kvertis";
                var images = new List<XImage>();
                try
                {
                    for (var i = 0; i < frames.Count; i++)
                    {
                        ct.ThrowIfCancellationRequested();
                        frames[i].Position = 0;
                        var image = XImage.FromStream(frames[i]);
                        images.Add(image);
                        AddPage(document, image);
                        progress.ReportUnit(i + 1, frames.Count);
                    }
                    document.Save(temp);
                }
                finally
                {
                    foreach (var image in images)
                    {
                        image.Dispose();
                    }
                }
            });
        }
        finally
        {
            foreach (var frame in frames)
            {
                frame.Dispose();
            }
        }
        progress?.Report(ConversionProgress.Complete);
        return outputs.Complete(input, watch);
    }

    private static void AddPage(PdfDocument document, XImage image)
    {
        var landscape = image.PixelWidth > image.PixelHeight;
        var page = document.AddPage();
        page.Width = XUnit.FromPoint(landscape ? A4Long : A4Short);
        page.Height = XUnit.FromPoint(landscape ? A4Short : A4Long);
        var availableW = page.Width.Point - 2 * Margin;
        var availableH = page.Height.Point - 2 * Margin;
        var scale = Math.Min(availableW / Math.Max(1, image.PixelWidth), availableH / Math.Max(1, image.PixelHeight));
        var w = image.PixelWidth * scale;
        var h = image.PixelHeight * scale;
        using var gfx = XGraphics.FromPdfPage(page);
        gfx.DrawImage(image, (page.Width.Point - w) / 2, (page.Height.Point - h) / 2, w, h);
    }

    /// <summary>Image data for each page, as streams the PDF library can read (JPEG or 8-bit PNG).</summary>
    private async Task<List<MemoryStream>> LoadFramesAsync(InputInfo input, bool strip, CancellationToken ct)
    {
        if (input.Format == FormatRegistry.Jpg)
        {
            return [LoadJpeg(input.Path, strip, ct)];
        }
        if (input.Format == FormatRegistry.Png)
        {
            // PNG pixels are decoded and re-compressed by the PDF library; no metadata chunks survive.
            return [CanEmbedPngDirectly(input.Path)
                ? new MemoryStream(File.ReadAllBytes(input.Path))
                : ToPng(input.Path, FormatRegistry.Png, ct)];
        }

        // TIFF: only the system codec decodes it; every page arrives as a metadata-free PNG.
        if (!_systemCodec.IsAvailable || !_systemCodec.CanDecode(input.Format))
        {
            throw new ConversionException(ConversionErrorCode.MissingSystemCodec, input.Path, "image-pdf", $"no system decoder for '{input.Format}'");
        }
        var scratch = Path.Combine(Path.GetTempPath(), "Kvertis", "pdf-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        var frames = new List<MemoryStream>();
        try
        {
            var pngs = await _systemCodec.DecodeToPngFramesAsync(input.Path, scratch, ImageConverter.MaxPages, ct).ConfigureAwait(false);
            if (pngs.Count == 0)
            {
                throw new ConversionException(ConversionErrorCode.CorruptFile, input.Path, "image-pdf", "no frames");
            }
            foreach (var png in pngs)
            {
                ct.ThrowIfCancellationRequested();
                frames.Add(ToPng(png, FormatRegistry.Png, ct));
            }
            return frames;
        }
        catch
        {
            foreach (var frame in frames)
            {
                frame.Dispose();
            }
            throw;
        }
        finally
        {
            TryDeleteDirectory(scratch);
        }
    }

    private static MemoryStream LoadJpeg(string path, bool strip, CancellationToken ct)
    {
        SKEncodedOrigin origin;
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (var codec = SkiaImaging.OpenCodec(stream, FormatRegistry.Jpg))
        {
            if (codec is null)
            {
                throw new ConversionException(ConversionErrorCode.CorruptFile, path, "image-pdf", "not a readable jpg image");
            }
            origin = codec.EncodedOrigin;
        }
        if (origin is SKEncodedOrigin.TopLeft)
        {
            var bytes = File.ReadAllBytes(path);
            if (!strip)
            {
                return new MemoryStream(bytes);
            }
            var stripped = JpegMetadataStripper.Strip(bytes);
            if (stripped is not null)
            {
                return new MemoryStream(stripped);
            }
        }

        // Rotated by EXIF (or not parseable): apply the orientation and re-encode once. The encoder writes
        // no metadata; with Keep the EXIF is carried over with the orientation reset to upright.
        using var image = SkiaImaging.Decode(path, FormatRegistry.Jpg, ct);
        SkiaImaging.FlattenOnWhite(image);
        var jpeg = SkiaImaging.EncodeJpeg(image, ReencodeJpegQuality);
        if (!strip)
        {
            using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (JpegSegments.ReadExif(source) is { } exif)
            {
                jpeg = JpegSegments.InsertApp1(jpeg, JpegSegments.WithUprightOrientation(exif));
            }
        }
        return new MemoryStream(jpeg);
    }

    private static bool CanEmbedPngDirectly(string path)
    {
        // The PDF library reads 8-bit, non-interlaced PNGs. Header: bit depth at 24, interlace at 28.
        Span<byte> header = stackalloc byte[29];
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return fs.ReadAtLeast(header, header.Length, throwOnEndOfStream: false) == header.Length
               && header[24] == 8 && header[28] == 0;
    }

    /// <summary>Decodes with Skia (orientation, sRGB) and writes an 8-bit, non-interlaced PNG without metadata.</summary>
    private static MemoryStream ToPng(string path, FormatId format, CancellationToken ct)
    {
        using var image = SkiaImaging.Decode(path, format, ct);
        return new MemoryStream(SkiaImaging.EncodePng(image));
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
            // Best effort.
        }
        catch (UnauthorizedAccessException)
        {
            // Best effort.
        }
    }
}
