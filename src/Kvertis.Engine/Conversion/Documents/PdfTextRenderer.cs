using System.Globalization;
using System.Text;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace Kvertis.Engine.Conversion.Documents;

/// <summary>Page sizes for text-to-PDF. Values in PDF points (1/72 inch).</summary>
internal enum PdfPageSize
{
    A4,
    Letter,
}

/// <summary>
/// Minimal flow layout for text-to-PDF: word wrapping, page breaks, headings, lists, code and simple
/// tables. 11 pt body text, 2 cm margins. No images; links are underlined.
/// </summary>
internal sealed class PdfTextRenderer : IDisposable
{
    private const double BodySize = 11;
    private const double CodeSize = 10;
    private const double Margin = 2 / 2.54 * 72; // 2 cm
    private const double LineFactor = 1.3;
    private const double IndentPerLevel = 18;
    private const int TabWidth = 4;

    private readonly PdfDocument _document;
    private readonly double _pageWidth;
    private readonly double _pageHeight;
    private readonly Dictionary<(string Family, double Size, XFontStyleEx Style), XFont> _fonts = [];
    private PdfPage? _page;
    private XGraphics? _gfx;
    private double _y;

    public PdfTextRenderer(PdfPageSize size)
    {
        DocumentFonts.EnsureInitialized();
        (_pageWidth, _pageHeight) = size == PdfPageSize.Letter ? (612.0, 792.0) : (595.28, 841.89);
        _document = new PdfDocument();
        _document.Info.Creator = "Kvertis";
    }

    public static PdfPageSize ParsePageSize(string? value) =>
        string.Equals(value?.Trim(), "Letter", StringComparison.OrdinalIgnoreCase) ? PdfPageSize.Letter : PdfPageSize.A4;

    public int PageCount => _document.PageCount;

    private double ContentWidth => _pageWidth - 2 * Margin;
    private double Bottom => _pageHeight - Margin;

    private XGraphics Gfx
    {
        get
        {
            if (_gfx is null)
            {
                NewPage();
            }
            return _gfx!;
        }
    }

    public void NewPage()
    {
        _gfx?.Dispose();
        _page = _document.AddPage();
        _page.Width = XUnit.FromPoint(_pageWidth);
        _page.Height = XUnit.FromPoint(_pageHeight);
        _gfx = XGraphics.FromPdfPage(_page);
        _y = Margin;
    }

    /// <summary>One line of plain text in the monospace font, wrapped at the right margin.</summary>
    public void AddPlainLine(string line)
    {
        var font = Font(DocumentFonts.Mono, BodySize, XFontStyleEx.Regular);
        var text = ExpandTabs(StripControl(line));
        var lineHeight = BodySize * LineFactor;
        if (text.Length == 0)
        {
            Advance(lineHeight);
            return;
        }
        foreach (var part in WrapByWidth(text, font, ContentWidth))
        {
            EnsureRoom(lineHeight);
            Gfx.DrawString(part, font, XBrushes.Black, Margin, _y, XStringFormats.TopLeft);
            _y += lineHeight;
        }
    }

    public void AddBlock(DocBlock block)
    {
        switch (block)
        {
            case DocHeading h:
                var size = h.Level switch { 1 => 20, 2 => 16, 3 => 14, 4 => 12.5, _ => BodySize };
                Advance(size * 0.6);
                FlowInlines(h.Inlines, Margin, ContentWidth, size, InlineStyle.Bold);
                Advance(size * 0.3);
                break;
            case DocParagraph p:
                if (!Inlines.IsBlank(p.Inlines))
                {
                    FlowInlines(p.Inlines, Margin, ContentWidth, BodySize, InlineStyle.None);
                }
                Advance(BodySize * 0.5);
                break;
            case DocListItem li:
                var indent = IndentPerLevel * (li.Level + 1);
                var marker = li.Ordered ? li.Number.ToString(CultureInfo.InvariantCulture) + "." : "•";
                FlowInlines(li.Inlines, Margin + indent, ContentWidth - indent, BodySize, InlineStyle.None, marker);
                Advance(BodySize * 0.2);
                break;
            case DocCode c:
                var codeFont = Font(DocumentFonts.Mono, CodeSize, XFontStyleEx.Regular);
                foreach (var line in DocumentWriters.SplitLines(c.Text))
                {
                    foreach (var part in WrapByWidth(ExpandTabs(StripControl(line)), codeFont, ContentWidth - 8))
                    {
                        EnsureRoom(CodeSize * LineFactor);
                        Gfx.DrawString(part, codeFont, XBrushes.Black, Margin + 8, _y, XStringFormats.TopLeft);
                        _y += CodeSize * LineFactor;
                    }
                }
                Advance(BodySize * 0.5);
                break;
            case DocTable t:
                DrawTable(t);
                Advance(BodySize * 0.5);
                break;
            case DocRule:
                EnsureRoom(BodySize);
                Gfx.DrawLine(XPens.Gray, Margin, _y + BodySize / 2, Margin + ContentWidth, _y + BodySize / 2);
                Advance(BodySize);
                break;
        }
    }

    public void Save(string path)
    {
        if (_document.PageCount == 0)
        {
            NewPage(); // An empty input still yields a valid one-page PDF.
        }
        _gfx?.Dispose();
        _gfx = null;
        _document.Save(path);
    }

    public void Dispose()
    {
        _gfx?.Dispose();
        _document.Dispose();
    }

    private sealed record Segment(string Text, XFont Font, bool Underline, double Width);

    /// <summary>Lays out styled runs with word wrapping. An optional marker is drawn left of the first line.</summary>
    private void FlowInlines(IReadOnlyList<InlineRun> runs, double left, double width, double size, InlineStyle extra, string? marker = null)
    {
        var line = new List<Segment>();
        var lineWidth = 0.0;
        var lineHeight = size * LineFactor;
        var first = true;

        void FlushLine()
        {
            EnsureRoom(lineHeight);
            if (first && marker is not null)
            {
                var markerFont = Font(DocumentFonts.Sans, size, XFontStyleEx.Regular);
                Gfx.DrawString(marker, markerFont, XBrushes.Black, left - Gfx.MeasureString(marker + " ", markerFont).Width, _y, XStringFormats.TopLeft);
            }
            var x = left;
            foreach (var seg in line)
            {
                Gfx.DrawString(seg.Text, seg.Font, XBrushes.Black, x, _y, XStringFormats.TopLeft);
                if (seg.Underline)
                {
                    var uy = _y + seg.Font.Size * 1.08;
                    Gfx.DrawLine(new XPen(XColors.Black, 0.5), x, uy, x + seg.Width, uy);
                }
                x += seg.Width;
            }
            _y += lineHeight;
            line.Clear();
            lineWidth = 0;
            first = false;
        }

        foreach (var run in runs)
        {
            var style = run.Style | extra;
            var family = style.HasFlag(InlineStyle.Code) ? DocumentFonts.Mono : DocumentFonts.Sans;
            var fontStyle = (style.HasFlag(InlineStyle.Bold), style.HasFlag(InlineStyle.Italic)) switch
            {
                (true, true) => XFontStyleEx.BoldItalic,
                (true, false) => XFontStyleEx.Bold,
                (false, true) => XFontStyleEx.Italic,
                _ => XFontStyleEx.Regular,
            };
            var font = Font(family, style.HasFlag(InlineStyle.Code) ? size * 0.92 : size, fontStyle);
            var underline = run.Url is not null;

            foreach (var token in Tokenize(StripControl(run.Text)))
            {
                if (token == "\n")
                {
                    FlushLine();
                    continue;
                }
                var isSpace = token == " ";
                if (isSpace && line.Count == 0)
                {
                    continue; // No leading spaces on wrapped lines.
                }
                var w = Gfx.MeasureString(token, font).Width;
                if (!isSpace && lineWidth + w > width && line.Count > 0)
                {
                    TrimTrailingSpace(line, ref lineWidth);
                    FlushLine();
                }
                if (!isSpace && w > width)
                {
                    // A single word wider than the line: break it by characters.
                    foreach (var piece in WrapByWidth(token, font, width))
                    {
                        if (line.Count > 0)
                        {
                            FlushLine();
                        }
                        var pw = Gfx.MeasureString(piece, font).Width;
                        line.Add(new Segment(piece, font, underline, pw));
                        lineWidth += pw;
                    }
                    continue;
                }
                line.Add(new Segment(token, font, underline, w));
                lineWidth += w;
            }
        }
        if (line.Count > 0 || first)
        {
            TrimTrailingSpace(line, ref lineWidth);
            FlushLine();
        }
    }

    private static void TrimTrailingSpace(List<Segment> line, ref double lineWidth)
    {
        while (line.Count > 0 && line[^1].Text == " ")
        {
            lineWidth -= line[^1].Width;
            line.RemoveAt(line.Count - 1);
        }
    }

    /// <summary>Splits text into words, single spaces and line breaks.</summary>
    private static IEnumerable<string> Tokenize(string text)
    {
        var sb = new StringBuilder();
        foreach (var c in text)
        {
            if (c is ' ' or '\t' or '\n')
            {
                if (sb.Length > 0)
                {
                    yield return sb.ToString();
                    sb.Clear();
                }
                yield return c == '\n' ? "\n" : " ";
            }
            else
            {
                sb.Append(c);
            }
        }
        if (sb.Length > 0)
        {
            yield return sb.ToString();
        }
    }

    private void DrawTable(DocTable table)
    {
        var columns = table.Rows.Count == 0 ? 0 : table.Rows.Max(r => r.Count);
        if (columns == 0)
        {
            return;
        }
        const double padding = 3;
        var colWidth = ContentWidth / columns;
        var pen = new XPen(XColors.Gray, 0.5);
        for (var r = 0; r < table.Rows.Count; r++)
        {
            var font = Font(DocumentFonts.Sans, BodySize - 1, r == 0 ? XFontStyleEx.Bold : XFontStyleEx.Regular);
            var lineHeight = (BodySize - 1) * LineFactor;
            var cells = new List<List<string>>();
            for (var c = 0; c < columns; c++)
            {
                var text = c < table.Rows[r].Count ? StripControl(table.Rows[r][c]) : string.Empty;
                var lines = new List<string>();
                foreach (var para in text.Split('\n'))
                {
                    lines.AddRange(WrapWords(para, font, colWidth - 2 * padding));
                }
                cells.Add(lines);
            }
            var rowHeight = Math.Max(1, cells.Max(l => l.Count)) * lineHeight + 2 * padding;
            if (rowHeight <= Bottom - Margin)
            {
                EnsureRoom(rowHeight);
            }
            for (var c = 0; c < columns; c++)
            {
                var x = Margin + c * colWidth;
                Gfx.DrawRectangle(pen, x, _y, colWidth, rowHeight);
                var y = _y + padding;
                foreach (var l in cells[c])
                {
                    Gfx.DrawString(l, font, XBrushes.Black, x + padding, y, XStringFormats.TopLeft);
                    y += lineHeight;
                }
            }
            _y += rowHeight;
        }
    }

    private List<string> WrapWords(string text, XFont font, double width)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = current.Length == 0 ? word : current + " " + word;
            if (Gfx.MeasureString(candidate, font).Width <= width || current.Length == 0)
            {
                if (current.Length == 0 && Gfx.MeasureString(word, font).Width > width)
                {
                    var pieces = WrapByWidth(word, font, width);
                    result.AddRange(pieces.Take(pieces.Count - 1));
                    current.Clear().Append(pieces[^1]);
                    continue;
                }
                current.Clear().Append(candidate);
            }
            else
            {
                result.Add(current.ToString());
                current.Clear().Append(word);
            }
        }
        if (current.Length > 0 || result.Count == 0)
        {
            result.Add(current.ToString());
        }
        return result;
    }

    /// <summary>Breaks text into pieces that fit the width, by characters (for monospace and long words).</summary>
    private List<string> WrapByWidth(string text, XFont font, double width)
    {
        var result = new List<string>();
        if (text.Length == 0)
        {
            result.Add(string.Empty);
            return result;
        }
        var start = 0;
        while (start < text.Length)
        {
            // Estimate with the average character width, then correct by measuring.
            var avg = Math.Max(1, Gfx.MeasureString("M", font).Width);
            var count = Math.Clamp((int)(width / avg), 1, text.Length - start);
            while (start + count < text.Length && Gfx.MeasureString(text.Substring(start, count + 1), font).Width <= width)
            {
                count++;
            }
            while (count > 1 && Gfx.MeasureString(text.Substring(start, count), font).Width > width)
            {
                count--;
            }
            if (char.IsHighSurrogate(text[start + count - 1]) && start + count < text.Length)
            {
                count = count > 1 ? count - 1 : count + 1;
            }
            result.Add(text.Substring(start, count));
            start += count;
        }
        return result;
    }

    private void EnsureRoom(double height)
    {
        if (_gfx is null || _y + height > Bottom)
        {
            NewPage();
        }
    }

    private void Advance(double height)
    {
        if (_gfx is null)
        {
            NewPage();
        }
        _y += height;
    }

    private XFont Font(string family, double size, XFontStyleEx style)
    {
        var key = (family, size, style);
        if (!_fonts.TryGetValue(key, out var font))
        {
            font = new XFont(family, size, style, new XPdfFontOptions(PdfFontEncoding.Unicode));
            _fonts[key] = font;
        }
        return font;
    }

    private static string ExpandTabs(string text)
    {
        if (!text.Contains('\t', StringComparison.Ordinal))
        {
            return text;
        }
        var sb = new StringBuilder(text.Length + 8);
        foreach (var c in text)
        {
            if (c == '\t')
            {
                sb.Append(' ', TabWidth - sb.Length % TabWidth);
            }
            else
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }

    /// <summary>Removes control characters that fonts cannot draw; keeps tabs and line feeds.</summary>
    private static string StripControl(string text)
    {
        var needs = false;
        foreach (var c in text)
        {
            if (char.IsControl(c) && c is not '\t' and not '\n')
            {
                needs = true;
                break;
            }
        }
        if (!needs)
        {
            return text;
        }
        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (!char.IsControl(c) || c is '\t' or '\n')
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }
}
