using System.Diagnostics;
using System.Globalization;
using System.Text;
using DocumentFormat.OpenXml.Packaging;
using Kvertis.Engine.Abstractions;
using Kvertis.Engine.Formats;

namespace Kvertis.Engine.Conversion.Documents;

/// <summary>
/// OOXML documents to text formats (ADR-010, phase 1): DOCX → TXT/Markdown/HTML, XLSX → CSV/TXT,
/// PPTX → TXT/Markdown. No layout rendering. Encrypted files are rejected, never opened.
/// </summary>
public sealed class OfficeConverter : IConverter
{
    private const string CsvNewLine = "\r\n";

    public string Name => "office";

    public bool Supports(InputInfo input, FormatId output)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.Format == FormatRegistry.Docx)
        {
            return output == FormatRegistry.Txt || output == FormatRegistry.Markdown || output == FormatRegistry.Html;
        }
        if (input.Format == FormatRegistry.Xlsx)
        {
            return output == FormatRegistry.Csv || output == FormatRegistry.Txt;
        }
        if (input.Format == FormatRegistry.Pptx)
        {
            return output == FormatRegistry.Txt || output == FormatRegistry.Markdown;
        }
        return false;
    }

    public Task<ConversionResult> ConvertAsync(InputInfo input, string outputPath, ConversionSettings settings, IProgress<ConversionProgress> progress, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        if (!Supports(input, settings.Output))
        {
            throw new ConversionException(ConversionErrorCode.UnsupportedFormat, input.Path, "office", $"{input.Format} -> {settings.Output}");
        }
        return DocumentTasks.RunAsync(() => Convert(input, outputPath, settings.Output, progress, ct), input.Path, "office");
    }

    public Task<PreviewResult?> PreviewAsync(InputInfo input, ConversionSettings settings, CancellationToken ct) =>
        Task.FromResult<PreviewResult?>(null);

    private static ConversionResult Convert(InputInfo input, string outputPath, FormatId output, IProgress<ConversionProgress>? progress, CancellationToken ct)
    {
        var watch = Stopwatch.StartNew();
        progress?.Report(ConversionProgress.Start);
        using var outputs = new OutputSet();
        try
        {
            ct.ThrowIfCancellationRequested();
            OfficeProtection.EnsureOpenable(input.Path, "open");
            if (input.Format == FormatRegistry.Docx)
            {
                ConvertDocx(input, outputPath, output, outputs, ct);
            }
            else if (input.Format == FormatRegistry.Xlsx)
            {
                ConvertXlsx(input, outputPath, output, outputs, progress, ct);
            }
            else
            {
                ConvertPptx(input, outputPath, output, outputs, progress, ct);
            }
        }
        catch (Exception ex) when (ex is not ConversionException)
        {
            throw DocumentErrors.Map(ex, input.Path, "office");
        }
        progress?.Report(ConversionProgress.Complete);
        return outputs.Complete(input, watch);
    }

    private static void ConvertDocx(InputInfo input, string outputPath, FormatId output, OutputSet outputs, CancellationToken ct)
    {
        using var document = WordprocessingDocument.Open(input.Path, false);
        var reader = new DocxReader(document);
        outputs.Write(outputPath, input.SizeBytes, temp =>
        {
            if (output == FormatRegistry.Txt)
            {
                using var writer = DocumentText.CreateWriter(temp, DocumentText.Utf8Bom, DocumentText.Crlf);
                DocumentWriters.WriteText(reader.ReadBlocks(ct), writer, ct);
            }
            else if (output == FormatRegistry.Markdown)
            {
                using var writer = DocumentText.CreateWriter(temp, DocumentText.Utf8);
                DocumentWriters.WriteMarkdown(reader.ReadBlocks(ct), writer, ct);
            }
            else
            {
                using var writer = DocumentText.CreateWriter(temp, DocumentText.Utf8);
                DocumentWriters.WriteHtml(reader.ReadBlocks(ct), writer, Path.GetFileNameWithoutExtension(input.Path), ct);
            }
        });
    }

    private static void ConvertXlsx(InputInfo input, string outputPath, FormatId output, OutputSet outputs, IProgress<ConversionProgress>? progress, CancellationToken ct)
    {
        using var document = SpreadsheetDocument.Open(input.Path, false);
        var reader = new XlsxReader(document, ct);
        var sheets = reader.Sheets();
        if (sheets.Count == 0)
        {
            throw new ConversionException(ConversionErrorCode.CorruptFile, input.Path, "office", "no worksheets");
        }

        if (output == FormatRegistry.Csv)
        {
            var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < sheets.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var sheet = sheets[i];
                var path = sheets.Count == 1 ? outputPath : UniqueSheetPath(outputPath, sheet.Name, usedNames);
                outputs.Write(path, input.SizeBytes, temp =>
                {
                    using var writer = DocumentText.CreateWriter(temp, DocumentText.Utf8Bom, CsvNewLine);
                    foreach (var row in XlsxReader.TrimTrailingEmpty(reader.ReadRows(sheet.Part, ct)))
                    {
                        WriteCsvRow(writer, row);
                    }
                });
                progress.ReportUnit(i + 1, sheets.Count);
            }
            return;
        }

        outputs.Write(outputPath, input.SizeBytes, temp =>
        {
            using var writer = DocumentText.CreateWriter(temp, DocumentText.Utf8Bom, DocumentText.Crlf);
            for (var i = 0; i < sheets.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                if (sheets.Count > 1)
                {
                    if (i > 0)
                    {
                        writer.WriteLine();
                    }
                    writer.WriteLine($"--- {sheets[i].Name} ---");
                }
                foreach (var row in XlsxReader.TrimTrailingEmpty(reader.ReadRows(sheets[i].Part, ct)))
                {
                    writer.WriteLine(string.Join('\t', row.Select(TabSafe)));
                }
                progress.ReportUnit(i + 1, sheets.Count);
            }
        });
    }

    private static void ConvertPptx(InputInfo input, string outputPath, FormatId output, OutputSet outputs, IProgress<ConversionProgress>? progress, CancellationToken ct)
    {
        using var document = PresentationDocument.Open(input.Path, false);
        var slides = new PptxReader(document).Slides();
        var markdown = output == FormatRegistry.Markdown;
        outputs.Write(outputPath, input.SizeBytes, temp =>
        {
            using var writer = markdown
                ? DocumentText.CreateWriter(temp, DocumentText.Utf8)
                : DocumentText.CreateWriter(temp, DocumentText.Utf8Bom, DocumentText.Crlf);
            for (var i = 0; i < slides.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var number = (i + 1).ToString(CultureInfo.InvariantCulture);
                if (i > 0)
                {
                    writer.WriteLine();
                }
                writer.WriteLine(markdown ? $"## Slide {number}" : $"--- Slide {number} ---");
                foreach (var paragraph in PptxReader.SlideParagraphs(slides[i]))
                {
                    if (markdown)
                    {
                        writer.WriteLine();
                        writer.WriteLine(DocumentWriters.EscapeLineStart(DocumentWriters.EscapeMarkdown(paragraph)));
                    }
                    else
                    {
                        writer.WriteLine(paragraph.Replace("\n", DocumentText.Crlf, StringComparison.Ordinal));
                    }
                }
                progress.ReportUnit(i + 1, slides.Count);
            }
            if (slides.Count == 0)
            {
                writer.WriteLine();
            }
        });
    }

    private static string UniqueSheetPath(string outputPath, string sheetName, HashSet<string> usedNames)
    {
        var safe = DocumentPaths.SafeName(sheetName);
        var candidate = safe;
        for (var n = 2; !usedNames.Add(candidate); n++)
        {
            candidate = safe + "_" + n.ToString(CultureInfo.InvariantCulture);
        }
        return DocumentPaths.Suffixed(outputPath, "_" + candidate);
    }

    /// <summary>RFC 4180: fields with comma, quote, CR or LF are quoted; quotes are doubled.</summary>
    internal static void WriteCsvRow(TextWriter writer, IReadOnlyList<string> fields)
    {
        for (var i = 0; i < fields.Count; i++)
        {
            if (i > 0)
            {
                writer.Write(',');
            }
            writer.Write(CsvField(fields[i]));
        }
        writer.Write(CsvNewLine);
    }

    internal static string CsvField(string value)
    {
        if (value.AsSpan().IndexOfAny(",\"\r\n") < 0)
        {
            return value;
        }
        var sb = new StringBuilder(value.Length + 2);
        sb.Append('"').Append(value.Replace("\"", "\"\"", StringComparison.Ordinal)).Append('"');
        return sb.ToString();
    }

    private static string TabSafe(string value) =>
        value.Replace('\t', ' ').Replace("\r\n", " ", StringComparison.Ordinal).Replace('\n', ' ').Replace('\r', ' ');
}
