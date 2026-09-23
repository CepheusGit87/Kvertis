using System.Text;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Kvertis.Engine.Conversion.Documents;

/// <summary>Streams elements of one type set from an OpenXml part without loading the whole part.</summary>
internal static class OpenXmlStreaming
{
    public static IEnumerable<OpenXmlElement> Elements(OpenXmlPart part, CancellationToken ct, params Type[] types)
    {
        using var reader = OpenXmlReader.Create(part);
        if (!reader.Read())
        {
            yield break;
        }
        while (!reader.EOF)
        {
            ct.ThrowIfCancellationRequested();
            if (reader.IsStartElement && Array.IndexOf(types, reader.ElementType) >= 0)
            {
                // LoadCurrentElement leaves the reader on the node after the loaded element.
                var element = reader.LoadCurrentElement();
                if (element is not null)
                {
                    yield return element;
                }
                continue;
            }
            if (!reader.Read())
            {
                break;
            }
        }
    }
}

/// <summary>
/// Reads the body of a DOCX as blocks: paragraphs, headings (by style), list items (by numbering),
/// tables. Images become the placeholder "[image]". Headers, footers, comments and tracked
/// deletions are not part of the output.
/// </summary>
internal sealed partial class DocxReader
{
    public const string ImagePlaceholder = "[image]";

    private readonly MainDocumentPart _main;
    private readonly Dictionary<string, StyleInfo> _styles;
    private readonly Dictionary<string, string> _links;

    private sealed record StyleInfo(int? HeadingLevel, bool IsList);

    public DocxReader(WordprocessingDocument document)
    {
        _main = document.MainDocumentPart ?? throw new InvalidDataException("document has no main part");
        _styles = ReadStyles(_main.StyleDefinitionsPart);
        _links = _main.HyperlinkRelationships.ToDictionary(r => r.Id, r => r.Uri.OriginalString, StringComparer.Ordinal);
    }

    public IEnumerable<DocBlock> ReadBlocks(CancellationToken ct)
    {
        foreach (var element in OpenXmlStreaming.Elements(_main, ct, typeof(Paragraph), typeof(Table)))
        {
            if (element is Paragraph p)
            {
                yield return ConvertParagraph(p);
            }
            else if (element is Table t)
            {
                yield return ConvertTable(t);
            }
        }
    }

    private DocBlock ConvertParagraph(Paragraph p)
    {
        var runs = new List<InlineRun>();
        CollectInlines(p, runs, null);
        var props = p.ParagraphProperties;
        var styleId = props?.ParagraphStyleId?.Val?.Value;
        var style = styleId is not null && _styles.TryGetValue(styleId, out var s) ? s : null;

        var headingLevel = style?.HeadingLevel ?? HeadingFromStyleId(styleId);
        if (props?.OutlineLevel?.Val?.Value is int outline && outline < 6)
        {
            headingLevel = outline + 1;
        }
        if (headingLevel is int level && !Inlines.IsBlank(runs))
        {
            return new DocHeading(level, runs);
        }

        var numbering = props?.NumberingProperties;
        var numId = numbering?.NumberingId?.Val?.Value;
        if (numId is > 0 || (numId is null && style?.IsList == true))
        {
            var ilvl = numbering?.NumberingLevelReference?.Val?.Value ?? 0;
            return new DocListItem(Math.Clamp(ilvl, 0, 8), runs);
        }
        return new DocParagraph(runs);
    }

    private void CollectInlines(OpenXmlElement container, List<InlineRun> runs, string? url)
    {
        foreach (var child in container.ChildElements)
        {
            switch (child)
            {
                case Run run:
                    AddRun(run, runs, url);
                    break;
                case Hyperlink link:
                    var target = link.Id?.Value is { } id && _links.TryGetValue(id, out var u) ? u
                        : link.Anchor?.Value is { Length: > 0 } anchor ? "#" + anchor : null;
                    CollectInlines(link, runs, target ?? url);
                    break;
                case SimpleField or SdtRun or SdtContentRun or InsertedRun or CustomXmlRun or MoveToRun:
                    CollectInlines(child, runs, url);
                    break;
                // DeletedRun, MoveFromRun, properties, bookmarks, comments: not part of the visible text.
            }
        }
    }

    private static void AddRun(Run run, List<InlineRun> runs, string? url)
    {
        var rp = run.RunProperties;
        var style = InlineStyle.None;
        if (IsOn(rp?.Bold))
        {
            style |= InlineStyle.Bold;
        }
        if (IsOn(rp?.Italic))
        {
            style |= InlineStyle.Italic;
        }

        var sb = new StringBuilder();
        foreach (var child in run.ChildElements)
        {
            switch (child)
            {
                case Text t:
                    sb.Append(t.Text);
                    break;
                case TabChar:
                    sb.Append('\t');
                    break;
                case Break or CarriageReturn:
                    sb.Append('\n');
                    break;
                case NoBreakHyphen:
                    sb.Append('-');
                    break;
                case Drawing or Picture or EmbeddedObject:
                    Flush(sb, runs, style, url);
                    runs.Add(new InlineRun(ImagePlaceholder, InlineStyle.Literal));
                    break;
                case AlternateContent ac when ac.Descendants<Drawing>().Any() || ac.Descendants<Picture>().Any():
                    Flush(sb, runs, style, url);
                    runs.Add(new InlineRun(ImagePlaceholder, InlineStyle.Literal));
                    break;
            }
        }
        Flush(sb, runs, style, url);
    }

    private static void Flush(StringBuilder sb, List<InlineRun> runs, InlineStyle style, string? url)
    {
        if (sb.Length > 0)
        {
            runs.Add(new InlineRun(sb.ToString(), style, url));
            sb.Clear();
        }
    }

    private static bool IsOn(OnOffType? value) => value is not null && (value.Val is null || value.Val.Value);

    private static DocTable ConvertTable(Table table)
    {
        var rows = new List<IReadOnlyList<string>>();
        foreach (var row in table.Elements<TableRow>())
        {
            var cells = new List<string>();
            foreach (var cell in row.Elements<TableCell>())
            {
                var paragraphs = cell.Descendants<Paragraph>().Select(ParagraphPlainText).Where(t => t.Length > 0);
                cells.Add(string.Join('\n', paragraphs));
            }
            rows.Add(cells);
        }
        return new DocTable(rows);
    }

    private static string ParagraphPlainText(Paragraph p)
    {
        var sb = new StringBuilder();
        foreach (var node in p.Descendants())
        {
            switch (node)
            {
                case Text t when t.Ancestors<Paragraph>().First() == p:
                    sb.Append(t.Text);
                    break;
                case TabChar:
                    sb.Append(' ');
                    break;
                case Drawing or Picture:
                    sb.Append(ImagePlaceholder);
                    break;
            }
        }
        return sb.ToString().Trim();
    }

    private static Dictionary<string, StyleInfo> ReadStyles(StyleDefinitionsPart? part)
    {
        var map = new Dictionary<string, StyleInfo>(StringComparer.Ordinal);
        if (part?.Styles is null)
        {
            return map;
        }
        foreach (var style in part.Styles.Elements<Style>())
        {
            var id = style.StyleId?.Value;
            if (id is null)
            {
                continue;
            }
            int? level = null;
            // Built-in style names are stored in English regardless of the UI language ("heading 1").
            var name = style.StyleName?.Val?.Value ?? string.Empty;
            var match = HeadingName().Match(name);
            if (match.Success)
            {
                level = Math.Min(6, int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture));
            }
            else if (string.Equals(name, "title", StringComparison.OrdinalIgnoreCase))
            {
                level = 1;
            }
            else if (style.StyleParagraphProperties?.OutlineLevel?.Val?.Value is int outline && outline < 6)
            {
                level = outline + 1;
            }
            var isList = style.StyleParagraphProperties?.NumberingProperties?.NumberingId?.Val?.Value is > 0;
            map[id] = new StyleInfo(level ?? HeadingFromStyleId(id), isList);
        }
        return map;
    }

    private static int? HeadingFromStyleId(string? styleId)
    {
        if (styleId is null)
        {
            return null;
        }
        var match = HeadingId().Match(styleId);
        return match.Success ? int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) : null;
    }

    [GeneratedRegex(@"^heading\s*([1-9])$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HeadingName();

    [GeneratedRegex(@"^Heading([1-6])$", RegexOptions.CultureInvariant)]
    private static partial Regex HeadingId();
}
