using System.Text;
using Kvertis.Engine.Abstractions;
using Kvertis.Engine.Conversion.Documents;
using Kvertis.Engine.Formats;
using Kvertis.Engine.Probing;
using Shouldly;
using Xunit;
using static Kvertis.Engine.Tests.Documents.DocumentTestFiles;

namespace Kvertis.Engine.Tests.Documents;

public sealed class OfficeConverterTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly OfficeConverter _converter = new();

    public void Dispose() => _dir.Dispose();

    private async Task<string> Convert(string input, FormatId inFormat, FormatId outFormat, string outName)
    {
        var result = await _converter.ConvertAsync(Info(input, inFormat), _dir.File(outName), To(outFormat), new NullProgress(), CancellationToken.None);
        return result.OutputPath;
    }

    [Fact]
    public async Task Docx_to_markdown_maps_headings_emphasis_lists_tables_images_links()
    {
        var docx = CreateDocx(_dir.File("in.docx"));

        var md = await File.ReadAllTextAsync(await Convert(docx, FormatRegistry.Docx, FormatRegistry.Markdown, "out.md"));

        md.ShouldContain("# Report Title\n");
        md.ShouldContain("## Section\n");
        md.ShouldContain("Plain start **bold part** and *italic part*.");
        md.ShouldContain("Second paragraph with \\*stars\\*");
        md.ShouldContain("- First item\n  - Nested item\n- Second item");
        md.ShouldContain("| Name | Value |");
        md.ShouldContain("| --- | --- |");
        md.ShouldContain("| alpha | 1\\|2 |");
        md.ShouldContain("[image]");
        md.ShouldContain("[our site](https://example.org/page)");
    }

    [Fact]
    public async Task Docx_to_txt_keeps_every_paragraph_in_order()
    {
        var docx = CreateDocx(_dir.File("in.docx"));

        var path = await Convert(docx, FormatRegistry.Docx, FormatRegistry.Txt, "out.txt");
        var bytes = await File.ReadAllBytesAsync(path);
        var txt = Encoding.UTF8.GetString(bytes);

        bytes.Take(3).ShouldBe(new byte[] { 0xEF, 0xBB, 0xBF });
        var title = txt.IndexOf("Report Title", StringComparison.Ordinal);
        var first = txt.IndexOf("Plain start bold part and italic part.", StringComparison.Ordinal);
        var second = txt.IndexOf("Second paragraph", StringComparison.Ordinal);
        title.ShouldBeGreaterThanOrEqualTo(0);
        first.ShouldBeGreaterThan(title);
        second.ShouldBeGreaterThan(first);
        txt.ShouldContain("Name\tValue");
        txt.ShouldContain("Visit our site (https://example.org/page)");
        txt.ShouldNotContain("**");
    }

    [Fact]
    public async Task Docx_to_html_escapes_text_and_uses_semantic_tags()
    {
        var docx = CreateDocx(_dir.File("in.docx"));

        var html = await File.ReadAllTextAsync(await Convert(docx, FormatRegistry.Docx, FormatRegistry.Html, "out.html"));

        html.ShouldContain("<meta charset=\"utf-8\">");
        html.ShouldContain("<h1>Report Title</h1>");
        html.ShouldContain("<strong>bold part</strong>");
        html.ShouldContain("<em>italic part</em>");
        html.ShouldContain("<li>First item</li>");
        html.ShouldContain("<table>");
        html.ShouldContain("<th>Name</th>");
        html.ShouldContain("<a href=\"https://example.org/page\">our site</a>");
        html.ShouldContain("&lt;b&gt;not html&lt;/b&gt; &amp; more");
        html.ShouldNotContain("<b>not html");
    }

    [Fact]
    public async Task Xlsx_to_csv_resolves_strings_formats_values_and_quotes_rfc4180()
    {
        var xlsx = CreateXlsx(_dir.File("data.xlsx"), new SheetSpec("Only", [
            Row(1, Shared("A1", 0), Shared("B1", 1), Shared("C1", 2)),
            Row(2, Inline("A2", "a,b"), Number("B2", "3.5"), Inline("C2", "say \"hi\"")),
            Row(3, Inline("A3", "line1\nline2"), Number("B3", "45306", style: 1), Bool("C3", true)),
            Row(5, Number("A5", "45306.5", style: 2), Number("C5", "0.25", style: 3)),
        ]));

        var path = await Convert(xlsx, FormatRegistry.Xlsx, FormatRegistry.Csv, "data.csv");
        var bytes = await File.ReadAllBytesAsync(path);
        var csv = Encoding.UTF8.GetString(bytes.AsSpan(3));

        path.ShouldBe(_dir.File("data.csv"));
        bytes.Take(3).ShouldBe(new byte[] { 0xEF, 0xBB, 0xBF });
        csv.ShouldBe(
            "Name,Amount,rich text\r\n" +
            "\"a,b\",3.5,\"say \"\"hi\"\"\"\r\n" +
            "\"line1\nline2\",2024-01-15,TRUE\r\n" +
            ",,\r\n" +
            "2024-01-15 12:00:00,,25%\r\n");
    }

    [Fact]
    public async Task Xlsx_with_several_sheets_writes_one_csv_per_sheet()
    {
        var xlsx = CreateXlsx(_dir.File("book.xlsx"),
            new SheetSpec("Sheet One", [Row(1, Inline("A1", "first"))]),
            new SheetSpec("Q1/Q2", [Row(1, Inline("A1", "second"))]));

        var result = await _converter.ConvertAsync(Info(xlsx, FormatRegistry.Xlsx), _dir.File("book.csv"), To(FormatRegistry.Csv), new NullProgress(), CancellationToken.None);

        result.OutputPath.ShouldBe(_dir.File("book_Sheet One.csv"));
        File.ReadAllText(_dir.File("book_Sheet One.csv")).ShouldContain("first");
        File.ReadAllText(_dir.File("book_Q1_Q2.csv")).ShouldContain("second");
        File.Exists(_dir.File("book.csv")).ShouldBeFalse();
    }

    [Fact]
    public async Task Xlsx_to_txt_is_tab_separated_with_sheet_headers()
    {
        var xlsx = CreateXlsx(_dir.File("book.xlsx"),
            new SheetSpec("A", [Row(1, Inline("A1", "x"), Inline("B1", "y\tz"))]),
            new SheetSpec("B", [Row(1, Number("A1", "1"), Number("B1", "2"))]));

        var txt = await File.ReadAllTextAsync(await Convert(xlsx, FormatRegistry.Xlsx, FormatRegistry.Txt, "book.txt"));

        txt.ShouldBe("--- A ---\r\nx\ty z\r\n\r\n--- B ---\r\n1\t2\r\n");
    }

    [Fact]
    public async Task Pptx_to_txt_and_markdown_in_slide_order()
    {
        var pptx = CreatePptx(_dir.File("deck.pptx"), ["Welcome", "Intro text"], ["Second slide", "# not a heading"]);

        var txt = await File.ReadAllTextAsync(await Convert(pptx, FormatRegistry.Pptx, FormatRegistry.Txt, "deck.txt"));
        var md = await File.ReadAllTextAsync(await Convert(pptx, FormatRegistry.Pptx, FormatRegistry.Markdown, "deck.md"));

        txt.ShouldBe("--- Slide 1 ---\r\nWelcome\r\nIntro text\r\n\r\n--- Slide 2 ---\r\nSecond slide\r\n# not a heading\r\n");
        md.ShouldBe("## Slide 1\n\nWelcome\n\nIntro text\n\n## Slide 2\n\nSecond slide\n\n\\# not a heading\n");
    }

    [Fact]
    public async Task Pptx_prober_reports_slide_count_and_xlsx_sheet_count()
    {
        var pptx = CreatePptx(_dir.File("deck.pptx"), ["a"], ["b"], ["c"]);
        var xlsx = CreateXlsx(_dir.File("b.xlsx"), new SheetSpec("S1", []), new SheetSpec("S2", []));
        var docx = CreateDocx(_dir.File("d.docx"));
        var prober = new DocumentProber();

        (await prober.ProbeAsync(Info(pptx, FormatRegistry.Pptx), CancellationToken.None)).PageCount.ShouldBe(3);
        (await prober.ProbeAsync(Info(xlsx, FormatRegistry.Xlsx), CancellationToken.None)).PageCount.ShouldBe(2);
        (await prober.ProbeAsync(Info(docx, FormatRegistry.Docx), CancellationToken.None)).PageCount.ShouldBeNull();
    }

    [Fact]
    public async Task Encrypted_office_file_is_protected()
    {
        var path = CreateFakeEncryptedOffice(_dir.File("secret.docx"));

        var probe = await Should.ThrowAsync<ConversionException>(() => new DocumentProber().ProbeAsync(Info(path, FormatRegistry.Docx), CancellationToken.None));
        probe.Code.ShouldBe(ConversionErrorCode.ProtectedFile);

        var convert = await Should.ThrowAsync<ConversionException>(() =>
            _converter.ConvertAsync(Info(path, FormatRegistry.Docx), _dir.File("out.txt"), To(FormatRegistry.Txt), new NullProgress(), CancellationToken.None));
        convert.Code.ShouldBe(ConversionErrorCode.ProtectedFile);
        OfficeProtection.IsEncryptedOfficeFile(path).ShouldBeTrue();
    }

    [Fact]
    public async Task Broken_docx_package_is_corrupt()
    {
        var path = _dir.File("broken.docx");
        await File.WriteAllBytesAsync(path, [.. "PK\u0003\u0004"u8.ToArray(), .. new byte[200]]);

        var probe = await Should.ThrowAsync<ConversionException>(() => new DocumentProber().ProbeAsync(Info(path, FormatRegistry.Docx), CancellationToken.None));
        probe.Code.ShouldBe(ConversionErrorCode.CorruptFile);
    }

    [Fact]
    public async Task Cancelled_job_reports_cancelled_and_leaves_no_output()
    {
        var docx = CreateDocx(_dir.File("in.docx"));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var ex = await Should.ThrowAsync<ConversionException>(() =>
            _converter.ConvertAsync(Info(docx, FormatRegistry.Docx), _dir.File("out.txt"), To(FormatRegistry.Txt), new NullProgress(), cts.Token));

        ex.Code.ShouldBe(ConversionErrorCode.Cancelled);
        Directory.GetFiles(_dir.Path).ShouldBe([docx]);
    }

    [Fact]
    public void Supports_only_phase_one_matrix()
    {
        var docx = new InputInfo("x.docx", FormatRegistry.Docx, MediaKind.Document, 1, null, null, null, null, []);
        _converter.Supports(docx, FormatRegistry.Txt).ShouldBeTrue();
        _converter.Supports(docx, FormatRegistry.Pdf).ShouldBeFalse();
        _converter.Supports(docx with { Format = FormatRegistry.LegacyOffice }, FormatRegistry.Txt).ShouldBeFalse();
    }
}
