using System.Text;

namespace Kvertis.Engine.Conversion.Documents;

/// <summary>Writes the block model as plain text, Markdown or HTML.</summary>
internal static class DocumentWriters
{
    public static void WriteText(IEnumerable<DocBlock> blocks, TextWriter writer, CancellationToken ct)
    {
        foreach (var block in blocks)
        {
            ct.ThrowIfCancellationRequested();
            switch (block)
            {
                case DocHeading h:
                    writer.WriteLine(TextInline(h.Inlines).Replace("\n", writer.NewLine, StringComparison.Ordinal));
                    break;
                case DocParagraph p:
                    writer.WriteLine(TextInline(p.Inlines).Replace("\n", writer.NewLine, StringComparison.Ordinal));
                    break;
                case DocListItem li:
                    writer.Write(new string(' ', li.Level * 2));
                    writer.Write("- ");
                    writer.WriteLine(TextInline(li.Inlines).Replace("\n", writer.NewLine, StringComparison.Ordinal));
                    break;
                case DocTable t:
                    foreach (var row in t.Rows)
                    {
                        writer.WriteLine(string.Join('\t', row.Select(c => OneLine(c).Replace('\t', ' '))));
                    }
                    break;
                case DocCode c:
                    foreach (var line in SplitLines(c.Text))
                    {
                        writer.WriteLine(line);
                    }
                    break;
                case DocRule:
                    writer.WriteLine(new string('-', 20));
                    break;
            }
        }
    }

    public static void WriteMarkdown(IEnumerable<DocBlock> blocks, TextWriter writer, CancellationToken ct)
    {
        var first = true;
        var previousWasList = false;
        foreach (var block in blocks)
        {
            ct.ThrowIfCancellationRequested();
            if (block is DocParagraph { Inlines: var pi } && Inlines.IsBlank(pi))
            {
                continue; // Empty paragraphs are only spacing in the source.
            }
            var isList = block is DocListItem;
            if (!first && !(isList && previousWasList))
            {
                writer.WriteLine();
            }
            first = false;
            previousWasList = isList;

            switch (block)
            {
                case DocHeading h:
                    writer.Write(new string('#', Math.Clamp(h.Level, 1, 6)));
                    writer.Write(' ');
                    writer.WriteLine(MarkdownInline(h.Inlines).Replace('\n', ' '));
                    break;
                case DocParagraph p:
                    writer.WriteLine(EscapeLineStart(MarkdownInline(p.Inlines)));
                    break;
                case DocListItem li:
                    writer.Write(new string(' ', li.Level * 2));
                    writer.Write(li.Ordered ? "1. " : "- ");
                    writer.WriteLine(MarkdownInline(li.Inlines).Replace("\n", "\n" + new string(' ', li.Level * 2 + 2), StringComparison.Ordinal));
                    break;
                case DocTable t:
                    WriteMarkdownTable(t, writer);
                    break;
                case DocCode c:
                    var fence = c.Text.Contains("```", StringComparison.Ordinal) ? "~~~~" : "```";
                    writer.WriteLine(fence);
                    foreach (var line in SplitLines(c.Text))
                    {
                        writer.WriteLine(line);
                    }
                    writer.WriteLine(fence);
                    break;
                case DocRule:
                    writer.WriteLine("---");
                    break;
            }
        }
    }

    public static void WriteHtml(IEnumerable<DocBlock> blocks, TextWriter writer, string title, CancellationToken ct)
    {
        WriteHtmlHeader(writer, title);
        var listLevels = new Stack<bool>(); // true = ordered
        foreach (var block in blocks)
        {
            ct.ThrowIfCancellationRequested();
            if (block is DocListItem li)
            {
                while (listLevels.Count > li.Level + 1)
                {
                    writer.WriteLine(listLevels.Pop() ? "</ol>" : "</ul>");
                }
                while (listLevels.Count < li.Level + 1)
                {
                    listLevels.Push(li.Ordered);
                    writer.WriteLine(li.Ordered ? "<ol>" : "<ul>");
                }
                writer.Write("<li>");
                writer.Write(HtmlInline(li.Inlines));
                writer.WriteLine("</li>");
                continue;
            }
            while (listLevels.Count > 0)
            {
                writer.WriteLine(listLevels.Pop() ? "</ol>" : "</ul>");
            }

            switch (block)
            {
                case DocHeading h:
                    var level = Math.Clamp(h.Level, 1, 6);
                    writer.WriteLine($"<h{level}>{HtmlInline(h.Inlines)}</h{level}>");
                    break;
                case DocParagraph p:
                    if (!Inlines.IsBlank(p.Inlines))
                    {
                        writer.WriteLine($"<p>{HtmlInline(p.Inlines)}</p>");
                    }
                    break;
                case DocTable t:
                    writer.WriteLine("<table>");
                    for (var r = 0; r < t.Rows.Count; r++)
                    {
                        var tag = r == 0 ? "th" : "td";
                        writer.Write("<tr>");
                        foreach (var cell in t.Rows[r])
                        {
                            writer.Write($"<{tag}>{Html(cell).Replace("\n", "<br>", StringComparison.Ordinal)}</{tag}>");
                        }
                        writer.WriteLine("</tr>");
                    }
                    writer.WriteLine("</table>");
                    break;
                case DocCode c:
                    writer.WriteLine($"<pre><code>{Html(c.Text)}</code></pre>");
                    break;
                case DocRule:
                    writer.WriteLine("<hr>");
                    break;
            }
        }
        while (listLevels.Count > 0)
        {
            writer.WriteLine(listLevels.Pop() ? "</ol>" : "</ul>");
        }
        WriteHtmlFooter(writer);
    }

    public static void WriteHtmlHeader(TextWriter writer, string title)
    {
        writer.WriteLine("<!DOCTYPE html>");
        writer.WriteLine("<html>");
        writer.WriteLine("<head>");
        writer.WriteLine("<meta charset=\"utf-8\">");
        writer.WriteLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        writer.WriteLine($"<title>{Html(title)}</title>");
        writer.WriteLine("<style>body{font-family:sans-serif;max-width:50em;margin:2em auto;padding:0 1em;line-height:1.5}"
                         + "table{border-collapse:collapse}th,td{border:1px solid #999;padding:.25em .5em;text-align:left}"
                         + "pre{background:#f4f4f4;padding:.5em;overflow:auto}</style>");
        writer.WriteLine("</head>");
        writer.WriteLine("<body>");
    }

    public static void WriteHtmlFooter(TextWriter writer)
    {
        writer.WriteLine("</body>");
        writer.WriteLine("</html>");
    }

    /// <summary>
    /// Escapes the characters that are markup in HTML. Other characters stay as they are (the file is
    /// UTF-8), so umlauts remain readable in the source.
    /// </summary>
    public static string Html(string text)
    {
        if (text.AsSpan().IndexOfAny("<>&\"'") < 0)
        {
            return text;
        }
        var sb = new StringBuilder(text.Length + 16);
        foreach (var c in text)
        {
            sb.Append(c switch
            {
                '<' => "&lt;",
                '>' => "&gt;",
                '&' => "&amp;",
                '"' => "&quot;",
                '\'' => "&#39;",
                _ => c.ToString(),
            });
        }
        return sb.ToString();
    }

    public static string TextInline(IEnumerable<InlineRun> runs)
    {
        var sb = new StringBuilder();
        foreach (var run in runs)
        {
            sb.Append(run.Text);
            if (run.Url is { Length: > 0 } url && !string.Equals(run.Text.Trim(), url, StringComparison.Ordinal))
            {
                sb.Append(" (").Append(url).Append(')');
            }
        }
        return sb.ToString();
    }

    public static string MarkdownInline(IReadOnlyList<InlineRun> runs)
    {
        var sb = new StringBuilder();
        foreach (var group in Merge(runs))
        {
            var text = group.Style.HasFlag(InlineStyle.Code) ? CodeSpan(group.Text)
                : group.Style.HasFlag(InlineStyle.Literal) ? group.Text
                : EscapeMarkdown(group.Text);
            if (group.Url is { Length: > 0 } url)
            {
                text = $"[{text}]({url.Replace(" ", "%20", StringComparison.Ordinal).Replace(")", "%29", StringComparison.Ordinal)})";
            }
            var marker = (group.Style & (InlineStyle.Bold | InlineStyle.Italic)) switch
            {
                InlineStyle.Bold | InlineStyle.Italic => "***",
                InlineStyle.Bold => "**",
                InlineStyle.Italic => "*",
                _ => string.Empty,
            };
            AppendWrapped(sb, text, marker);
        }
        return sb.ToString();
    }

    public static string HtmlInline(IReadOnlyList<InlineRun> runs)
    {
        var sb = new StringBuilder();
        foreach (var group in Merge(runs))
        {
            var text = Html(group.Text).Replace("\n", "<br>", StringComparison.Ordinal);
            if (group.Style.HasFlag(InlineStyle.Code))
            {
                text = $"<code>{text}</code>";
            }
            if (group.Style.HasFlag(InlineStyle.Italic))
            {
                text = $"<em>{text}</em>";
            }
            if (group.Style.HasFlag(InlineStyle.Bold))
            {
                text = $"<strong>{text}</strong>";
            }
            if (group.Url is { Length: > 0 } url && IsSafeHref(url))
            {
                text = $"<a href=\"{Html(url)}\">{text}</a>";
            }
            sb.Append(text);
        }
        return sb.ToString();
    }

    /// <summary>Only plain web, mail and fragment links are written as href; script URLs never.</summary>
    private static bool IsSafeHref(string url) =>
        url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
        || url.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) || url.StartsWith('#');

    /// <summary>Joins adjacent runs with identical style and link so markers are not repeated.</summary>
    private static IEnumerable<InlineRun> Merge(IReadOnlyList<InlineRun> runs)
    {
        InlineRun? current = null;
        foreach (var run in runs)
        {
            if (run.Text.Length == 0)
            {
                continue;
            }
            if (current is not null && current.Style == run.Style && current.Url == run.Url)
            {
                current = current with { Text = current.Text + run.Text };
                continue;
            }
            if (current is not null)
            {
                yield return current;
            }
            current = run;
        }
        if (current is not null)
        {
            yield return current;
        }
    }

    /// <summary>Emphasis markers must hug non-space text, so surrounding whitespace moves outside.</summary>
    private static void AppendWrapped(StringBuilder sb, string text, string marker)
    {
        if (marker.Length == 0 || string.IsNullOrWhiteSpace(text))
        {
            sb.Append(text);
            return;
        }
        var start = 0;
        while (start < text.Length && char.IsWhiteSpace(text[start]))
        {
            start++;
        }
        var end = text.Length;
        while (end > start && char.IsWhiteSpace(text[end - 1]))
        {
            end--;
        }
        sb.Append(text, 0, start).Append(marker).Append(text, start, end - start).Append(marker).Append(text, end, text.Length - end);
    }

    private static string CodeSpan(string text)
    {
        var ticks = text.Contains('`', StringComparison.Ordinal) ? "``" : "`";
        return ticks + (ticks.Length == 2 ? " " + text + " " : text) + ticks;
    }

    public static string EscapeMarkdown(string text)
    {
        var sb = new StringBuilder(text.Length + 8);
        foreach (var c in text)
        {
            if (c is '\\' or '*' or '_' or '`' or '[' or ']' or '<' or '>')
            {
                sb.Append('\\');
            }
            sb.Append(c);
        }
        return sb.ToString().Replace("\n", "  \n", StringComparison.Ordinal);
    }

    /// <summary>Escapes characters at the start of a paragraph that would turn it into another block.</summary>
    public static string EscapeLineStart(string line)
    {
        if (line.Length == 0)
        {
            return line;
        }
        if (line[0] is '#' or '-' or '+' or '=' or '|' or '~')
        {
            return "\\" + line;
        }
        var digits = 0;
        while (digits < line.Length && char.IsAsciiDigit(line[digits]))
        {
            digits++;
        }
        if (digits > 0 && digits < line.Length && line[digits] is '.' or ')')
        {
            return line[..digits] + "\\" + line[digits..];
        }
        return line;
    }

    private static void WriteMarkdownTable(DocTable table, TextWriter writer)
    {
        if (table.Rows.Count == 0)
        {
            return;
        }
        var columns = table.Rows.Max(r => r.Count);
        if (columns == 0)
        {
            return;
        }
        for (var r = 0; r < table.Rows.Count; r++)
        {
            var row = table.Rows[r];
            writer.Write('|');
            for (var c = 0; c < columns; c++)
            {
                var cell = c < row.Count ? row[c] : string.Empty;
                writer.Write(' ');
                writer.Write(EscapeMarkdown(OneLine(cell)).Replace("|", "\\|", StringComparison.Ordinal));
                writer.Write(" |");
            }
            writer.WriteLine();
            if (r == 0)
            {
                writer.Write('|');
                for (var c = 0; c < columns; c++)
                {
                    writer.Write(" --- |");
                }
                writer.WriteLine();
            }
        }
    }

    private static string OneLine(string text) => text.Replace("\r\n", " ", StringComparison.Ordinal).Replace('\n', ' ').Replace('\r', ' ');

    public static IEnumerable<string> SplitLines(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n').Split('\n');
}
