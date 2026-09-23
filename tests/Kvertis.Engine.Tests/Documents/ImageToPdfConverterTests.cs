using System.Text;
using ImageMagick;
using Kvertis.Engine.Abstractions;
using Kvertis.Engine.Conversion.Documents;
using Kvertis.Engine.Formats;
using Shouldly;
using UglyToad.PdfPig;
using Xunit;
using static Kvertis.Engine.Tests.Documents.DocumentTestFiles;

namespace Kvertis.Engine.Tests.Documents;

public sealed class ImageToPdfConverterTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly ImageToPdfConverter _converter = new();

    public void Dispose() => _dir.Dispose();

    private string CreateJpegWithExif(string name, uint width, uint height, ushort orientation = 1)
    {
        var path = _dir.File(name);
        using var image = new MagickImage(MagickColors.SteelBlue, width, height);
        File.WriteAllBytes(path, ExifJpeg.WithExif(image.ToByteArray(MagickFormat.Jpeg), orientation, "SecretArtistName"));
        return path;
    }

    private async Task<string> Convert(string input, FormatId format, MetadataPolicy metadata = MetadataPolicy.Strip)
    {
        var result = await _converter.ConvertAsync(Info(input, format, MediaKind.Image), Path.ChangeExtension(input, ".pdf"), To(FormatRegistry.Pdf, metadata: metadata), new NullProgress(), CancellationToken.None);
        return result.OutputPath;
    }

    [Fact]
    public async Task Jpeg_becomes_one_landscape_page_without_exif()
    {
        var jpg = CreateJpegWithExif("wide.jpg", 400, 200);
        File.ReadAllBytes(jpg).AsSpan().IndexOf("SecretArtistName"u8).ShouldBeGreaterThan(0);

        var pdf = await Convert(jpg, FormatRegistry.Jpg);

        var bytes = await File.ReadAllBytesAsync(pdf);
        bytes.AsSpan().IndexOf("SecretArtistName"u8).ShouldBe(-1);
        using var doc = PdfDocument.Open(pdf);
        doc.NumberOfPages.ShouldBe(1);
        var page = doc.GetPage(1);
        page.Width.ShouldBeGreaterThan(page.Height);
        doc.Information.Creator.ShouldBe("Kvertis");
        doc.Information.Author.ShouldBeNull();
    }

    [Fact]
    public async Task Jpeg_keeps_exif_when_asked()
    {
        var jpg = CreateJpegWithExif("keep.jpg", 100, 200);

        var pdf = await Convert(jpg, FormatRegistry.Jpg, MetadataPolicy.Keep);

        (await File.ReadAllBytesAsync(pdf)).AsSpan().IndexOf("SecretArtistName"u8).ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task Rotated_jpeg_is_oriented_before_embedding()
    {
        // 400x200 pixels with orientation "rotate 90°" displays as 200x400: portrait.
        var jpg = CreateJpegWithExif("rotated.jpg", 400, 200, orientation: 6);

        var pdf = await Convert(jpg, FormatRegistry.Jpg);

        using var doc = PdfDocument.Open(pdf);
        var page = doc.GetPage(1);
        page.Height.ShouldBeGreaterThan(page.Width);
        (await File.ReadAllBytesAsync(pdf)).AsSpan().IndexOf("SecretArtistName"u8).ShouldBe(-1);
    }

    [Fact]
    public async Task Png_becomes_portrait_page()
    {
        var png = _dir.File("tall.png");
        using (var image = new MagickImage(MagickColors.Orange, 100, 300))
        {
            image.Write(png, MagickFormat.Png);
        }

        var pdf = await Convert(png, FormatRegistry.Png);

        using var doc = PdfDocument.Open(pdf);
        doc.NumberOfPages.ShouldBe(1);
        doc.GetPage(1).Height.ShouldBe(841.89, 0.5);
        doc.GetPage(1).GetImages().Count().ShouldBe(1);
    }

    [Fact]
    public async Task Sixteen_bit_png_is_converted_via_image_library()
    {
        var png = _dir.File("deep.png");
        using (var image = new MagickImage(MagickColors.Green, 50, 40))
        {
            image.Depth = 16;
            image.Write(png, MagickFormat.Png);
        }

        var pdf = await Convert(png, FormatRegistry.Png);

        using var doc = PdfDocument.Open(pdf);
        doc.NumberOfPages.ShouldBe(1);
    }

    [Fact]
    public async Task Multi_page_tiff_becomes_one_page_per_frame()
    {
        var tiff = _dir.File("scan.tiff");
        using (var frames = new MagickImageCollection())
        {
            frames.Add(new MagickImage(MagickColors.Red, 100, 150));
            frames.Add(new MagickImage(MagickColors.Green, 150, 100));
            frames.Add(new MagickImage(MagickColors.Blue, 100, 150));
            frames.Write(tiff, MagickFormat.Tiff);
        }
        var progress = new NullProgress();

        var result = await _converter.ConvertAsync(Info(tiff, FormatRegistry.Tiff, MediaKind.Image), _dir.File("scan.pdf"), To(FormatRegistry.Pdf), progress, CancellationToken.None);

        using var doc = PdfDocument.Open(result.OutputPath);
        doc.NumberOfPages.ShouldBe(3);
        doc.GetPage(2).Width.ShouldBeGreaterThan(doc.GetPage(2).Height);
        progress.Reports.Count(p => p.Phase == ConversionPhase.Converting).ShouldBe(3);
    }

    [Fact]
    public void Heic_is_not_handled_here()
    {
        var heic = new InputInfo("x.heic", FormatRegistry.Heic, MediaKind.Image, 1, null, null, null, null, []);
        _converter.Supports(heic, FormatRegistry.Pdf).ShouldBeFalse();
        _converter.Supports(heic with { Format = FormatRegistry.Jpg }, FormatRegistry.Pdf).ShouldBeTrue();
        _converter.Supports(heic with { Format = FormatRegistry.Jpg }, FormatRegistry.Png).ShouldBeFalse();
    }

    [Fact]
    public void Jpeg_stripper_removes_app1_and_keeps_image_decodable()
    {
        var jpg = CreateJpegWithExif("s.jpg", 64, 32);
        var original = File.ReadAllBytes(jpg);

        var stripped = JpegMetadataStripper.Strip(original);

        stripped.ShouldNotBeNull();
        stripped.AsSpan().IndexOf("Exif\0\0"u8).ShouldBe(-1);
        stripped.Length.ShouldBeLessThan(original.Length);
        using var image = new MagickImage(stripped);
        image.Width.ShouldBe(64u);
        image.Height.ShouldBe(32u);
        JpegMetadataStripper.Strip(Encoding.ASCII.GetBytes("not a jpeg")).ShouldBeNull();
    }
}
