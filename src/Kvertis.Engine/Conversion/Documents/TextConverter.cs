using System.Diagnostics;
using System.Text;
using Kvertis.Engine.Abstractions;
using Kvertis.Engine.Formats;

namespace Kvertis.Engine.Conversion.Documents;

/// <summary>
/// Text formats: TXT/Markdown → PDF/HTML, Markdown ↔ TXT, HTML → TXT/Markdown, CSV → TXT.
/// Inputs are decoded with <see cref="TextEncodingDetector"/>. HTML is read from the local file only.
/// </summary>
public sealed class TextConverter : IConverter
{
    public string Name => "text";

    public bool Supports(InputInfo input, FormatId output)
    {
        ArgumentNullException.ThrowIfNull(input);
        var f = input.Format;
        if (f == FormatRegistry.Txt)
        {
            return output == FormatRegistry.Pdf || output == FormatRegistry.Html || output == FormatRegistry.Markdown;
        }
        if (f == FormatRegistry.Markdown)
        {
            return output == FormatRegistry.Pdf || output == FormatRegistry.Html || output == FormatRegistry.Txt;
        }
        if (f == FormatRegistry.Html)
        {
            return output == FormatRegistry.Txt || output == FormatRegistry.Markdown;
        }
        if (f == FormatRegistry.Csv)
        {
            return output == FormatRegistry.Txt;
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
            throw new ConversionException(ConversionErrorCode.UnsupportedFormat, input.Path, "text", $"{input.Format} -> {settings.Output}");
        }
        return DocumentTasks.RunAsync(() => Convert(input, outputPath, settings, progress, ct), input.Path, "text");
    }

    public Task<PreviewResult?> PreviewAsync(InputInfo input, ConversionSettings settings, CancellationToken ct) =>
        Task.FromResult<PreviewResult?>(null);

    private static ConversionResult Convert(InputInfo input, string outputPath, ConversionSettings settings, IProgress<ConversionProgress>? progress, CancellationToken ct)
    {
        var watch = Stopwatch.StartNew();
        progress?.Report(ConversionProgress.Start);
        ct.ThrowIfCancellationRequested();
        var inFormat = input.Format;
        var output = settings.Output;
        var title = Path.GetFileNameWithoutExtension(input.Path);

        using var outputs = new OutputSet();
        outputs.Write(outputPath, input.SizeBytes * 2, temp =>
        {
            if (output == FormatRegistry.Pdf)
            {
                var pageSize = PdfTextRenderer.ParsePageSize(settings.GetAdvanced(ConversionSettings.AdvancedKeys.PageSize));
                using var renderer = new PdfTextRenderer(pageSize);
                if (inFormat == FormatRegistry.Markdown)
                {
                    var blocks = MarkdownSupport.ToBlocks(TextEncodingDetector.ReadAllText(input.Path));
                    for (var i = 0; i < blocks.Count; i++)
                    {
                        ct.ThrowIfCancellationRequested();
                        renderer.AddBlock(blocks[i]);
                        if (i % 50 == 0)
                        {
                            progress.ReportUnit(i + 1, blocks.Count);
                        }
                    }
                }
                else
                {
                    RenderPlainText(input.Path, renderer, progress, ct);
                }
                renderer.Save(temp);
            }
            else if (output == FormatRegistry.Html)
            {
                using var writer = DocumentText.CreateWriter(temp, DocumentText.Utf8);
                DocumentWriters.WriteHtmlHeader(writer, title);
                if (inFormat == FormatRegistry.Markdown)
                {
                    writer.Write(MarkdownSupport.ToHtmlFragment(TextEncodingDetector.ReadAllText(input.Path)));
                }
                else
                {
                    writer.Write("<pre>");
                    using var reader = TextEncodingDetector.OpenReader(input.Path);
                    string? line;
                    while ((line = reader.ReadLine()) is not null)
                    {
                        ct.ThrowIfCancellationRequested();
                        writer.WriteLine(DocumentWriters.Html(line));
                    }
                    writer.WriteLine("</pre>");
                }
                DocumentWriters.WriteHtmlFooter(writer);
            }
            else if (output == FormatRegistry.Markdown)
            {
                using var writer = DocumentText.CreateWriter(temp, DocumentText.Utf8);
                if (inFormat == FormatRegistry.Html)
                {
                    DocumentWriters.WriteMarkdown(HtmlTextParser.Parse(TextEncodingDetector.ReadAllText(input.Path)), writer, ct);
                }
                else
                {
                    PlainTextToMarkdown(input.Path, writer, ct);
                }
            }
            else
            {
                using var writer = DocumentText.CreateWriter(temp, DocumentText.Utf8Bom, DocumentText.Crlf);
                if (inFormat == FormatRegistry.Markdown)
                {
                    var plain = MarkdownSupport.ToPlainText(TextEncodingDetector.ReadAllText(input.Path));
                    foreach (var line in DocumentWriters.SplitLines(plain))
                    {
                        writer.WriteLine(line);
                    }
                }
                else if (inFormat == FormatRegistry.Html)
                {
                    DocumentWriters.WriteText(HtmlTextParser.Parse(TextEncodingDetector.ReadAllText(input.Path)), writer, ct);
                }
                else
                {
                    using var reader = TextEncodingDetector.OpenReader(input.Path);
                    CsvToTabSeparated(reader, writer, ct);
                }
            }
        });
        progress?.Report(ConversionProgress.Complete);
        return outputs.Complete(input, watch);
    }

    private static void RenderPlainText(string path, PdfTextRenderer renderer, IProgress<ConversionProgress>? progress, CancellationToken ct)
    {
        using var reader = TextEncodingDetector.OpenReader(path);
        var length = Math.Max(1, reader.BaseStream.Length);
        var lineNo = 0;
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            ct.ThrowIfCancellationRequested();
            // A form feed starts a new page, as in printed plain text.
            var parts = line.Split('\f');
            for (var p = 0; p < parts.Length; p++)
            {
                if (p > 0)
                {
                    renderer.NewPage();
                }
                if (p < parts.Length - 1 && parts[p].Length == 0)
                {
                    continue;
                }
                renderer.AddPlainLine(parts[p]);
            }
            if (++lineNo % 200 == 0)
            {
                progress.ReportUnit((int)(reader.BaseStream.Position * 1000 / length), 1000);
            }
        }
    }

    /// <summary>Plain text as Markdown: special characters escaped, line breaks kept as hard breaks.</summary>
    private static void PlainTextToMarkdown(string path, TextWriter writer, CancellationToken ct)
    {
        using var reader = TextEncodingDetector.OpenReader(path);
        string? previous = null;
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            ct.ThrowIfCancellationRequested();
            if (previous is not null)
            {
                WriteMarkdownLine(writer, previous, hardBreak: line.Trim().Length > 0);
            }
            previous = line;
        }
        if (previous is not null)
        {
            WriteMarkdownLine(writer, previous, hardBreak: false);
        }
    }

    private static void WriteMarkdownLine(TextWriter writer, string line, bool hardBreak)
    {
        if (line.Trim().Length == 0)
        {
            writer.WriteLine();
            return;
        }
        var escaped = DocumentWriters.EscapeLineStart(DocumentWriters.EscapeMarkdown(line.TrimEnd()));
        // Leading spaces would turn into a code block; keep them as non-breaking spaces.
        var indent = 0;
        while (indent < escaped.Length && escaped[indent] == ' ')
        {
            indent++;
        }
        if (indent > 0)
        {
            escaped = new string(' ', indent) + escaped[indent..];
        }
        writer.WriteLine(hardBreak ? escaped + "  " : escaped);
    }

    /// <summary>RFC 4180 reader (quoted fields may contain delimiters, quotes and line breaks) → tab-separated.</summary>
    internal static void CsvToTabSeparated(TextReader reader, TextWriter writer, CancellationToken ct)
    {
        var firstLine = reader.ReadLine();
        if (firstLine is null)
        {
            writer.WriteLine();
            return;
        }
        var delimiter = DetectDelimiter(firstLine);
        using var combined = new StringReader(firstLine + "\n");
        foreach (var row in ReadCsvRows(new ConcatReader(combined, reader), delimiter, ct))
        {
            writer.WriteLine(string.Join('\t', row.Select(f => f.Replace('\t', ' ').Replace("\r\n", " ", StringComparison.Ordinal).Replace('\n', ' ').Replace('\r', ' '))));
        }
    }

    internal static char DetectDelimiter(string firstLine)
    {
        int comma = 0, semicolon = 0, tab = 0;
        var inQuotes = false;
        foreach (var c in firstLine)
        {
            if (c == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (!inQuotes)
            {
                switch (c)
                {
                    case ',':
                        comma++;
                        break;
                    case ';':
                        semicolon++;
                        break;
                    case '\t':
                        tab++;
                        break;
                }
            }
        }
        if (semicolon > comma && semicolon >= tab)
        {
            return ';';
        }
        return tab > comma ? '\t' : ',';
    }

    internal static IEnumerable<List<string>> ReadCsvRows(TextReader reader, char delimiter, CancellationToken ct)
    {
        var row = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        var fieldStarted = false;
        var count = 0;
        int ch;
        while ((ch = reader.Read()) >= 0)
        {
            if (++count % 65536 == 0)
            {
                ct.ThrowIfCancellationRequested();
            }
            var c = (char)ch;
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (reader.Peek() == '"')
                    {
                        reader.Read();
                        field.Append('"');
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    field.Append(c);
                }
                continue;
            }
            if (c == '"' && field.Length == 0)
            {
                inQuotes = true;
                fieldStarted = true;
            }
            else if (c == delimiter)
            {
                row.Add(field.ToString());
                field.Clear();
                fieldStarted = true;
            }
            else if (c is '\r' or '\n')
            {
                if (c == '\r' && reader.Peek() == '\n')
                {
                    reader.Read();
                }
                row.Add(field.ToString());
                field.Clear();
                yield return row;
                row = [];
                fieldStarted = false;
            }
            else
            {
                field.Append(c);
                fieldStarted = true;
            }
        }
        if (fieldStarted || field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            yield return row;
        }
    }

    /// <summary>Reads one reader to its end, then the next. Used to re-join a peeked first line.</summary>
    private sealed class ConcatReader(TextReader first, TextReader second) : TextReader
    {
        private bool _firstDone;

        private TextReader Current => _firstDone ? second : first;

        public override int Peek()
        {
            var c = Current.Peek();
            if (c < 0 && !_firstDone)
            {
                _firstDone = true;
                return second.Peek();
            }
            return c;
        }

        public override int Read()
        {
            var c = Current.Read();
            if (c < 0 && !_firstDone)
            {
                _firstDone = true;
                return second.Read();
            }
            return c;
        }
    }
}
