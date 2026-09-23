using System.Text;
using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Kvertis.Engine.Conversion.Documents;

/// <summary>
/// Markdown handling. Raw HTML in the source is disabled everywhere: it is shown as text, never
/// passed through into the output. Only extensions that produce plain markup are enabled (no media
/// embeds that would load remote content).
/// </summary>
internal static class MarkdownSupport
{
    public static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UsePipeTables()
        .UseEmphasisExtras()
        .UseTaskLists()
        .UseAutoLinks()
        .DisableHtml()
        .Build();

    public static string ToHtmlFragment(string markdown) => Markdown.ToHtml(markdown, Pipeline);

    public static string ToPlainText(string markdown) => Markdown.ToPlainText(markdown, Pipeline);

    /// <summary>Converts the Markdown syntax tree into the shared block model (used for PDF output).</summary>
    public static IReadOnlyList<DocBlock> ToBlocks(string markdown)
    {
        var document = Markdown.Parse(markdown, Pipeline);
        var blocks = new List<DocBlock>();
        AddBlocks(document, blocks, 0);
        return blocks;
    }

    private static void AddBlocks(ContainerBlock container, List<DocBlock> blocks, int listLevel)
    {
        foreach (var block in container)
        {
            switch (block)
            {
                case HeadingBlock h:
                    blocks.Add(new DocHeading(h.Level, ReadInlines(h.Inline)));
                    break;
                case ParagraphBlock p:
                    blocks.Add(new DocParagraph(ReadInlines(p.Inline)));
                    break;
                case CodeBlock code: // Fenced and indented.
                    blocks.Add(new DocCode(code.Lines.ToString()));
                    break;
                case ThematicBreakBlock:
                    blocks.Add(new DocRule());
                    break;
                case ListBlock list:
                    AddList(list, blocks, listLevel);
                    break;
                case Table table:
                    blocks.Add(ReadTable(table));
                    break;
                case QuoteBlock quote:
                    AddBlocks(quote, blocks, listLevel);
                    break;
                case ContainerBlock other:
                    AddBlocks(other, blocks, listLevel);
                    break;
                case LeafBlock leaf when leaf.Inline is not null:
                    blocks.Add(new DocParagraph(ReadInlines(leaf.Inline)));
                    break;
            }
        }
    }

    private static void AddList(ListBlock list, List<DocBlock> blocks, int level)
    {
        var number = int.TryParse(list.OrderedStart, out var start) ? start : 1;
        foreach (var item in list.OfType<ListItemBlock>())
        {
            var first = true;
            foreach (var child in item)
            {
                if (first && child is ParagraphBlock p)
                {
                    blocks.Add(new DocListItem(level, ReadInlines(p.Inline), list.IsOrdered, number));
                    first = false;
                    continue;
                }
                if (first)
                {
                    blocks.Add(new DocListItem(level, [], list.IsOrdered, number));
                    first = false;
                }
                if (child is ListBlock nested)
                {
                    AddList(nested, blocks, level + 1);
                }
                else if (child is ContainerBlock c)
                {
                    AddBlocks(c, blocks, level + 1);
                }
                else if (child is ParagraphBlock more)
                {
                    blocks.Add(new DocListItem(level + 1, ReadInlines(more.Inline)));
                }
                else if (child is CodeBlock code)
                {
                    blocks.Add(new DocCode(code.Lines.ToString()));
                }
            }
            number++;
        }
    }

    private static DocTable ReadTable(Table table)
    {
        var rows = new List<IReadOnlyList<string>>();
        foreach (var row in table.OfType<TableRow>())
        {
            var cells = new List<string>();
            foreach (var cell in row.OfType<TableCell>())
            {
                var text = new StringBuilder();
                foreach (var para in cell.OfType<ParagraphBlock>())
                {
                    if (text.Length > 0)
                    {
                        text.Append('\n');
                    }
                    text.Append(Inlines.PlainText(ReadInlines(para.Inline)));
                }
                cells.Add(text.ToString());
            }
            rows.Add(cells);
        }
        return new DocTable(rows);
    }

    private static List<InlineRun> ReadInlines(ContainerInline? container)
    {
        var runs = new List<InlineRun>();
        if (container is not null)
        {
            AddInlines(container, runs, InlineStyle.None, null);
        }
        return runs;
    }

    private static void AddInlines(ContainerInline container, List<InlineRun> runs, InlineStyle style, string? url)
    {
        foreach (var inline in container)
        {
            switch (inline)
            {
                case LiteralInline literal:
                    runs.Add(new InlineRun(literal.Content.ToString(), style, url));
                    break;
                case CodeInline code:
                    runs.Add(new InlineRun(code.Content, style | InlineStyle.Code, url));
                    break;
                case LineBreakInline br:
                    runs.Add(new InlineRun(br.IsHard ? "\n" : " ", style, url));
                    break;
                case HtmlEntityInline entity:
                    runs.Add(new InlineRun(entity.Transcoded.ToString(), style, url));
                    break;
                case HtmlInline html:
                    runs.Add(new InlineRun(html.Tag, style, url)); // Raw HTML is disabled; kept as text.
                    break;
                case Markdig.Extensions.TaskLists.TaskList task:
                    runs.Add(new InlineRun(task.Checked ? "[x] " : "[ ] ", style, url));
                    break;
                case AutolinkInline auto:
                    runs.Add(new InlineRun(auto.Url, style, auto.Url));
                    break;
                case LinkInline { IsImage: true } image:
                    var alt = new List<InlineRun>();
                    AddInlines(image, alt, style, null);
                    var altText = Inlines.PlainText(alt);
                    runs.Add(new InlineRun(altText.Length > 0 ? $"[image: {altText}]" : DocxReader.ImagePlaceholder, style, url));
                    break;
                case LinkInline link:
                    AddInlines(link, runs, style, link.Url ?? url);
                    break;
                case EmphasisInline emphasis:
                    var added = emphasis.DelimiterChar is '*' or '_'
                        ? emphasis.DelimiterCount >= 2 ? InlineStyle.Bold : InlineStyle.Italic
                        : InlineStyle.None;
                    AddInlines(emphasis, runs, style | added, url);
                    break;
                case ContainerInline other:
                    AddInlines(other, runs, style, url);
                    break;
            }
        }
    }
}
