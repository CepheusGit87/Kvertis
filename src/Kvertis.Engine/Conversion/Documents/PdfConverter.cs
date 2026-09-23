using System.Diagnostics;
using ImageMagick;
using Kvertis.Engine.Abstractions;
using Kvertis.Engine.Formats;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace Kvertis.Engine.Conversion.Documents;

/// <summary>Opens PDFs for reading and rejects every encrypted PDF before any content is read.</summary>
internal static class PdfInspection
{
    /// <summary>
    /// Opens the document. The PDF reader library silently tries an empty user password; an encrypted
    /// file must never be read that way, so any encryption dictionary leads to ProtectedFile, even if
    /// the file would open without a password. The check happens before a page is touched.
    /// </summary>
    public static PdfDocument OpenUnencrypted(string path, string step)
    {
        PdfDocument document;
        try
        {
            document = PdfDocument.Open(path, new ParsingOptions { UseLenientParsing = true, SkipMissingFonts = true });
        }
        catch (Exception ex) when (ex is not ConversionException)
        {
            throw DocumentErrors.Map(ex, path, step);
        }
        bool encrypted;
        try
        {
            encrypted = document.IsEncrypted || document.Structure.Trailer.EncryptionToken is not null;
        }
        catch (Exception ex)
        {
            document.Dispose();
            throw DocumentErrors.Map(ex, path, step);
        }
        if (encrypted)
        {
            document.Dispose();
            throw new ConversionException(ConversionErrorCode.ProtectedFile, path, step, "encrypted pdf");
        }
        return document;
    }
}

/// <summary>
/// PDF → TXT (text per page in reading order, pages separated by form feed) and PDF → PNG/JPG per
/// page through an <see cref="IPdfRasterizer"/>. Encrypted PDFs are rejected.
/// </summary>
public sealed class PdfConverter : IConverter
{
    private const int DefaultDpi = 150;

    private readonly IPdfRasterizer _rasterizer;

    public PdfConverter(IPdfRasterizer? rasterizer = null)
    {
        _rasterizer = rasterizer ?? NullPdfRasterizer.Instance;
    }

    public string Name => "pdf";

    public bool Supports(InputInfo input, FormatId output)
    {
        ArgumentNullException.ThrowIfNull(input);
        return input.Format == FormatRegistry.Pdf
               && (output == FormatRegistry.Txt || output == FormatRegistry.Png || output == FormatRegistry.Jpg);
    }

    public Task<ConversionResult> ConvertAsync(InputInfo input, string outputPath, ConversionSettings settings, IProgress<ConversionProgress> progress, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        if (!Supports(input, settings.Output))
        {
            throw new ConversionException(ConversionErrorCode.UnsupportedFormat, input.Path, "pdf", $"{input.Format} -> {settings.Output}");
        }
        if (settings.Output == FormatRegistry.Txt)
        {
            return DocumentTasks.RunAsync(() => ExtractText(input, outputPath, progress, ct), input.Path, "pdf-text");
        }
        return DocumentTasks.RunAsync(() => RasterizeAsync(input, outputPath, settings, progress, ct), input.Path, "pdf-raster");
    }

    public Task<PreviewResult?> PreviewAsync(InputInfo input, ConversionSettings settings, CancellationToken ct) =>
        Task.FromResult<PreviewResult?>(null);

    private static ConversionResult ExtractText(InputInfo input, string outputPath, IProgress<ConversionProgress>? progress, CancellationToken ct)
    {
        var watch = Stopwatch.StartNew();
        progress?.Report(ConversionProgress.Start);
        ct.ThrowIfCancellationRequested();
        using var document = PdfInspection.OpenUnencrypted(input.Path, "pdf-text");
        using var outputs = new OutputSet();
        outputs.Write(outputPath, input.SizeBytes, temp =>
        {
            using var writer = DocumentText.CreateWriter(temp, DocumentText.Utf8Bom, DocumentText.Crlf);
            var pages = document.NumberOfPages;
            for (var i = 1; i <= pages; i++)
            {
                ct.ThrowIfCancellationRequested();
                var page = document.GetPage(i);
                var text = ContentOrderTextExtractor.GetText(page);
                foreach (var line in DocumentWriters.SplitLines(text))
                {
                    writer.WriteLine(line);
                }
                if (i < pages)
                {
                    writer.Write('\f');
                    writer.WriteLine();
                }
                progress.ReportUnit(i, pages);
            }
        });
        progress?.Report(ConversionProgress.Complete);
        return outputs.Complete(input, watch);
    }

    private async Task<ConversionResult> RasterizeAsync(InputInfo input, string outputPath, ConversionSettings settings, IProgress<ConversionProgress>? progress, CancellationToken ct)
    {
        var watch = Stopwatch.StartNew();
        progress?.Report(ConversionProgress.Start);
        if (!_rasterizer.IsAvailable)
        {
            throw new ConversionException(ConversionErrorCode.MissingSystemCodec, input.Path, "pdf-raster", "pdf rasterizer");
        }

        int pages;
        using (var document = PdfInspection.OpenUnencrypted(input.Path, "pdf-raster"))
        {
            pages = document.NumberOfPages;
        }
        if (pages <= 0)
        {
            throw new ConversionException(ConversionErrorCode.CorruptFile, input.Path, "pdf-raster", "no pages");
        }

        var dpi = Math.Clamp(settings.GetAdvancedInt(ConversionSettings.AdvancedKeys.Dpi) ?? DefaultDpi, 36, 600);
        var jpg = settings.Output == FormatRegistry.Jpg;
        using var outputs = new OutputSet();
        for (var i = 0; i < pages; i++)
        {
            ct.ThrowIfCancellationRequested();
            var path = pages == 1 ? outputPath : DocumentPaths.PagePath(outputPath, i);
            var expected = input.SizeBytes / pages + 1;
            await outputs.WriteAsync(path, expected, async temp =>
            {
                if (!jpg)
                {
                    await _rasterizer.RasterizePageAsync(input.Path, i, dpi, temp, ct).ConfigureAwait(false);
                    return;
                }
                var png = Path.Combine(Path.GetTempPath(), "kvertis-" + Guid.NewGuid().ToString("N") + ".png");
                try
                {
                    await _rasterizer.RasterizePageAsync(input.Path, i, dpi, png, ct).ConfigureAwait(false);
                    using var image = new MagickImage(png);
                    image.Strip();
                    image.Quality = (uint)Math.Clamp(settings.QualityClamped, 1, 100);
                    image.BackgroundColor = MagickColors.White;
                    image.Alpha(AlphaOption.Remove);
                    await image.WriteAsync(temp, MagickFormat.Jpeg, ct).ConfigureAwait(false);
                }
                finally
                {
                    TryDelete(png);
                }
            }).ConfigureAwait(false);
            progress.ReportUnit(i + 1, pages);
        }
        progress?.Report(ConversionProgress.Complete);
        return outputs.Complete(input, watch);
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
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
