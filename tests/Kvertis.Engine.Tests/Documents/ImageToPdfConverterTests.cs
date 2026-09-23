using System.Text;
using Kvertis.Engine.Abstractions;
using Kvertis.Engine.Conversion.Documents;
using Kvertis.Engine.Formats;
using Kvertis.Engine.Tests.Images;
using NSubstitute;
using Shouldly;
using SkiaSharp;
using UglyToad.PdfPig;
using Xunit;
using static Kvertis.Engine.Tests.Documents.DocumentTestFiles;

namespace Kvertis.Engine.Tests.Documents;

public sealed class ImageToPdfConverterTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly ImageToPdfConverter _converter = new();

    public void Dispose() => _dir.Dispose();

    private string CreateJpegWithExif(string name, int width, int height, ushort orientation = 1)
    {
        var path = _dir.File(name);
        using var image = TestImages.SolidBitmap(width, height, SKColors.SteelBlue);
        File.WriteAllBytes(path, ExifJpeg.WithExif(TestImages.Encode(image, SKEncodedImageFormat.Jpeg), orientation, "SecretArtistName"));
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
    public async Task Rotated_jpeg_keeps_exif_with_upright_orientation_when_asked()
    {
        var jpg = CreateJpegWithExif("rotated-keep.jpg", 400, 200, orientation: 6);

        var pdf = await Convert(jpg, FormatRegistry.Jpg, MetadataPolicy.Keep);

        (await File.ReadAllBytesAsync(pdf)).AsSpan().IndexOf("SecretArtistName"u8).ShouldBeGreaterThan(0);
        using var doc = PdfDocument.Open(pdf);
        doc.GetPage(1).Height.ShouldBeGreaterThan(doc.GetPage(1).Width);
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
        using (var image = TestImages.SolidBitmap(100, 300, SKColors.Orange))
        {
            File.WriteAllBytes(png, TestImages.Encode(image, SKEncodedImageFormat.Png));
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
        File.WriteAllBytes(png, PngWriter.Rgb16(50, 40, 0, 0x8000, 0));

        var pdf = await Convert(png, FormatRegistry.Png);

        using var doc = PdfDocument.Open(pdf);
        doc.NumberOfPages.ShouldBe(1);
    }

    [Fact]
    public async Task Multi_page_tiff_becomes_one_page_per_frame()
    {
        // TIFF is decoded by the system codec only; the fake returns one PNG per page.
        var tiff = _dir.File("scan.tiff");
        File.WriteAllBytes(tiff, [0x49, 0x49, 0x2A, 0x00, 8, 0, 0, 0]);
        var codec = Substitute.For<ISystemImageCodec>();
        codec.IsAvailable.Returns(true);
        codec.CanDecode(FormatRegistry.Tiff).Returns(true);
        codec.DecodeToPngFramesAsync(tiff, Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var directory = call.ArgAt<string>(1);
            var pages = new List<string>();
            foreach (var (w, h, color) in new[] { (100, 150, SKColors.Red), (150, 100, SKColors.Green), (100, 150, SKColors.Blue) })
            {
                using var page = TestImages.SolidBitmap(w, h, color);
                var path = Path.Combine(directory, $"frame{pages.Count + 1:000}.png");
                File.WriteAllBytes(path, TestImages.Encode(page, SKEncodedImageFormat.Png));
                pages.Add(path);
            }
            return Task.FromResult<IReadOnlyList<string>>(pages);
        });
        var converter = new ImageToPdfConverter(codec);
        var progress = new NullProgress();

        var result = await converter.ConvertAsync(Info(tiff, FormatRegistry.Tiff, MediaKind.Image), _dir.File("scan.pdf"), To(FormatRegistry.Pdf), progress, CancellationToken.None);

        using var doc = PdfDocument.Open(result.OutputPath);
        doc.NumberOfPages.ShouldBe(3);
        doc.GetPage(2).Width.ShouldBeGreaterThan(doc.GetPage(2).Height);
        progress.Reports.Count(p => p.Phase == ConversionPhase.Converting).ShouldBe(3);
    }

    [Fact]
    public async Task Tiff_without_system_codec_fails_with_missing_codec()
    {
        var tiff = _dir.File("scan2.tiff");
        File.WriteAllBytes(tiff, [0x49, 0x49, 0x2A, 0x00, 8, 0, 0, 0]);

        var ex = await Should.ThrowAsync<ConversionException>(() => Convert(tiff, FormatRegistry.Tiff));

        ex.Code.ShouldBe(ConversionErrorCode.MissingSystemCodec);
        File.Exists(_dir.File("scan2.pdf")).ShouldBeFalse();
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
        using var image = SKBitmap.Decode(stripped);
        image.Width.ShouldBe(64);
        image.Height.ShouldBe(32);
        JpegMetadataStripper.Strip(Encoding.ASCII.GetBytes("not a jpeg")).ShouldBeNull();
    }

    [Fact]
    public void Jpeg_stripper_drops_trailing_data_after_end_of_image()
    {
        var jpg = CreateJpegWithExif("t.jpg", 16, 16);
        var clean = JpegMetadataStripper.Strip(File.ReadAllBytes(jpg));
        clean.ShouldNotBeNull();
        var trailer = Encoding.ASCII.GetBytes("PK\u0003\u0004hidden appended payload with GPS 52.1,13.4");
        var withTrailer = File.ReadAllBytes(jpg).Concat(trailer).ToArray();

        var stripped = JpegMetadataStripper.Strip(withTrailer);

        stripped.ShouldNotBeNull();
        stripped.ShouldBe(clean);
        stripped[^2..].ShouldBe(new byte[] { 0xFF, 0xD9 });
        stripped.AsSpan().IndexOf("hidden appended"u8).ShouldBe(-1);
    }
}
