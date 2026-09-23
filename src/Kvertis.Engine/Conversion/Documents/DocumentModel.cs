namespace Kvertis.Engine.Conversion.Documents;

/// <summary>Inline formatting flags shared by all document readers and writers.</summary>
[Flags]
internal enum InlineStyle
{
    None = 0,
    Bold = 1,
    Italic = 2,
    Code = 4,
    /// <summary>Written as is by the Markdown writer (placeholders such as "[image]").</summary>
    Literal = 8,
}

/// <summary>A run of text with one formatting. <see cref="Url"/> is set for hyperlinks.</summary>
internal sealed record InlineRun(string Text, InlineStyle Style = InlineStyle.None, string? Url = null)
{
    public static InlineRun Plain(string text) => new(text);
}

/// <summary>
/// Minimal block model used between readers (DOCX, HTML, Markdown) and writers (TXT, Markdown,
/// HTML, PDF). Readers yield blocks lazily so large documents are never held completely in memory.
/// </summary>
internal abstract record DocBlock;

internal sealed record DocHeading(int Level, IReadOnlyList<InlineRun> Inlines) : DocBlock;

internal sealed record DocParagraph(IReadOnlyList<InlineRun> Inlines) : DocBlock;

/// <summary>List item. Level is 0-based nesting depth.</summary>
internal sealed record DocListItem(int Level, IReadOnlyList<InlineRun> Inlines, bool Ordered = false, int Number = 1) : DocBlock;

/// <summary>Table as plain cell texts. The first row is treated as header by the Markdown/HTML writers.</summary>
internal sealed record DocTable(IReadOnlyList<IReadOnlyList<string>> Rows) : DocBlock;

internal sealed record DocCode(string Text) : DocBlock;

internal sealed record DocRule : DocBlock;

internal static class Inlines
{
    public static string PlainText(IEnumerable<InlineRun> runs) => string.Concat(runs.Select(r => r.Text));

    public static bool IsBlank(IReadOnlyList<InlineRun> runs) => runs.All(r => string.IsNullOrWhiteSpace(r.Text));
}
