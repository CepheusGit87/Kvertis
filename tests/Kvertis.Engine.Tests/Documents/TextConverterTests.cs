using System.Text;
using Kvertis.Engine.Abstractions;
using Kvertis.Engine.Conversion.Documents;
using Kvertis.Engine.Formats;
using Shouldly;
using UglyToad.PdfPig;
using Xunit;
using Xunit.Abstractions;
using static Kvertis.Engine.Tests.Documents.DocumentTestFiles;

namespace Kvertis.Engine.Tests.Documents;

public sealed class TextConverterTests(ITestOutputHelper output) : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly TextConverter _converter = new();

    public void Dispose() => _dir.Dispose();

    private async Task<string> Convert(string input, FormatId inFormat, FormatId outFormat, string outName, Dictionary<string, string>? advanced = null)
    {
        var result = await _converter.ConvertAsync(Info(input, inFormat), _dir.File(outName), To(outFormat, advanced), new NullProgress(), CancellationToken.None);
        return result.OutputPath;
    }

    private bool FontsMissing()
    {
        if (DocumentFonts.IsAvailable)
        {
            return false;
        }
        output.WriteLine("SKIPPED: no TrueType font found in the system font folders; PDF rendering cannot run here.");
        return true;
    }

    [Fact]
    public async Task Txt_to_pdf_renders_all_lines_on_a4_by_default()
    {
        if (FontsMissing())
        {
            return;
        }
        var lines = Enumerable.Range(1, 120).Select(i => $"Line number {i} with some text");
        var txt = WriteText(_dir.File("in.txt"), string.Join("\n", lines) + "\n" + new string('x', 300));

        var pdf = await Convert(txt, FormatRegistry.Txt, FormatRegistry.Pdf, "out.pdf");

        using var doc = PdfDocument.Open(pdf);
        doc.NumberOfPages.ShouldBeGreaterThan(1);
        doc.GetPage(1).Width.ShouldBe(595.28, 0.5);
        doc.Information.Creator.ShouldBe("Kvertis");
        var all = string.Concat(doc.GetPages().Select(p => p.Text));
        all.ShouldContain("Line number 1 with some text");
        all.ShouldContain("Line number 120 with some text");
    }

    [Fact]
    public async Task Txt_to_pdf_honors_letter_page_size()
    {
        if (FontsMissing())
        {
            return;
        }
        var txt = WriteText(_dir.File("in.txt"), "hello");

        var pdf = await Convert(txt, FormatRegistry.Txt, FormatRegistry.Pdf, "out.pdf",
            new() { [ConversionSettings.AdvancedKeys.PageSize] = "Letter" });

        using var doc = PdfDocument.Open(pdf);
        doc.GetPage(1).Width.ShouldBe(612, 0.5);
        doc.GetPage(1).Height.ShouldBe(792, 0.5);
    }

    [Fact]
    public async Task Markdown_to_pdf_renders_headings_lists_code_and_tables()
    {
        if (FontsMissing())
        {
            return;
        }
        var md = WriteText(_dir.File("in.md"),
            "# Big Title\n\nSome **bold** and *italic* text with a [link](https://example.org).\n\n" +
            "- apple\n- banana\n  1. nested\n\n```\ncode line\n```\n\n| A | B |\n|---|---|\n| c1 | c2 |\n\n![pic](http://example.invalid/x.png)\n");

        var pdf = await Convert(md, FormatRegistry.Markdown, FormatRegistry.Pdf, "out.pdf");

        using var doc = PdfDocument.Open(pdf);
        var text = string.Concat(doc.GetPages().Select(p => p.Text));
        text.ShouldContain("Big Title");
        text.ShouldContain("apple");
        text.ShouldContain("nested");
        text.ShouldContain("code line");
        text.ShouldContain("c2");
        text.ShouldContain("[image: pic]");
    }

    [Fact]
    public async Task Markdown_to_html_disables_raw_html()
    {
        var md = WriteText(_dir.File("in.md"), "# Head\n\n<script>alert(1)</script>\n\nText with <b onclick=\"x()\">inline</b> html.\n");

        var html = await File.ReadAllTextAsync(await Convert(md, FormatRegistry.Markdown, FormatRegistry.Html, "out.html"));

        html.ShouldContain("<h1");
        html.ShouldContain("Head</h1>");
        html.ShouldNotContain("<script>");
        html.ShouldContain("&lt;script&gt;");
        html.ShouldNotContain("<b onclick");
        html.ShouldContain("<meta charset=\"utf-8\">");
    }

    [Fact]
    public async Task Txt_to_html_wraps_escaped_text_in_pre()
    {
        var txt = WriteText(_dir.File("in.txt"), "a < b & <i>c</i>\nsecond");

        var html = await File.ReadAllTextAsync(await Convert(txt, FormatRegistry.Txt, FormatRegistry.Html, "out.html"));

        html.ShouldContain("<pre>a &lt; b &amp; &lt;i&gt;c&lt;/i&gt;\nsecond\n</pre>");
    }

    [Fact]
    public async Task Markdown_to_txt_strips_markup()
    {
        var md = WriteText(_dir.File("in.md"), "# Title\n\nSome **bold** and `code` and [a link](https://example.org).\n");

        var txt = await File.ReadAllTextAsync(await Convert(md, FormatRegistry.Markdown, FormatRegistry.Txt, "out.txt"));

        txt.ShouldContain("Title");
        txt.ShouldContain("Some bold and code and a link.");
        txt.ShouldNotContain("**");
        txt.ShouldNotContain("#");
    }

    [Fact]
    public async Task Txt_to_markdown_escapes_markup_characters()
    {
        var txt = WriteText(_dir.File("in.txt"), "# not heading\n2*3 = 6\n\n- dash");

        var md = await File.ReadAllTextAsync(await Convert(txt, FormatRegistry.Txt, FormatRegistry.Markdown, "out.md"));

        md.ShouldBe("\\# not heading  \n2\\*3 = 6\n\n\\- dash\n");
    }

    [Fact]
    public async Task Html_to_txt_strips_scripts_styles_and_keeps_links()
    {
        var html = WriteText(_dir.File("page.html"), """
            <!DOCTYPE html>
            <html><head><title>Ignored title</title>
            <style>body { color: red }</style>
            <script>document.write("evil")</script>
            <link rel="stylesheet" href="https://example.invalid/x.css">
            </head>
            <body>
            <h1>Main &amp; Heading</h1>
            <p>First   paragraph<br>next line with <a href="https://example.org/a">a link</a>.</p>
            <!-- comment -->
            <ul><li>one</li><li>two <b>bold</b></li></ul>
            <img src="http://example.invalid/tracker.png" alt="Logo">
            <script type="text/javascript">
              var s = "</p><p>fake";
            </script>
            <table><tr><th>K</th><th>V</th></tr><tr><td>a</td><td>1</td></tr></table>
            </body></html>
            """);

        var txt = await File.ReadAllTextAsync(await Convert(html, FormatRegistry.Html, FormatRegistry.Txt, "page.txt"));

        txt.ShouldNotContain("evil");
        txt.ShouldNotContain("color");
        txt.ShouldNotContain("fake");
        txt.ShouldNotContain("Ignored title");
        txt.ShouldNotContain("comment");
        txt.ShouldContain("Main & Heading");
        txt.ShouldContain("First paragraph\r\nnext line with a link (https://example.org/a).");
        txt.ShouldContain("- one");
        txt.ShouldContain("- two bold");
        txt.ShouldContain("[image: Logo]");
        txt.ShouldContain("K\tV");
        txt.ShouldContain("a\t1");
    }

    [Fact]
    public async Task Html_to_markdown_converts_structure()
    {
        var html = WriteText(_dir.File("page.html"),
            "<h2>Sub</h2><p>Text <em>em</em> <strong>strong</strong> <a href=\"https://example.org\">site</a> <a href=\"javascript:alert(1)\">bad</a></p><ol><li>first</li></ol>");

        var md = await File.ReadAllTextAsync(await Convert(html, FormatRegistry.Html, FormatRegistry.Markdown, "page.md"));

        md.ShouldContain("## Sub");
        md.ShouldContain("Text *em* **strong** [site](https://example.org) bad");
        md.ShouldNotContain("javascript");
        md.ShouldContain("1. first");
    }

    [Fact]
    public async Task Csv_to_txt_handles_quotes_newlines_and_semicolons()
    {
        var csv = WriteText(_dir.File("in.csv"), "name,note\r\n\"Doe, John\",\"said \"\"hi\"\"\"\r\nx,\"multi\nline\"\r\n");
        var semi = WriteText(_dir.File("semi.csv"), "a;b;c\n1;2,5;3\n");

        var txt = await File.ReadAllTextAsync(await Convert(csv, FormatRegistry.Csv, FormatRegistry.Txt, "out.txt"));
        var txt2 = await File.ReadAllTextAsync(await Convert(semi, FormatRegistry.Csv, FormatRegistry.Txt, "semi.txt"));

        txt.ShouldBe("name\tnote\r\nDoe, John\tsaid \"hi\"\r\nx\tmulti line\r\n");
        txt2.ShouldBe("a\tb\tc\r\n1\t2,5\t3\r\n");
    }

    [Fact]
    public async Task Latin1_input_is_decoded_when_not_valid_utf8()
    {
        var path = _dir.File("latin.txt");
        await File.WriteAllBytesAsync(path, Encoding.Latin1.GetBytes("Grüße aus Köln"));

        TextEncodingDetector.Detect(path).ShouldBe(Encoding.Latin1);
        var html = await File.ReadAllTextAsync(await Convert(path, FormatRegistry.Txt, FormatRegistry.Html, "latin.html"));

        html.ShouldContain("Grüße aus Köln");
        html.ShouldNotContain("�");
    }

    [Fact]
    public void Encoding_detection_uses_bom_then_utf8_validation()
    {
        TextEncodingDetector.Detect([0xFF, 0xFE, 0x41, 0x00]).ShouldBe(Encoding.Unicode);
        TextEncodingDetector.Detect([0xFE, 0xFF, 0x00, 0x41]).ShouldBe(Encoding.BigEndianUnicode);
        TextEncodingDetector.Detect([0xEF, 0xBB, 0xBF, 0x41]).WebName.ShouldBe("utf-8");
        TextEncodingDetector.Detect(Encoding.UTF8.GetBytes("Grüße")).WebName.ShouldBe("utf-8");
        TextEncodingDetector.Detect([0x47, 0x72, 0xFC, 0xDF, 0x65]).ShouldBe(Encoding.Latin1);
    }

    [Fact]
    public async Task Utf16_input_with_bom_is_read()
    {
        var path = _dir.File("u16.md");
        await File.WriteAllTextAsync(path, "# Überschrift\n", Encoding.Unicode);

        var html = await File.ReadAllTextAsync(await Convert(path, FormatRegistry.Markdown, FormatRegistry.Html, "u16.html"));

        html.ShouldContain("Überschrift</h1>");
    }

    [Fact]
    public async Task Existing_output_is_not_overwritten()
    {
        var txt = WriteText(_dir.File("in.txt"), "x");
        await File.WriteAllTextAsync(_dir.File("out.html"), "keep");

        var ex = await Should.ThrowAsync<ConversionException>(() => Convert(txt, FormatRegistry.Txt, FormatRegistry.Html, "out.html"));

        ex.Code.ShouldBe(ConversionErrorCode.OutputExists);
        (await File.ReadAllTextAsync(_dir.File("out.html"))).ShouldBe("keep");
    }
}
