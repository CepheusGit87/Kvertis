using Kvertis.Engine.Abstractions;
using Kvertis.Engine.Conversion.Documents;
using Kvertis.Engine.Formats;
using Kvertis.Engine.Probing;
using Shouldly;
using Xunit;
using Xunit.Abstractions;
using static Kvertis.Engine.Tests.Documents.DocumentTestFiles;

namespace Kvertis.Engine.Tests.Documents;

public sealed class PdfConverterTests(ITestOutputHelper output) : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    private bool FontsMissing()
    {
        if (DocumentFonts.IsAvailable)
        {
            return false;
        }
        output.WriteLine("SKIPPED: no TrueType font found in the system font folders; PDF generation cannot run here.");
        return true;
    }

    [Fact]
    public async Task Pdf_to_txt_extracts_pages_separated_by_form_feed()
    {
        if (FontsMissing())
        {
            return;
        }
        var pdf = CreatePdf(_dir.File("in.pdf"), ["Hello page one", "Second page text"]);
        var progress = new NullProgress();

        var result = await new PdfConverter().ConvertAsync(Info(pdf, FormatRegistry.Pdf), _dir.File("out.txt"), To(FormatRegistry.Txt), progress, CancellationToken.None);

        var text = await File.ReadAllTextAsync(result.OutputPath);
        text.ShouldContain("Hello page one");
        text.ShouldContain("Second page text");
        text.IndexOf('\f', StringComparison.Ordinal).ShouldBeGreaterThan(text.IndexOf("Hello", StringComparison.Ordinal));
        text.IndexOf('\f', StringComparison.Ordinal).ShouldBeLessThan(text.IndexOf("Second", StringComparison.Ordinal));
        progress.Reports.ShouldContain(p => p.Phase == ConversionPhase.Done);
        File.Exists(result.OutputPath + ".kvertis-tmp").ShouldBeFalse();
    }

    [Fact]
    public async Task Prober_reports_page_count()
    {
        if (FontsMissing())
        {
            return;
        }
        var pdf = CreatePdf(_dir.File("in.pdf"), ["a", "b", "c"]);

        var info = await new DocumentProber().ProbeAsync(Info(pdf, FormatRegistry.Pdf), CancellationToken.None);

        info.PageCount.ShouldBe(3);
    }

    [Fact]
    public async Task Password_protected_pdf_is_rejected_by_prober_and_converter()
    {
        if (FontsMissing())
        {
            return;
        }
        var pdf = CreatePdf(_dir.File("locked.pdf"), ["secret"], d => d.SecuritySettings.UserPassword = "x");

        var probe = await Should.ThrowAsync<ConversionException>(() => new DocumentProber().ProbeAsync(Info(pdf, FormatRegistry.Pdf), CancellationToken.None));
        probe.Code.ShouldBe(ConversionErrorCode.ProtectedFile);

        var convert = await Should.ThrowAsync<ConversionException>(() =>
            new PdfConverter().ConvertAsync(Info(pdf, FormatRegistry.Pdf), _dir.File("out.txt"), To(FormatRegistry.Txt), new NullProgress(), CancellationToken.None));
        convert.Code.ShouldBe(ConversionErrorCode.ProtectedFile);
        File.Exists(_dir.File("out.txt")).ShouldBeFalse();
    }

    [Fact]
    public async Task Pdf_encrypted_with_empty_user_password_is_still_rejected()
    {
        if (FontsMissing())
        {
            return;
        }
        // Only an owner password: readers can open it without a password, but it is still encrypted.
        var pdf = CreatePdf(_dir.File("owner.pdf"), ["restricted"], d => d.SecuritySettings.OwnerPassword = "owner");

        var ex = await Should.ThrowAsync<ConversionException>(() =>
            new PdfConverter().ConvertAsync(Info(pdf, FormatRegistry.Pdf), _dir.File("out.txt"), To(FormatRegistry.Txt), new NullProgress(), CancellationToken.None));

        ex.Code.ShouldBe(ConversionErrorCode.ProtectedFile);
        File.Exists(_dir.File("out.txt")).ShouldBeFalse();
    }

    [Fact]
    public async Task Garbage_pdf_is_corrupt()
    {
        var path = _dir.File("broken.pdf");
        await File.WriteAllBytesAsync(path, [.. "%PDF-1.7\n"u8.ToArray(), .. Enumerable.Range(0, 500).Select(i => (byte)(i * 7 % 251))]);

        var probe = await Should.ThrowAsync<ConversionException>(() => new DocumentProber().ProbeAsync(Info(path, FormatRegistry.Pdf), CancellationToken.None));
        probe.Code.ShouldBe(ConversionErrorCode.CorruptFile);

        var convert = await Should.ThrowAsync<ConversionException>(() =>
            new PdfConverter().ConvertAsync(Info(path, FormatRegistry.Pdf), _dir.File("out.txt"), To(FormatRegistry.Txt), new NullProgress(), CancellationToken.None));
        convert.Code.ShouldBe(ConversionErrorCode.CorruptFile);
    }

    [Fact]
    public async Task Pdf_to_png_without_rasterizer_reports_missing_system_codec()
    {
        var path = _dir.File("any.pdf");
        await File.WriteAllTextAsync(path, "%PDF-1.4");
        var converter = new PdfConverter();

        converter.Supports(Info(path, FormatRegistry.Pdf), FormatRegistry.Png).ShouldBeTrue();
        var ex = await Should.ThrowAsync<ConversionException>(() =>
            converter.ConvertAsync(Info(path, FormatRegistry.Pdf), _dir.File("out.png"), To(FormatRegistry.Png), new NullProgress(), CancellationToken.None));

        ex.Code.ShouldBe(ConversionErrorCode.MissingSystemCodec);
        ex.Detail.ShouldBe("pdf rasterizer");
    }

    [Fact]
    public async Task Multi_page_pdf_to_png_writes_numbered_page_files()
    {
        if (FontsMissing())
        {
            return;
        }
        var pdf = CreatePdf(_dir.File("doc.pdf"), ["one", "two"]);
        var rasterizer = new FakeRasterizer();

        var result = await new PdfConverter(rasterizer).ConvertAsync(Info(pdf, FormatRegistry.Pdf), _dir.File("doc.png"), To(FormatRegistry.Png), new NullProgress(), CancellationToken.None);

        result.OutputPath.ShouldBe(_dir.File("doc_p001.png"));
        File.Exists(_dir.File("doc_p001.png")).ShouldBeTrue();
        File.Exists(_dir.File("doc_p002.png")).ShouldBeTrue();
        File.Exists(_dir.File("doc.png")).ShouldBeFalse();
        rasterizer.Pages.ShouldBe([0, 1]);
        Directory.GetFiles(_dir.Path, "*.kvertis-tmp").ShouldBeEmpty();
    }

    [Fact]
    public async Task Existing_page_files_are_never_overwritten()
    {
        if (FontsMissing())
        {
            return;
        }
        var pdf = CreatePdf(_dir.File("doc.pdf"), ["one", "two"]);
        await File.WriteAllTextAsync(_dir.File("doc_p001.png"), "keep me");

        var result = await new PdfConverter(new FakeRasterizer()).ConvertAsync(Info(pdf, FormatRegistry.Pdf), _dir.File("doc.png"), To(FormatRegistry.Png), new NullProgress(), CancellationToken.None);

        (await File.ReadAllTextAsync(_dir.File("doc_p001.png"))).ShouldBe("keep me");
        result.OutputPath.ShouldBe(_dir.File("doc_p001_1.png"));
        File.Exists(_dir.File("doc_p001_1.png")).ShouldBeTrue();
        File.Exists(_dir.File("doc_p002.png")).ShouldBeTrue();
    }

    [Fact]
    public async Task Single_page_pdf_to_jpg_writes_target_path()
    {
        if (FontsMissing())
        {
            return;
        }
        var pdf = CreatePdf(_dir.File("one.pdf"), ["only"]);

        var result = await new PdfConverter(new FakeRasterizer()).ConvertAsync(Info(pdf, FormatRegistry.Pdf), _dir.File("one.jpg"), To(FormatRegistry.Jpg), new NullProgress(), CancellationToken.None);

        result.OutputPath.ShouldBe(_dir.File("one.jpg"));
        Kvertis.Engine.Tests.Images.TestImages.FormatOf(result.OutputPath).ShouldBe(SkiaSharp.SKEncodedImageFormat.Jpeg);
        Kvertis.Engine.Conversion.Images.JpegSegments.HasApp1(await File.ReadAllBytesAsync(result.OutputPath)).ShouldBeFalse();
    }

    private sealed class FakeRasterizer : IPdfRasterizer
    {
        public List<int> Pages { get; } = [];

        public bool IsAvailable => true;

        public Task RasterizePageAsync(string pdfPath, int pageIndex, int dpi, string outputPngPath, CancellationToken ct)
        {
            lock (Pages)
            {
                Pages.Add(pageIndex);
            }
            using var image = Kvertis.Engine.Tests.Images.TestImages.SolidBitmap(20, 30, SkiaSharp.SKColors.White);
            File.WriteAllBytes(outputPngPath, Kvertis.Engine.Tests.Images.TestImages.Encode(image, SkiaSharp.SKEncodedImageFormat.Png));
            return Task.CompletedTask;
        }
    }
}
