using System.Diagnostics;
using ImageMagick;
using Kvertis.Engine.Abstractions;
using Kvertis.Engine.Formats;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace Kvertis.Engine.Conversion.Documents;

/// <summary>
/// Image (JPG, PNG, TIFF) → PDF. One page per image or TIFF frame, A4 portrait or landscape by the
/// image's aspect ratio, image scaled to fit inside a 1 cm margin. JPEG data is embedded without
/// re-encoding; with MetadataPolicy.Strip its EXIF/XMP/IPTC segments are removed first.
/// </summary>
public sealed class ImageToPdfConverter : IConverter
{
    private const double A4Short = 595.28;
    private const double A4Long = 841.89;
    private const double Margin = 1 / 2.54 * 72;
    private const uint ReencodeJpegQuality = 92;

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
        return DocumentTasks.RunAsync(() => Convert(input, outputPath, settings, progress, ct), input.Path, "image-pdf");
    }

    public Task<PreviewResult?> PreviewAsync(InputInfo input, ConversionSettings settings, CancellationToken ct) =>
        Task.FromResult<PreviewResult?>(null);

    private static ConversionResult Convert(InputInfo input, string outputPath, ConversionSettings settings, IProgress<ConversionProgress>? progress, CancellationToken ct)
    {
        var watch = Stopwatch.StartNew();
        progress?.Report(ConversionProgress.Start);
        ct.ThrowIfCancellationRequested();
        var strip = settings.Metadata == MetadataPolicy.Strip;

        var frames = LoadFrames(input, strip, ct);
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
    private static List<MemoryStream> LoadFrames(InputInfo input, bool strip, CancellationToken ct)
    {
        var frames = new List<MemoryStream>();
        if (input.Format == FormatRegistry.Jpg)
        {
            frames.Add(LoadJpeg(input.Path, strip));
            return frames;
        }
        if (input.Format == FormatRegistry.Png && CanEmbedPngDirectly(input.Path))
        {
            // PNG pixels are decoded and re-compressed by the PDF library; no metadata chunks survive.
            frames.Add(new MemoryStream(File.ReadAllBytes(input.Path)));
            return frames;
        }

        using var collection = new MagickImageCollection();
        collection.Read(input.Path);
        foreach (var frame in collection)
        {
            ct.ThrowIfCancellationRequested();
            frames.Add(ToPng(frame));
        }
        return frames;
    }

    private static MemoryStream LoadJpeg(string path, bool strip)
    {
        var orientation = OrientationType.Undefined;
        using (var probe = new MagickImage())
        {
            probe.Ping(path);
            orientation = probe.Orientation;
        }
        if (orientation is OrientationType.Undefined or OrientationType.TopLeft)
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

        // Rotated by EXIF (or not parseable): apply the orientation and re-encode once.
        using var image = new MagickImage(path);
        image.AutoOrient();
        if (strip)
        {
            StripKeepingIcc(image);
        }
        image.Quality = ReencodeJpegQuality;
        var output = new MemoryStream();
        image.Write(output, MagickFormat.Jpeg);
        return output;
    }

    private static bool CanEmbedPngDirectly(string path)
    {
        // The PDF library reads 8-bit, non-interlaced PNGs. Header: bit depth at 24, interlace at 28.
        Span<byte> header = stackalloc byte[29];
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return fs.ReadAtLeast(header, header.Length, throwOnEndOfStream: false) == header.Length
               && header[24] == 8 && header[28] == 0;
    }

    private static MemoryStream ToPng(IMagickImage<ushort> frame)
    {
        frame.AutoOrient();
        StripKeepingIcc(frame);
        frame.Depth = 8;
        frame.Settings.Interlace = Interlace.NoInterlace;
        var output = new MemoryStream();
        frame.Write(output, MagickFormat.Png);
        return output;
    }

    private static void StripKeepingIcc(IMagickImage<ushort> image)
    {
        var icc = image.GetColorProfile();
        image.Strip();
        if (icc is not null)
        {
            image.SetProfile(icc);
        }
    }
}
