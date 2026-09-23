using Kvertis.Engine.Abstractions;
using Kvertis.Engine.Formats;
using Kvertis.Engine.Probing;
using Shouldly;
using SkiaSharp;
using Xunit;

namespace Kvertis.Engine.Tests.Images;

public sealed class ImageProberTests : IDisposable
{
    private readonly TestImages _files = new();
    private readonly ImageProber _prober = new();

    public void Dispose() => _files.Dispose();

    [Fact]
    public void Supports_only_images()
    {
        _prober.Supports(MediaKind.Image).ShouldBeTrue();
        _prober.Supports(MediaKind.Video).ShouldBeFalse();
        _prober.Supports(MediaKind.Document).ShouldBeFalse();
    }

    [Theory]
    [InlineData("png", SKEncodedImageFormat.Png)]
    [InlineData("jpg", SKEncodedImageFormat.Jpeg)]
    [InlineData("webp", SKEncodedImageFormat.Webp)]
    public async Task Reads_dimensions(string format, SKEncodedImageFormat skiaFormat)
    {
        var info = TestImages.Info(_files.Solid("in." + format, skiaFormat, 123, 45), new FormatId(format));

        var result = await _prober.ProbeAsync(info, CancellationToken.None);

        result.Width.ShouldBe(123);
        result.Height.ShouldBe(45);
        result.Warnings.ShouldBeEmpty();
    }

    [Fact]
    public async Task Animated_gif_gets_animation_dropped_warning()
    {
        var info = TestImages.Info(_files.AnimatedGif("anim.gif"), FormatRegistry.Gif);

        var result = await _prober.ProbeAsync(info, CancellationToken.None);

        result.Width.ShouldBe(32);
        result.HasWarning(InputWarning.AnimationDropped).ShouldBeTrue();
        result.HasWarning(InputWarning.TransparencyLost).ShouldBeFalse();
    }

    [Fact]
    public async Task Single_frame_gif_has_no_animation_warning()
    {
        var info = TestImages.Info(_files.StillGif("still.gif"), FormatRegistry.Gif);

        var result = await _prober.ProbeAsync(info, CancellationToken.None);

        result.Width.ShouldBe(64);
        result.Height.ShouldBe(48);
        result.HasWarning(InputWarning.AnimationDropped).ShouldBeFalse();
    }

    [Theory]
    [InlineData("heic")]
    [InlineData("avif")]
    [InlineData("tiff")]
    [InlineData("raw")]
    public async Task System_codec_formats_are_not_touched_and_keep_unknown_dimensions(string format)
    {
        byte[] garbage = [0x00, 0x00, 0x00, 0x18, (byte)'f', (byte)'t', (byte)'y', (byte)'p', (byte)'h', (byte)'e', (byte)'i', (byte)'c', 0xDE, 0xAD];
        var info = TestImages.Info(_files.Bytes("photo." + format, garbage), new FormatId(format));

        var result = await _prober.ProbeAsync(info, CancellationToken.None);

        result.ShouldBe(info);
        result.Width.ShouldBeNull();
    }

    [Fact]
    public async Task Png_content_detected_as_jpg_throws_corrupt_file()
    {
        var info = TestImages.Info(_files.Solid("fake.jpg", SKEncodedImageFormat.Png), FormatRegistry.Jpg);

        var ex = await Should.ThrowAsync<ConversionException>(() => _prober.ProbeAsync(info, CancellationToken.None));

        ex.Code.ShouldBe(ConversionErrorCode.CorruptFile);
    }

    [Fact]
    public async Task Corrupt_png_throws_corrupt_file()
    {
        byte[] content = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4, 5, 6, 7, 8, 9];
        var info = TestImages.Info(_files.Bytes("broken.png", content), FormatRegistry.Png);

        var ex = await Should.ThrowAsync<ConversionException>(() => _prober.ProbeAsync(info, CancellationToken.None));

        ex.Code.ShouldBe(ConversionErrorCode.CorruptFile);
    }

    [Fact]
    public async Task Cancelled_probe_reports_cancelled()
    {
        var info = TestImages.Info(_files.Solid("in.png", SKEncodedImageFormat.Png), FormatRegistry.Png);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var ex = await Should.ThrowAsync<ConversionException>(() => _prober.ProbeAsync(info, cts.Token));

        ex.Code.ShouldBe(ConversionErrorCode.Cancelled);
    }

    [Fact]
    public async Task Detector_uses_prober_for_dimensions()
    {
        var path = _files.Solid("in.png", SKEncodedImageFormat.Png, 80, 60);
        var detector = new FormatDetector(new FormatRegistry(), [_prober]);

        var info = await detector.DetectAsync(path, CancellationToken.None);

        info.Format.ShouldBe(FormatRegistry.Png);
        info.Width.ShouldBe(80);
        info.Height.ShouldBe(60);
    }
}
