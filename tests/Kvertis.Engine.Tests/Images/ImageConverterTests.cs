using System.Globalization;
using ImageMagick;
using Kvertis.Engine.Abstractions;
using Kvertis.Engine.Conversion;
using Kvertis.Engine.Conversion.Images;
using Kvertis.Engine.Formats;
using Kvertis.Engine.Platform;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Kvertis.Engine.Tests.Images;

public sealed class ImageConverterTests : IDisposable
{
    private readonly TestImages _files = new();
    private readonly FormatRegistry _registry = new();

    public void Dispose() => _files.Dispose();

    private ImageConverter CreateConverter(IHeicDecoder? heic = null) =>
        new(_registry, heic ?? NullHeicDecoder.Instance, NullLogger<ImageConverter>.Instance);

    private async Task<(ConversionResult Result, RecordingProgress Progress)> ConvertAsync(
        InputInfo input, ConversionSettings settings, string outputName, IHeicDecoder? heic = null, CancellationToken ct = default)
    {
        var progress = new RecordingProgress();
        var result = await CreateConverter(heic).ConvertAsync(input, _files.PathFor(outputName), settings, progress, ct);
        return (result, progress);
    }

    private static MagickFormat FormatOf(string path) => new MagickImageInfo(path).Format;

    [Fact]
    public async Task Png_to_jpg_writes_a_jpeg_with_same_dimensions()
    {
        var input = TestImages.Info(_files.Solid("in.png", MagickFormat.Png), FormatRegistry.Png);

        var (result, progress) = await ConvertAsync(input, new ConversionSettings(FormatRegistry.Jpg), "out.jpg");

        File.Exists(result.OutputPath).ShouldBeTrue();
        File.Exists(result.OutputPath + ".kvertis-tmp").ShouldBeFalse();
        result.OutputBytes.ShouldBe(new FileInfo(result.OutputPath).Length);
        File.ReadAllBytes(result.OutputPath).Take(3).ShouldBe(new byte[] { 0xFF, 0xD8, 0xFF });
        using var output = new MagickImage(result.OutputPath);
        output.Width.ShouldBe(64u);
        output.Height.ShouldBe(48u);
        progress.Reports[^1].ShouldBe(ConversionProgress.Complete);
    }

    [Fact]
    public async Task Jpg_to_webp_writes_webp()
    {
        var input = TestImages.Info(_files.Noisy("in.jpg", MagickFormat.Jpeg), FormatRegistry.Jpg);

        var (result, _) = await ConvertAsync(input, new ConversionSettings(FormatRegistry.WebP, Quality: 70), "out.webp");

        FormatOf(result.OutputPath).ShouldBe(MagickFormat.WebP);
    }

    [Fact]
    public async Task Higher_quality_produces_larger_jpeg()
    {
        var input = TestImages.Info(_files.Noisy("in.png", MagickFormat.Png), FormatRegistry.Png);

        var (low, _) = await ConvertAsync(input, new ConversionSettings(FormatRegistry.Jpg, Quality: 30), "low.jpg");
        var (high, _) = await ConvertAsync(input, new ConversionSettings(FormatRegistry.Jpg, Quality: 95), "high.jpg");

        high.OutputBytes.ShouldBeGreaterThan(low.OutputBytes);
    }

    [Fact]
    public async Task Transparent_png_to_jpg_is_flattened_on_white()
    {
        using (var image = new MagickImage(MagickColors.Transparent, 20, 20))
        {
            _files.Write(image, "alpha.png", MagickFormat.Png);
        }
        var input = TestImages.Info(_files.PathFor("alpha.png"), FormatRegistry.Png);

        var (result, _) = await ConvertAsync(input, new ConversionSettings(FormatRegistry.Jpg), "flat.jpg");

        using var output = new MagickImage(result.OutputPath);
        output.HasAlpha.ShouldBeFalse();
        var pixel = output.GetPixels().GetPixel(10, 10).ToColor()!;
        pixel.R.ShouldBeGreaterThan((ushort)64000);
        pixel.G.ShouldBeGreaterThan((ushort)64000);
        pixel.B.ShouldBeGreaterThan((ushort)64000);
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

        using var frames = new MagickImageCollection(result.OutputPath);
        frames.Count.ShouldBe(1);
        var pixel = frames[0].GetPixels().GetPixel(5, 5).ToColor()!;
        pixel.R.ShouldBe(ushort.MaxValue);
        pixel.G.ShouldBe((ushort)0);
        pixel.B.ShouldBe((ushort)0);
    }

    [Theory]
    [InlineData("jpg")]
    [InlineData("png")]
    [InlineData("webp")]
    public async Task Strip_removes_exif_and_comment_but_keeps_icc(string output)
    {
        var input = TestImages.Info(_files.WithExifAndIcc("meta.jpg", MagickFormat.Jpeg), FormatRegistry.Jpg);
        using (var source = new MagickImage(input.Path))
        {
            source.GetExifProfile().ShouldNotBeNull();
            source.GetColorProfile().ShouldNotBeNull();
        }

        var (result, _) = await ConvertAsync(input, new ConversionSettings(new FormatId(output)), "stripped." + output);

        using var image = new MagickImage(result.OutputPath);
        image.GetExifProfile().ShouldBeNull();
        image.GetXmpProfile().ShouldBeNull();
        image.GetIptcProfile().ShouldBeNull();
        image.Comment.ShouldBeNull();
        var icc = image.GetColorProfile();
        icc.ShouldNotBeNull();
        icc.ColorSpace.ShouldBe(ColorSpace.sRGB);
        File.ReadAllText(result.OutputPath, System.Text.Encoding.Latin1).ShouldNotContain("TestCamera");
    }

    [Fact]
    public async Task Keep_policy_keeps_exif()
    {
        var input = TestImages.Info(_files.WithExifAndIcc("meta.jpg", MagickFormat.Jpeg), FormatRegistry.Jpg);

        var (result, _) = await ConvertAsync(input, new ConversionSettings(FormatRegistry.Jpg, Metadata: MetadataPolicy.Keep), "kept.jpg");

        using var image = new MagickImage(result.OutputPath);
        var exif = image.GetExifProfile();
        exif.ShouldNotBeNull();
        exif.GetValue(ExifTag.Make)!.Value.ShouldBe("TestCamera");
        image.GetColorProfile().ShouldNotBeNull();
    }

    [Theory]
    [InlineData(32, 32u, 24u)]
    [InlineData(200, 64u, 48u)] // never upscale
    [InlineData(0, 64u, 48u)]   // 0 = keep
    public async Task MaxDimension_shrinks_longest_edge_keeping_aspect(int maxDimension, uint expectedWidth, uint expectedHeight)
    {
        var input = TestImages.Info(_files.Solid("in.png", MagickFormat.Png), FormatRegistry.Png);
        var settings = new ConversionSettings(
            FormatRegistry.Png,
            Advanced: new Dictionary<string, string> { [ConversionSettings.AdvancedKeys.MaxDimension] = maxDimension.ToString(CultureInfo.InvariantCulture) });

        var (result, _) = await ConvertAsync(input, settings, "sized.png");

        using var image = new MagickImage(result.OutputPath);
        image.Width.ShouldBe(expectedWidth);
        image.Height.ShouldBe(expectedHeight);
    }

    [Fact]
    public async Task Ico_output_is_limited_to_256_px()
    {
        var input = TestImages.Info(_files.Solid("big.png", MagickFormat.Png, 600, 300), FormatRegistry.Png);

        var (result, _) = await ConvertAsync(input, new ConversionSettings(FormatRegistry.Ico), "icon.ico");

        using var image = new MagickImage(result.OutputPath, MagickFormat.Ico);
        Math.Max(image.Width, image.Height).ShouldBe(256u);
    }

    [Fact]
    public async Task Tiff_output_uses_lzw()
    {
        var input = TestImages.Info(_files.Solid("in.png", MagickFormat.Png), FormatRegistry.Png);

        var (result, _) = await ConvertAsync(input, new ConversionSettings(FormatRegistry.Tiff), "out.tiff");

        using var image = new MagickImage(result.OutputPath);
        image.Compression.ShouldBe(CompressionMethod.LZW);
    }

    [Fact]
    public async Task Gif_output_has_at_most_256_colors()
    {
        var input = TestImages.Info(_files.Noisy("in.png", MagickFormat.Png, 100, 100), FormatRegistry.Png);

        var (result, _) = await ConvertAsync(input, new ConversionSettings(FormatRegistry.Gif), "out.gif");

        using var image = new MagickImage(result.OutputPath);
        image.TotalColors.ShouldBeLessThanOrEqualTo(256u);
    }

    [Fact]
    public async Task Target_size_is_reached_for_jpg()
    {
        var input = TestImages.Info(_files.Noisy("in.png", MagickFormat.Png), FormatRegistry.Png);
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
        var input = TestImages.Info(_files.Noisy("in.png", MagickFormat.Png), FormatRegistry.Png);
        var (floor, _) = await ConvertAsync(input, new ConversionSettings(FormatRegistry.WebP, Quality: 20), "floor.webp");
        var target = floor.OutputBytes * 2 / 3;

        var (result, _) = await ConvertAsync(input, new ConversionSettings(FormatRegistry.WebP, TargetSizeBytes: target), "target.webp");

        result.OutputBytes.ShouldBeLessThanOrEqualTo(target);
        using var image = new MagickImage(result.OutputPath);
        image.Width.ShouldBeLessThan(400u);
        image.Width.ShouldBeGreaterThanOrEqualTo(100u);
    }

    [Fact]
    public async Task Unreachable_target_size_reports_smallest_size()
    {
        var input = TestImages.Info(_files.Noisy("in.png", MagickFormat.Png), FormatRegistry.Png);

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
        var input = TestImages.Info(_files.Noisy("in.png", MagickFormat.Png), FormatRegistry.Png);

        var (result, _) = await ConvertAsync(input, new ConversionSettings(FormatRegistry.Png, TargetSizeBytes: 100), "big.png");

        result.OutputBytes.ShouldBeGreaterThan(100);
    }

    [Fact]
    public async Task Heic_without_system_decoder_fails_with_missing_codec()
    {
        var input = TestImages.Info(_files.Bytes("photo.heic", HeicHeader()), FormatRegistry.Heic);

        var ex = await Should.ThrowAsync<ConversionException>(
            () => ConvertAsync(input, new ConversionSettings(FormatRegistry.Jpg), "photo.jpg"));

        ex.Code.ShouldBe(ConversionErrorCode.MissingSystemCodec);
    }

    [Fact]
    public async Task Heic_is_decoded_by_system_decoder_and_never_by_magick()
    {
        // The HEIC file is not decodable at all: if Magick.NET ever touched it, the conversion would fail.
        var input = TestImages.Info(_files.Bytes("photo.heic", HeicHeader()), FormatRegistry.Heic);
        var decoder = Substitute.For<IHeicDecoder>();
        decoder.IsAvailable.Returns(true);
        string? decodedPath = null;
        decoder.DecodeToPngAsync(input.Path, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                decodedPath = call.ArgAt<string>(1);
                using var image = new MagickImage(MagickColors.Blue, 40, 30);
                image.Write(decodedPath, MagickFormat.Png);
                return Task.CompletedTask;
            });

        var (result, _) = await ConvertAsync(input, new ConversionSettings(FormatRegistry.Jpg), "photo.jpg", decoder);

        await decoder.Received(1).DecodeToPngAsync(input.Path, Arg.Any<string>(), Arg.Any<CancellationToken>());
        using var output = new MagickImage(result.OutputPath);
        output.Width.ShouldBe(40u);
        decodedPath.ShouldNotBeNull();
        File.Exists(decodedPath).ShouldBeFalse(); // temp PNG cleaned up
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
    public async Task Truncated_jpeg_does_not_produce_unknown_error()
    {
        var full = File.ReadAllBytes(_files.Noisy("full.jpg", MagickFormat.Jpeg));
        var input = TestImages.Info(_files.Bytes("cut.jpg", full[..(full.Length / 3)]), FormatRegistry.Jpg);

        try
        {
            // libjpeg may recover a partial image (a warning only); that is acceptable.
            await ConvertAsync(input, new ConversionSettings(FormatRegistry.Png), "cut.png");
        }
        catch (ConversionException ex)
        {
            ex.Code.ShouldBe(ConversionErrorCode.CorruptFile);
        }
    }

    [Fact]
    public async Task Cancelled_token_fails_with_cancelled()
    {
        var input = TestImages.Info(_files.Solid("in.png", MagickFormat.Png), FormatRegistry.Png);
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
        var input = TestImages.Info(_files.Noisy("in.png", MagickFormat.Png), FormatRegistry.Png);
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
        var input = TestImages.Info(_files.Solid("in.png", MagickFormat.Png), FormatRegistry.Png);
        File.WriteAllText(_files.PathFor("taken.jpg"), "keep me");

        var ex = await Should.ThrowAsync<ConversionException>(
            () => ConvertAsync(input, new ConversionSettings(FormatRegistry.Jpg), "taken.jpg"));

        ex.Code.ShouldBe(ConversionErrorCode.OutputExists);
        File.ReadAllText(_files.PathFor("taken.jpg")).ShouldBe("keep me");
    }

    [Fact]
    public async Task Progress_runs_through_phases_in_order()
    {
        var input = TestImages.Info(_files.Noisy("in.png", MagickFormat.Png), FormatRegistry.Png);

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
        converter.Name.ShouldBe("image");
    }

    [Fact]
    public async Task Preview_is_capped_at_1024_px_and_estimates_size()
    {
        var input = TestImages.Info(_files.Noisy("big.png", MagickFormat.Png, 2000, 1000), FormatRegistry.Png);
        var settings = new ConversionSettings(FormatRegistry.Jpg, Quality: 80);

        var preview = await CreateConverter().PreviewAsync(input, settings, CancellationToken.None);

        preview.ShouldNotBeNull();
        try
        {
            preview.PreviewPath.ShouldStartWith(Path.Combine(Path.GetTempPath(), "Kvertis"));
            using var image = new MagickImage(preview.PreviewPath);
            image.Width.ShouldBe(1024u);
            var (result, _) = await ConvertAsync(input, settings, "real.jpg");
            preview.EstimatedOutputBytes.ShouldBe(result.OutputBytes);
        }
        finally
        {
            File.Delete(preview.PreviewPath);
        }
    }

    [Fact]
    public async Task Preview_returns_null_for_unsupported_output()
    {
        var input = TestImages.Info(_files.Solid("in.png", MagickFormat.Png), FormatRegistry.Png);

        var preview = await CreateConverter().PreviewAsync(input, new ConversionSettings(FormatRegistry.Pdf), CancellationToken.None);

        preview.ShouldBeNull();
    }

    [Fact]
    public async Task Archive_preset_png_is_lossless_and_keeps_metadata()
    {
        var input = TestImages.Info(_files.WithExifAndIcc("meta.png", MagickFormat.Png), FormatRegistry.Png);
        var settings = PresetCatalog.Apply(new ConversionSettings(FormatRegistry.Jpg, Preset: ConversionPreset.Archive), MediaKind.Image);

        var (result, _) = await ConvertAsync(input, settings, "archive.png");

        using var original = new MagickImage(input.Path);
        using var output = new MagickImage(result.OutputPath);
        output.Compare(original, ErrorMetric.Absolute).ShouldBe(0);
        output.GetExifProfile().ShouldNotBeNull();
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
