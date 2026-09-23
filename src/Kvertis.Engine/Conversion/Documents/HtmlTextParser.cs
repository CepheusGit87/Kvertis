using System.Net;
using System.Text;

namespace Kvertis.Engine.Conversion.Documents;

/// <summary>
/// Small, forgiving HTML reader for HTML → TXT/Markdown. It only looks at the markup of the local
/// file: scripts, styles and other embedded content are dropped, and nothing referenced by the page
/// (images, style sheets, frames) is ever loaded.
/// </summary>
internal sealed class HtmlTextParser
{
    /// <summary>Elements whose content is never text for the reader.</summary>
    private static readonly HashSet<string> SkippedElements = new(StringComparer.Ordinal)
    {
        "script", "style", "title", "noscript", "template", "iframe", "object", "embed", "svg", "math",
        "canvas", "audio", "video", "select", "textarea", "button",
    };

    private static readonly HashSet<string> BlockElements = new(StringComparer.Ordinal)
    {
        "p", "div", "section", "article", "header", "footer", "main", "nav", "aside", "blockquote", "figure",
        "figcaption", "form", "fieldset", "address", "dl", "dt", "dd", "h1", "h2", "h3", "h4", "h5", "h6",
        "li", "ul", "ol", "pre", "table", "tr", "hr", "body", "html", "head", "caption", "details", "summary",
    };

    private readonly List<DocBlock> _blocks = [];
    private readonly List<InlineRun> _runs = [];
    private readonly Stack<string?> _links = new();
    private readonly Stack<(bool Ordered, int Counter)> _lists = new();
    private int _bold;
    private int _italic;
    private int _code;
    private int _heading;
    private bool _inListItem;
    private int _preDepth;
    private readonly StringBuilder _pre = new();

    private int _tableDepth;
    private List<IReadOnlyList<string>>? _tableRows;
    private List<string>? _row;
    private StringBuilder? _cell;

    public static IReadOnlyList<DocBlock> Parse(string html)
    {
        var parser = new HtmlTextParser();
        parser.Run(html);
        return parser._blocks;
    }

    private void Run(string html)
    {
        var i = 0;
        while (i < html.Length)
        {
            var lt = html.IndexOf('<', i);
            if (lt < 0)
            {
                Text(html[i..]);
                break;
            }
            if (lt > i)
            {
                Text(html[i..lt]);
            }
            i = lt;

            if (string.CompareOrdinal(html, i, "<!--", 0, 4) == 0)
            {
                var end = html.IndexOf("-->", i + 4, StringComparison.Ordinal);
                i = end < 0 ? html.Length : end + 3;
                continue;
            }
            if (i + 1 < html.Length && html[i + 1] is '!' or '?')
            {
                var end = html.IndexOf('>', i);
                i = end < 0 ? html.Length : end + 1;
                continue;
            }
            var closing = i + 1 < html.Length && html[i + 1] == '/';
            var nameStart = closing ? i + 2 : i + 1;
            if (nameStart >= html.Length || !char.IsAsciiLetter(html[nameStart]))
            {
                Text("<");
                i++;
                continue;
            }
            var nameEnd = nameStart;
            while (nameEnd < html.Length && (char.IsAsciiLetterOrDigit(html[nameEnd]) || html[nameEnd] is '-' or ':'))
            {
                nameEnd++;
            }
            var name = html[nameStart..nameEnd].ToLowerInvariant();
            var tagEnd = FindTagEnd(html, nameEnd);
            var attributes = closing ? string.Empty : html[nameEnd..Math.Min(tagEnd, html.Length)];
            i = Math.Min(tagEnd + 1, html.Length);

            if (closing)
            {
                EndTag(name);
                continue;
            }
            if (SkippedElements.Contains(name) && !attributes.TrimEnd().EndsWith('/'))
            {
                i = SkipRawContent(html, i, name);
                continue;
            }
            StartTag(name, attributes);
        }
        FlushParagraph();
        FlushTable(force: true);
    }

    /// <summary>Index of the '>' closing a tag, honoring quoted attribute values.</summary>
    private static int FindTagEnd(string html, int from)
    {
        char? quote = null;
        for (var j = from; j < html.Length; j++)
        {
            var c = html[j];
            if (quote is not null)
            {
                if (c == quote)
                {
                    quote = null;
                }
            }
            else if (c is '"' or '\'')
            {
                quote = c;
            }
            else if (c == '>')
            {
                return j;
            }
        }
        return html.Length;
    }

    private static int SkipRawContent(string html, int from, string name)
    {
        var end = html.IndexOf("</" + name, from, StringComparison.OrdinalIgnoreCase);
        if (end < 0)
        {
            return html.Length;
        }
        var close = html.IndexOf('>', end);
        return close < 0 ? html.Length : close + 1;
    }

    private void StartTag(string name, string attributes)
    {
        switch (name)
        {
            case "br":
                if (_preDepth > 0)
                {
                    _pre.Append('\n');
                }
                else if (_cell is not null)
                {
                    _cell.Append('\n');
                }
                else
                {
                    _runs.Add(new InlineRun("\n", CurrentStyle(), CurrentLink()));
                }
                return;
            case "img":
                var alt = Attribute(attributes, "alt");
                if (!string.IsNullOrWhiteSpace(alt))
                {
                    Text($" [image: {alt.Trim()}] ", decode: false);
                }
                return;
            case "b" or "strong":
                _bold++;
                return;
            case "i" or "em":
                _italic++;
                return;
            case "code" or "kbd" or "samp" or "tt":
                _code++;
                return;
            case "a":
                var href = Attribute(attributes, "href")?.Trim();
                _links.Push(href is { Length: > 0 } && !href.StartsWith('#') && !href.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase) ? href : null);
                return;
        }

        if (!BlockElements.Contains(name) && name is not ("td" or "th"))
        {
            return;
        }

        if (name is "table")
        {
            FlushParagraph();
            _tableDepth++;
            if (_tableDepth == 1)
            {
                _tableRows = [];
            }
            return;
        }
        if (_tableDepth > 0)
        {
            TableStart(name);
            return;
        }

        FlushParagraph();
        switch (name)
        {
            case "h1" or "h2" or "h3" or "h4" or "h5" or "h6":
                _heading = name[1] - '0';
                break;
            case "ul" or "ol":
                _lists.Push((name == "ol", 0));
                _inListItem = false;
                break;
            case "li":
                _inListItem = true;
                if (_lists.Count > 0)
                {
                    var top = _lists.Pop();
                    _lists.Push((top.Ordered, top.Counter + 1));
                }
                break;
            case "pre":
                _preDepth++;
                break;
            case "hr":
                _blocks.Add(new DocRule());
                break;
        }
    }

    private void EndTag(string name)
    {
        switch (name)
        {
            case "b" or "strong":
                _bold = Math.Max(0, _bold - 1);
                return;
            case "i" or "em":
                _italic = Math.Max(0, _italic - 1);
                return;
            case "code" or "kbd" or "samp" or "tt":
                _code = Math.Max(0, _code - 1);
                return;
            case "a":
                if (_links.Count > 0)
                {
                    _links.Pop();
                }
                return;
        }

        if (name is "table")
        {
            if (_tableDepth > 0)
            {
                _tableDepth--;
                if (_tableDepth == 0)
                {
                    FlushTable(force: true);
                }
            }
            return;
        }
        if (_tableDepth > 0)
        {
            TableEnd(name);
            return;
        }
        if (!BlockElements.Contains(name))
        {
            return;
        }

        if (name == "pre" && _preDepth > 0)
        {
            _preDepth--;
            if (_preDepth == 0)
            {
                var text = _pre.ToString().Trim('\n', '\r');
                _pre.Clear();
                if (text.Length > 0)
                {
                    _blocks.Add(new DocCode(text));
                }
            }
            return;
        }

        FlushParagraph();
        switch (name)
        {
            case "h1" or "h2" or "h3" or "h4" or "h5" or "h6":
                _heading = 0;
                break;
            case "ul" or "ol":
                if (_lists.Count > 0)
                {
                    _lists.Pop();
                }
                _inListItem = _lists.Count > 0;
                break;
            case "li":
                _inListItem = false;
                break;
        }
    }

    private void TableStart(string name)
    {
        if (_tableDepth > 1)
        {
            return; // Nested tables are flattened into the outer cell.
        }
        switch (name)
        {
            case "tr":
                FinishRow();
                _row = [];
                break;
            case "td" or "th":
                FinishCell();
                _row ??= [];
                _cell = new StringBuilder();
                break;
        }
    }

    private void TableEnd(string name)
    {
        if (_tableDepth > 1)
        {
            return;
        }
        switch (name)
        {
            case "td" or "th":
                FinishCell();
                break;
            case "tr":
                FinishRow();
                break;
        }
    }

    private void FinishCell()
    {
        if (_cell is null)
        {
            return;
        }
        _row ??= [];
        _row.Add(CollapseSpaces(_cell.ToString()).Trim());
        _cell = null;
    }

    private void FinishRow()
    {
        FinishCell();
        if (_row is { Count: > 0 })
        {
            _tableRows ??= [];
            _tableRows.Add(_row);
        }
        _row = null;
    }

    private void FlushTable(bool force)
    {
        if (!force)
        {
            return;
        }
        FinishRow();
        if (_tableRows is { Count: > 0 })
        {
            _blocks.Add(new DocTable(_tableRows));
        }
        _tableRows = null;
        _tableDepth = 0;
    }

    private void Text(string raw, bool decode = true)
    {
        if (_preDepth > 0)
        {
            _pre.Append(decode ? WebUtility.HtmlDecode(raw) : raw);
            return;
        }
        var collapsed = CollapseSpaces(raw);
        var text = decode ? WebUtility.HtmlDecode(collapsed) : collapsed;
        if (_cell is not null)
        {
            _cell.Append(text);
            return;
        }
        if (_tableDepth > 0)
        {
            return; // Text between table tags outside cells is whitespace or junk.
        }
        if (text.Length == 0 || (text == " " && (_runs.Count == 0 || EndsWithSpace())))
        {
            return;
        }
        if (text[0] == ' ' && EndsWithSpace())
        {
            text = text[1..];
        }
        _runs.Add(new InlineRun(text, CurrentStyle(), CurrentLink()));
    }

    private bool EndsWithSpace() =>
        _runs.Count > 0 && _runs[^1].Text.Length > 0 && _runs[^1].Text[^1] is ' ' or '\n';

    private InlineStyle CurrentStyle() =>
        (_bold > 0 ? InlineStyle.Bold : InlineStyle.None)
        | (_italic > 0 ? InlineStyle.Italic : InlineStyle.None)
        | (_code > 0 ? InlineStyle.Code : InlineStyle.None);

    private string? CurrentLink() => _links.Count > 0 ? _links.Peek() : null;

    private void FlushParagraph()
    {
        if (_runs.Count == 0)
        {
            return;
        }
        var runs = TrimRuns(_runs);
        _runs.Clear();
        if (runs.Count == 0)
        {
            return;
        }
        if (_heading > 0)
        {
            _blocks.Add(new DocHeading(_heading, runs));
        }
        else if (_inListItem && _lists.Count > 0)
        {
            var top = _lists.Peek();
            _blocks.Add(new DocListItem(_lists.Count - 1, runs, top.Ordered, Math.Max(1, top.Counter)));
        }
        else
        {
            _blocks.Add(new DocParagraph(runs));
        }
    }

    private static List<InlineRun> TrimRuns(List<InlineRun> runs)
    {
        var result = runs.Where(r => r.Text.Length > 0).ToList();
        while (result.Count > 0 && string.IsNullOrWhiteSpace(result[0].Text))
        {
            result.RemoveAt(0);
        }
        while (result.Count > 0 && string.IsNullOrWhiteSpace(result[^1].Text))
        {
            result.RemoveAt(result.Count - 1);
        }
        if (result.Count > 0)
        {
            result[0] = result[0] with { Text = result[0].Text.TrimStart() };
            result[^1] = result[^1] with { Text = result[^1].Text.TrimEnd() };
        }
        return result;
    }

    /// <summary>HTML whitespace rules: runs of ASCII whitespace become one space.</summary>
    private static string CollapseSpaces(string text)
    {
        var sb = new StringBuilder(text.Length);
        var space = false;
        foreach (var c in text)
        {
            if (c is ' ' or '\t' or '\n' or '\r' or '\f')
            {
                if (!space)
                {
                    sb.Append(' ');
                    space = true;
                }
            }
            else
            {
                sb.Append(c);
                space = false;
            }
        }
        return sb.ToString();
    }

    /// <summary>Reads one attribute value from the raw attribute text of a start tag.</summary>
    internal static string? Attribute(string attributes, string name)
    {
        var i = 0;
        while (i < attributes.Length)
        {
            while (i < attributes.Length && (char.IsWhiteSpace(attributes[i]) || attributes[i] == '/'))
            {
                i++;
            }
            var start = i;
            while (i < attributes.Length && !char.IsWhiteSpace(attributes[i]) && attributes[i] is not '=' and not '/')
            {
                i++;
            }
            var attrName = attributes[start..i];
            while (i < attributes.Length && char.IsWhiteSpace(attributes[i]))
            {
                i++;
            }
            string? value = null;
            if (i < attributes.Length && attributes[i] == '=')
            {
                i++;
                while (i < attributes.Length && char.IsWhiteSpace(attributes[i]))
                {
                    i++;
                }
                if (i < attributes.Length && attributes[i] is '"' or '\'')
                {
                    var quote = attributes[i];
                    var end = attributes.IndexOf(quote, i + 1);
                    end = end < 0 ? attributes.Length : end;
                    value = attributes[(i + 1)..end];
                    i = Math.Min(end + 1, attributes.Length);
                }
                else
                {
                    var vs = i;
                    while (i < attributes.Length && !char.IsWhiteSpace(attributes[i]))
                    {
                        i++;
                    }
                    value = attributes[vs..i];
                }
            }
            if (attrName.Length == 0)
            {
                i++;
                continue;
            }
            if (string.Equals(attrName, name, StringComparison.OrdinalIgnoreCase))
            {
                return value is null ? string.Empty : WebUtility.HtmlDecode(value);
            }
        }
        return null;
    }
}
