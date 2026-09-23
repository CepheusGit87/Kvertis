using System.Globalization;
using Kvertis.Engine.Abstractions;
using Kvertis.Engine.Conversion;
using Kvertis.Engine.Conversion.Images;
using Kvertis.Engine.Formats;
using Kvertis.Engine.Platform;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using SkiaSharp;
using Xunit;

namespace Kvertis.Engine.Tests.Images;

public sealed class ImageConverterTests : IDisposable
{
    private readonly TestImages _files = new();
    private readonly FormatRegistry _registry = new();

    public void Dispose() => _files.Dispose();

    private ImageConverter CreateConverter(ISystemImageCodec? codec = null) =>
        new(_registry, codec ?? NullSystemImageCodec.Instance, NullLogger<ImageConverter>.Instance);

    private async Task<(ConversionResult Result, RecordingProgress Progress)> ConvertAsync(
        InputInfo input, ConversionSettings settings, string outputName, ISystemImageCodec? codec = null, CancellationToken ct = default)
    {
        var progress = new RecordingProgress();
        var result = await CreateConverter(codec).ConvertAsync(input, _files.PathFor(outputName), settings, progress, ct);
        return (result, progress);
    }

    /// <summary>A fake system codec that "decodes" into the given PNG frames and "encodes" by copying the PNG.</summary>
    private ISystemImageCodec FakeSystemCodec(params SKColor[] frames)
    {
        var codec = Substitute.For<ISystemImageCodec>();
        codec.IsAvailable.Returns(true);
        codec.CanDecode(Arg.Any<FormatId>()).Returns(true);
        codec.CanEncode(Arg.Any<FormatId>()).Returns(true);
        codec.DecodeToPngFramesAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var directory = call.ArgAt<string>(1);
                var max = call.ArgAt<int>(2);
                var paths = new List<string>();
                for (var i = 0; i < Math.Min(max, frames.Length); i++)
                {
                    using var bitmap = TestImages.SolidBitmap(40 + i * 10, 30, frames[i]);
                    var path = Path.Combine(directory, $"frame{i + 1:000}.png");
                    File.WriteAllBytes(path, TestImages.Encode(bitmap, SKEncodedImageFormat.Png));
                    paths.Add(path);
                }
                return Task.FromResult<IReadOnlyList<string>>(paths);
            });
        codec.EncodeFromPngAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<FormatId>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                File.Copy(call.ArgAt<string>(0), call.ArgAt<string>(1), overwrite: true);
                return Task.CompletedTask;
            });
        return codec;
    }

    [Fact]
    public async Task Png_to_jpg_writes_a_jpeg_with_same_dimensions()
    {
        var input = TestImages.Info(_files.Solid("in.png", SKEncodedImageFormat.Png), FormatRegistry.Png);

        var (result, progress) = await ConvertAsync(input, new ConversionSettings(FormatRegistry.Jpg), "out.jpg");

        File.Exists(result.OutputPath).ShouldBeTrue();
        File.Exists(result.OutputPath + ".kvertis-tmp").ShouldBeFalse();
        result.OutputBytes.ShouldBe(new FileInfo(result.OutputPath).Length);
        File.ReadAllBytes(result.OutputPath).Take(3).ShouldBe(new byte[] { 0xFF, 0xD8, 0xFF });
        using var output = TestImages.Load(result.OutputPath);
        output.Width.ShouldBe(64);
        output.Height.ShouldBe(48);
        progress.Reports[^1].ShouldBe(ConversionProgress.Complete);
    }

    [Fact]
    public async Task Jpg_to_webp_writes_webp()
    {
        var input = TestImages.Info(_files.Noisy("in.jpg", SKEncodedImageFormat.Jpeg), FormatRegistry.Jpg);

        var (result, _) = await ConvertAsync(input, new ConversionSettings(FormatRegistry.WebP, Quality: 70), "out.webp");

        TestImages.FormatOf(result.OutputPath).ShouldBe(SKEncodedImageFormat.Webp);
    }

    [Fact]
    public async Task Higher_quality_produces_larger_jpeg()
    {
        var input = TestImages.Info(_files.Noisy("in.png", SKEncodedImageFormat.Png), FormatRegistry.Png);

        var (low, _) = await ConvertAsync(input, new ConversionSettings(FormatRegistry.Jpg, Quality: 30), "low.jpg");
        var (high, _) = await ConvertAsync(input, new ConversionSettings(FormatRegistry.Jpg, Quality: 95), "high.jpg");

        high.OutputBytes.ShouldBeGreaterThan(low.OutputBytes);
    }

    [Fact]
    public async Task Transparent_png_to_jpg_is_flattened_on_white()
    {
        using (var bitmap = TestImages.SolidBitmap(20, 20, SKColors.Transparent))
        {
            _files.Write(bitmap, "alpha.png", SKEncodedImageFormat.Png);
        }
        var input = TestImages.Info(_files.PathFor("alpha.png"), FormatRegistry.Png);

        var (result, _) = await ConvertAsync(input, new ConversionSettings(FormatRegistry.Jpg), "flat.jpg");

        using var output = TestImages.Load(result.OutputPath);
        var pixel = output.GetPixel(10, 10);
        pixel.Alpha.ShouldBe((byte)255);
        pixel.Red.ShouldBeGreaterThan((byte)250);
        pixel.Green.ShouldBeGreaterThan((byte)250);
        pixel.Blue.ShouldBeGreaterThan((byte)250);
    }

    [Fact]
    public async Task Semi_transparent_png_to_png_keeps_alpha()
    {
        using (var bitmap = TestImages.SolidBitmap(20, 20, new SKColor(255, 0, 0, 128)))
        {
            _files.Write(bitmap, "half.png", SKEncodedImageFormat.Png);
        }
        var input = TestImages.Info(_files.PathFor("half.png"), FormatRegistry.Png);

        var (result, _) = await ConvertAsync(input, new ConversionSettings(FormatRegistry.Png), "half-out.png");
        var (resized, _) = await ConvertAsync(
            input,
            new ConversionSettings(FormatRegistry.Png, Advanced: new Dictionary<string, string> { [ConversionSettings.AdvancedKeys.MaxDimension] = "10" }),
            "half-small.png");

        using var output = TestImages.Load(result.OutputPath);
        output.GetPixel(5, 5).Alpha.ShouldBe((byte)128);
        using var small = TestImages.Load(resized.OutputPath);
        small.Width.ShouldBe(10);
        var pixel = small.GetPixel(5, 5);
        ((int)pixel.Alpha).ShouldBeInRange(126, 130);
        ((int)pixel.Red).ShouldBeGreaterThan(250); // unpremultiplied resize must not darken colours
    }

    [Fact]
    public void WillLoseTransparency_only_for_outputs_without_alpha()
    {
        var png = new InputInfo("x.png", FormatRegistry.Png, MediaKind.Image, 1, null, null, null, null, []);
        var jpg = png with { Format = FormatRegistry.Jpg };

        ImageConverter.WillLoseTransparency(png, FormatRegistry.Jpg, _registry).ShouldBeTrue();
        ImageConverter.WillLoseTransparency(png, FormatRegistry.Bmp, _registry).ShouldBeTrue();
        ImageConverter.WillLoseTransparency(png, FormatRegistry.WebP, _registry).ShouldBeFalse();
        ImageConverter.WillLoseTransparency(jpg, FormatRegistry.Png, _registry).ShouldBeFalse();
    }

    [Fact]
    public async Task Animated_gif_to_png_keeps_first_frame_only()
    {
        var input = TestImages.Info(_files.AnimatedGif("anim.gif"), FormatRegistry.Gif);

        var (result, _) = await ConvertAsync(input, new ConversionSettings(FormatRegistry.Png), "first.png");

        using var codec = SKCodec.Create(result.OutputPath);
        codec.FrameCount.ShouldBeLessThanOrEqualTo(1);
        using var output = TestImages.Load(result.OutputPath);
        output.GetPixel(5, 5).ShouldBe(new SKColor(255, 0, 0, 255));
    }

    [Theory]
    [InlineData("jpg")]
    [InlineData("png")]
    [InlineData("webp")]
    public async Task Strip_removes_exif(string output)
    {
        var input = TestImages.Info(_files.JpegWithExif("meta.jpg"), FormatRegistry.Jpg);
        File.ReadAllBytes(input.Path).AsSpan().IndexOf("TestCamera"u8).ShouldBeGreaterThan(0);

        var (result, _) = await ConvertAsync(input, new ConversionSettings(new FormatId(output)), "stripped." + output);

        var bytes = File.ReadAllBytes(result.OutputPath);
        bytes.AsSpan().IndexOf("TestCamera"u8).ShouldBe(-1);
        bytes.AsSpan().IndexOf("Exif\0\0"u8).ShouldBe(-1);
        bytes.AsSpan().IndexOf("ICC_PROFILE"u8).ShouldBe(-1); // colors are converted to sRGB, no profile embedded
        bytes.AsSpan().IndexOf("iCCP"u8).ShouldBe(-1);
        if (output == "jpg")
        {
            JpegSegments.HasApp1(bytes).ShouldBeFalse();
        }
    }

    [Fact]
    public async Task Keep_policy_copies_exif_for_jpg_to_jpg_with_upright_orientation()
    {
        // 64x48 stored, orientation 6 (rotate 90°): displayed and converted as 48x64.
        var input = TestImages.Info(_files.JpegWithExif("meta.jpg", orientation: 6), FormatRegistry.Jpg);

        var (result, _) = await ConvertAsync(input, new ConversionSettings(FormatRegistry.Jpg, Metadata: MetadataPolicy.Keep), "kept.jpg");

        var bytes = File.ReadAllBytes(result.OutputPath);
        JpegSegments.HasApp1(bytes).ShouldBeTrue();
        bytes.AsSpan().IndexOf("TestCamera"u8).ShouldBeGreaterThan(0);
        using var codec = SKCodec.Create(result.OutputPath);
        codec.EncodedOrigin.ShouldBe(SKEncodedOrigin.TopLeft); // tag reset, pixels already rotated
        codec.Info.Width.ShouldBe(48);
        codec.Info.Height.ShouldBe(64);
    }

    [Fact]
    public async Task Keep_policy_copies_exif_for_jpg_to_webp()
    {
        var input = TestImages.Info(_files.JpegWithExif("meta.jpg"), FormatRegistry.Jpg);

        var (result, _) = await ConvertAsync(input, new ConversionSettings(FormatRegistry.WebP, Metadata: MetadataPolicy.Keep), "kept.webp");

        var bytes = File.ReadAllBytes(result.OutputPath);
        bytes.AsSpan().IndexOf("EXIF"u8).ShouldBeGreaterThan(0);
        bytes.AsSpan().IndexOf("TestCamera"u8).ShouldBeGreaterThan(0);
        using var output = TestImages.Load(result.OutputPath);
        output.Width.ShouldBe(64);
    }

    [Fact]
    public async Task Strip_applies_exif_orientation_to_pixels()
    {
        var input = TestImages.Info(_files.JpegWithExif("rot.jpg", 64, 48, orientation: 8), FormatRegistry.Jpg);

        var (result, _) = await ConvertAsync(input, new ConversionSettings(FormatRegistry.Png), "rot.png");

        using var output = TestImages.Load(result.OutputPath);
        output.Width.ShouldBe(48);
        output.Height.ShouldBe(64);
    }

    [Theory]
    [InlineData(32, 32, 24)]
    [InlineData(200, 64, 48)] // never upscale
    [InlineData(0, 64, 48)]   // 0 = keep
    public async Task MaxDimension_shrinks_longest_edge_keeping_aspect(int maxDimension, int expectedWidth, int expectedHeight)
    {
        var input = TestImages.Info(_files.Solid("in.png", SKEncodedImageFormat.Png), FormatRegistry.Png);
        var settings = new ConversionSettings(
            FormatRegistry.Png,
            Advanced: new Dictionary<string, string> { [ConversionSettings.AdvancedKeys.MaxDimension] = maxDimension.ToString(CultureInfo.InvariantCulture) });

        var (result, _) = await ConvertAsync(input, settings, "sized.png");

        using var image = TestImages.Load(result.OutputPath);
        image.Width.ShouldBe(expectedWidth);
        image.Height.ShouldBe(expectedHeight);
    }

    [Fact]
    public async Task Ico_output_is_limited_to_256_px_and_decodable()
    {
        var input = TestImages.Info(_files.Solid("big.png", SKEncodedImageFormat.Png, 600, 300), FormatRegistry.Png);

        var (result, _) = await ConvertAsync(input, new ConversionSettings(FormatRegistry.Ico), "icon.ico");

        var bytes = File.ReadAllBytes(result.OutputPath);
        bytes[..6].ShouldBe(new byte[] { 0, 0, 1, 0, 1, 0 }); // ICONDIR: icon, one image
        bytes[6].ShouldBe((byte)0);   // width 256 is stored as 0
        bytes[7].ShouldBe((byte)128);
        BitConverter.ToUInt32(bytes, 14).ShouldBe((uint)(bytes.Length - 22));
        BitConverter.ToUInt32(bytes, 18).ShouldBe(22u);
        TestImages.FormatOf(result.OutputPath).ShouldBe(SKEncodedImageFormat.Ico);
        using var image = TestImages.Load(result.OutputPath);
        image.Width.ShouldBe(256);
        image.Height.ShouldBe(128);
    }

    [Fact]
    public void IcoWriter_writes_valid_header_for_small_icons()
    {
        using var bitmap = TestImages.SolidBitmap(16, 16, SKColors.Blue);
        var png = TestImages.Encode(bitmap, SKEncodedImageFormat.Png);

        var ico = IcoWriter.Write(png, 16, 16);

        ico[6].ShouldBe((byte)16);
        ico[7].ShouldBe((byte)16);
        BitConverter.ToUInt16(ico, 12).ShouldBe((ushort)32);
        using var decoded = SKBitmap.Decode(ico);
        decoded.ShouldNotBeNull();
        decoded.Width.ShouldBe(16);
        Should.Throw<ArgumentOutOfRangeException>(() => IcoWriter.Write(png, 257, 16));
    }

    [Theory]
    [InlineData("tiff")]
    [InlineData("bmp")]
    [InlineData("gif")]
    public async Task System_encoded_output_without_system_codec_fails_with_missing_codec(string output)
    {
        var input = TestImages.Info(_files.Solid("in.png", SKEncodedImageFormat.Png), FormatRegistry.Png);

        var ex = await Should.ThrowAsync<ConversionException>(
            () => ConvertAsync(input, new ConversionSettings(new FormatId(output)), "out." + output));

        ex.Code.ShouldBe(ConversionErrorCode.MissingSystemCodec);
        File.Exists(_files.PathFor("out." + output)).ShouldBeFalse();
    }

    [Fact]
    public async Task System_encoded_output_goes_through_system_encoder()
    {
        var input = TestImages.Info(_files.Solid("in.png", SKEncodedImageFormat.Png), FormatRegistry.Png);
        var codec = FakeSystemCodec();

        var (result, _) = await ConvertAsync(input, new ConversionSettings(FormatRegistry.Bmp, Quality: 77), "out.bmp", codec);

        await codec.Received(1).EncodeFromPngAsync(Arg.Any<string>(), result.OutputPath + ".kvertis-tmp", FormatRegistry.Bmp, 77, Arg.Any<CancellationToken>());
        File.Exists(result.OutputPath).ShouldBeTrue();
        Directory.GetFiles(_files.Directory, "*.kvertis-tmp.png").ShouldBeEmpty();
    }

    [Fact]
    public async Task Target_size_is_reached_for_jpg()
    {
        var input = TestImages.Info(_files.Noisy("in.png", SKEncodedImageFormat.Png), FormatRegistry.Png);
        var (full, _) = await ConvertAsync(input, new ConversionSettings(FormatRegistry.Jpg, Quality: 95), "full.jpg");
        var target = full.OutputBytes / 2;

        var (result, progress) = await ConvertAsync(input, new ConversionSettings(FormatRegistry.Jpg, TargetSizeBytes: target), "target.jpg");

        result.OutputBytes.ShouldBeLessThanOrEqualTo(target);
        result.OutputBytes.ShouldBeGreaterThan(target / 4); // should not collapse needlessly
        progress.Reports.Select(r => r.Phase).ShouldContain(ConversionPhase.Optimizing);
    }

    [Fact]
    public async Task Target_size_below_quality_floor_shrinks_resolution()
    {
        var input = TestImages.Info(_files.Noisy("in.png", SKEncodedImageFormat.Png), FormatRegistry.Png);
        var (floor, _) = await ConvertAsync(input, new ConversionSettings(FormatRegistry.WebP, Quality: 20), "floor.webp");
        var target = floor.OutputBytes * 2 / 3;

        var (result, _) = await ConvertAsync(input, new ConversionSettings(FormatRegistry.WebP, TargetSizeBytes: target), "target.webp");

        result.OutputBytes.ShouldBeLessThanOrEqualTo(target);
        using var image = TestImages.Load(result.OutputPath);
        image.Width.ShouldBeLessThan(400);
        image.Width.ShouldBeGreaterThanOrEqualTo(100);
    }

    [Fact]
    public async Task Unreachable_target_size_reports_smallest_size()
    {
        var input = TestImages.Info(_files.Noisy("in.png", SKEncodedImageFormat.Png), FormatRegistry.Png);

        var ex = await Should.ThrowAsync<ConversionException>(
            () => ConvertAsync(input, new ConversionSettings(FormatRegistry.Jpg, TargetSizeBytes: 100), "never.jpg"));

        ex.Code.ShouldBe(ConversionErrorCode.TargetSizeUnreachable);
        long.Parse(ex.Detail!, CultureInfo.InvariantCulture).ShouldBeGreaterThan(100);
        ex.FilePath.ShouldBe(input.Path);
        File.Exists(_files.PathFor("never.jpg")).ShouldBeFalse();
        File.Exists(_files.PathFor("never.jpg.kvertis-tmp")).ShouldBeFalse();
    }

    [Fact]
    public async Task Lossless_output_ignores_target_size()
    {
        var input = TestImages.Info(_files.Noisy("in.png", SKEncodedImageFormat.Png), FormatRegistry.Png);

        var (result, _) = await ConvertAsync(input, new ConversionSettings(FormatRegistry.Png, TargetSizeBytes: 100), "big.png");

        result.OutputBytes.ShouldBeGreaterThan(100);
    }

    [Theory]
    [InlineData("heic")]
    [InlineData("avif")]
    [InlineData("tiff")]
    public async Task System_decoded_input_without_system_codec_fails_with_missing_codec(string format)
    {
        var input = TestImages.Info(_files.Bytes("photo." + format, HeicHeader()), new FormatId(format));

        var ex = await Should.ThrowAsync<ConversionException>(
            () => ConvertAsync(input, new ConversionSettings(FormatRegistry.Jpg), "photo.jpg"));

        ex.Code.ShouldBe(ConversionErrorCode.MissingSystemCodec);
        File.Exists(_files.PathFor("photo.jpg")).ShouldBeFalse();
    }

    [Fact]
    public async Task Non_dng_raw_without_system_codec_fails_with_missing_codec()
    {
        var input = TestImages.Info(_files.Bytes("photo.cr2", [0x49, 0x49, 0x2A, 0x00, 0x10, 0, 0, 0, (byte)'C', (byte)'R', 2, 0, 0, 0, 0, 0]), FormatRegistry.Raw);

        var ex = await Should.ThrowAsync<ConversionException>(
            () => ConvertAsync(input, new ConversionSettings(FormatRegistry.Jpg), "photo.jpg"));

        ex.Code.ShouldBe(ConversionErrorCode.MissingSystemCodec);
    }

    [Fact]
    public async Task Heic_is_decoded_by_system_codec_and_never_by_skia()
    {
        // The HEIC file is not decodable at all: if Skia ever touched it, the conversion would fail.
        var input = TestImages.Info(_files.Bytes("photo.heic", HeicHeader()), FormatRegistry.Heic);
        var codec = FakeSystemCodec(SKColors.Blue);
        string? scratch = null;
        codec.When(c => c.DecodeToPngFramesAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>()))
            .Do(call => scratch = call.ArgAt<string>(1));

        var (result, _) = await ConvertAsync(input, new ConversionSettings(FormatRegistry.Jpg), "photo.jpg", codec);

        await codec.Received(1).DecodeToPngFramesAsync(input.Path, Arg.Any<string>(), ImageConverter.MaxPages, Arg.Any<CancellationToken>());
        using var output = TestImages.Load(result.OutputPath);
        output.Width.ShouldBe(40);
        scratch.ShouldNotBeNull();
        Directory.Exists(scratch).ShouldBeFalse(); // temp PNGs cleaned up
    }

    [Fact]
    public async Task Multi_page_tiff_becomes_one_output_per_page()
    {
        var input = TestImages.Info(_files.Bytes("scan.tiff", [0x49, 0x49, 0x2A, 0x00, 8, 0, 0, 0]), FormatRegistry.Tiff);
        var codec = FakeSystemCodec(SKColors.Red, SKColors.Green);
        File.WriteAllText(_files.PathFor("scan_p001.png"), "keep me");

        var (result, progress) = await ConvertAsync(input, new ConversionSettings(FormatRegistry.Png), "scan.png", codec);

        result.OutputPath.ShouldBe(_files.PathFor("scan_p001_1.png"));
        File.ReadAllText(_files.PathFor("scan_p001.png")).ShouldBe("keep me");
        File.Exists(_files.PathFor("scan.png")).ShouldBeFalse();
        using (var first = TestImages.Load(result.OutputPath))
        {
            first.Width.ShouldBe(40);
            first.GetPixel(1, 1).Red.ShouldBe((byte)255);
        }
        using (var second = TestImages.Load(_files.PathFor("scan_p002.png")))
        {
            second.Width.ShouldBe(50);
        }
        result.OutputBytes.ShouldBe(new FileInfo(result.OutputPath).Length + new FileInfo(_files.PathFor("scan_p002.png")).Length);
        progress.Reports.Select(r => r.Fraction).ShouldBeInOrder(SortDirection.Ascending);
    }

    [Fact]
    public async Task Corrupt_file_fails_with_corrupt_file()
    {
        byte[] header = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        var input = TestImages.Info(_files.Bytes("broken.png", [.. header, .. new byte[200]]), FormatRegistry.Png);

        var ex = await Should.ThrowAsync<ConversionException>(
            () => ConvertAsync(input, new ConversionSettings(FormatRegistry.Jpg), "broken.jpg"));

        ex.Code.ShouldBe(ConversionErrorCode.CorruptFile);
        File.Exists(_files.PathFor("broken.jpg.kvertis-tmp")).ShouldBeFalse();
    }

    [Fact]
    public async Task Content_that_is_not_the_detected_format_fails_with_corrupt_file()
    {
        // A valid PNG handed in as "jpg": Skia must not silently decode whatever it finds.
        var input = TestImages.Info(_files.Solid("fake.jpg", SKEncodedImageFormat.Png), FormatRegistry.Jpg);

        var ex = await Should.ThrowAsync<ConversionException>(
            () => ConvertAsync(input, new ConversionSettings(FormatRegistry.Png), "fake.png"));

        ex.Code.ShouldBe(ConversionErrorCode.CorruptFile);
    }

    [Fact]
    public async Task Truncated_jpeg_fails_with_corrupt_file()
    {
        var full = File.ReadAllBytes(_files.Noisy("full.jpg", SKEncodedImageFormat.Jpeg));
        var input = TestImages.Info(_files.Bytes("cut.jpg", full[..(full.Length / 3)]), FormatRegistry.Jpg);

        var ex = await Should.ThrowAsync<ConversionException>(
            () => ConvertAsync(input, new ConversionSettings(FormatRegistry.Png), "cut.png"));

        ex.Code.ShouldBe(ConversionErrorCode.CorruptFile);
    }

    [Fact]
    public async Task Cancelled_token_fails_with_cancelled()
    {
        var input = TestImages.Info(_files.Solid("in.png", SKEncodedImageFormat.Png), FormatRegistry.Png);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var ex = await Should.ThrowAsync<ConversionException>(
            () => ConvertAsync(input, new ConversionSettings(FormatRegistry.Jpg), "out.jpg", ct: cts.Token));

        ex.Code.ShouldBe(ConversionErrorCode.Cancelled);
        File.Exists(_files.PathFor("out.jpg")).ShouldBeFalse();
    }

    [Fact]
    public async Task Cancellation_during_target_search_fails_with_cancelled()
    {
        var input = TestImages.Info(_files.Noisy("in.png", SKEncodedImageFormat.Png), FormatRegistry.Png);
        using var cts = new CancellationTokenSource();
        var progress = new CancelOnPhase(ConversionPhase.Optimizing, cts);

        var ex = await Should.ThrowAsync<ConversionException>(() => CreateConverter().ConvertAsync(
            input, _files.PathFor("out.jpg"), new ConversionSettings(FormatRegistry.Jpg, TargetSizeBytes: 100), progress, cts.Token));

        ex.Code.ShouldBe(ConversionErrorCode.Cancelled);
        File.Exists(_files.PathFor("out.jpg.kvertis-tmp")).ShouldBeFalse();
    }

    [Fact]
    public async Task Existing_output_fails_with_output_exists()
    {
        var input = TestImages.Info(_files.Solid("in.png", SKEncodedImageFormat.Png), FormatRegistry.Png);
        File.WriteAllText(_files.PathFor("taken.jpg"), "keep me");

        var ex = await Should.ThrowAsync<ConversionException>(
            () => ConvertAsync(input, new ConversionSettings(FormatRegistry.Jpg), "taken.jpg"));

        ex.Code.ShouldBe(ConversionErrorCode.OutputExists);
        File.ReadAllText(_files.PathFor("taken.jpg")).ShouldBe("keep me");
    }

    [Fact]
    public async Task Progress_runs_through_phases_in_order()
    {
        var input = TestImages.Info(_files.Noisy("in.png", SKEncodedImageFormat.Png), FormatRegistry.Png);

        var (_, progress) = await ConvertAsync(input, new ConversionSettings(FormatRegistry.Jpg, TargetSizeBytes: 20_000), "p.jpg");

        var phases = progress.Reports.Select(r => r.Phase).Distinct().ToList();
        phases.ShouldBe([ConversionPhase.Analyzing, ConversionPhase.Converting, ConversionPhase.Optimizing, ConversionPhase.Finalizing, ConversionPhase.Done]);
        progress.Reports.Select(r => r.Fraction).ShouldBeInOrder(SortDirection.Ascending);
    }

    [Fact]
    public void Supports_image_outputs_but_not_pdf_or_other_kinds()
    {
        var converter = CreateConverter();
        var png = new InputInfo("x.png", FormatRegistry.Png, MediaKind.Image, 1, null, null, null, null, []);
        var wav = new InputInfo("x.wav", FormatRegistry.Wav, MediaKind.Audio, 1, null, null, null, null, []);

        foreach (var format in new[] { FormatRegistry.Png, FormatRegistry.Jpg, FormatRegistry.WebP, FormatRegistry.Gif, FormatRegistry.Bmp, FormatRegistry.Tiff, FormatRegistry.Ico })
        {
            converter.Supports(png, format).ShouldBeTrue(format.Id);
        }
        converter.Supports(png, FormatRegistry.Pdf).ShouldBeFalse();
        converter.Supports(png, FormatRegistry.Heic).ShouldBeFalse();
        converter.Supports(wav, FormatRegistry.Mp3).ShouldBeFalse();
        converter.Supports(png with { Format = FormatRegistry.Heic }, FormatRegistry.Jpg).ShouldBeTrue();
        converter.Supports(png with { Format = FormatRegistry.Avif }, FormatRegistry.Jpg).ShouldBeTrue();
        converter.Supports(png with { Format = FormatRegistry.Psd }, FormatRegistry.Png).ShouldBeFalse();
        converter.Supports(png with { Format = FormatRegistry.Svg }, FormatRegistry.Png).ShouldBeFalse();
        converter.Name.ShouldBe("image");
    }

    [Fact]
    public async Task Preview_is_capped_at_1024_px_and_estimates_size()
    {
        var input = TestImages.Info(_files.Noisy("big.png", SKEncodedImageFormat.Png, 2000, 1000), FormatRegistry.Png);
        var settings = new ConversionSettings(FormatRegistry.Jpg, Quality: 80);

        var preview = await CreateConverter().PreviewAsync(input, settings, CancellationToken.None);

        preview.ShouldNotBeNull();
        try
        {
            preview.PreviewPath.ShouldStartWith(Path.Combine(Path.GetTempPath(), "Kvertis"));
            using var image = TestImages.Load(preview.PreviewPath);
            image.Width.ShouldBe(1024);
            var (result, _) = await ConvertAsync(input, settings, "real.jpg");
            preview.EstimatedOutputBytes.ShouldBe(result.OutputBytes);
        }
        finally
        {
            File.Delete(preview.PreviewPath);
        }
    }

    [Fact]
    public async Task Preview_returns_null_for_unsupported_output_or_missing_encoder()
    {
        var input = TestImages.Info(_files.Solid("in.png", SKEncodedImageFormat.Png), FormatRegistry.Png);

        (await CreateConverter().PreviewAsync(input, new ConversionSettings(FormatRegistry.Pdf), CancellationToken.None)).ShouldBeNull();
        (await CreateConverter().PreviewAsync(input, new ConversionSettings(FormatRegistry.Tiff), CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    public async Task Archive_preset_png_is_lossless()
    {
        var input = TestImages.Info(_files.Noisy("noisy.png", SKEncodedImageFormat.Png, 120, 80), FormatRegistry.Png);
        var settings = PresetCatalog.Apply(new ConversionSettings(FormatRegistry.Jpg, Preset: ConversionPreset.Archive), MediaKind.Image);

        var (result, _) = await ConvertAsync(input, settings, "archive.png");

        using var original = TestImages.Load(input.Path);
        using var output = TestImages.Load(result.OutputPath);
        output.Width.ShouldBe(original.Width);
        output.Pixels.ShouldBe(original.Pixels);
    }

    private static byte[] HeicHeader() =>
    [
        0x00, 0x00, 0x00, 0x18, (byte)'f', (byte)'t', (byte)'y', (byte)'p', (byte)'h', (byte)'e', (byte)'i', (byte)'c',
        0x00, 0x00, 0x00, 0x00, (byte)'m', (byte)'i', (byte)'f', (byte)'1', (byte)'h', (byte)'e', (byte)'i', (byte)'c',
        0xDE, 0xAD, 0xBE, 0xEF, 0x00, 0x01, 0x02, 0x03,
    ];

    private sealed class CancelOnPhase(ConversionPhase phase, CancellationTokenSource cts) : IProgress<ConversionProgress>
    {
        public void Report(ConversionProgress value)
        {
            if (value.Phase == phase)
            {
                cts.Cancel();
            }
        }
    }
}
