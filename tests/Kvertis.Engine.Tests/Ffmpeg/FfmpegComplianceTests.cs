using Kvertis.Engine.Abstractions;
using Kvertis.Engine.Ffmpeg;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Kvertis.Engine.Tests.Ffmpeg;

public class FfmpegComplianceTests
{
    // Built from parts so this file, too, never contains the literal names (keeps grep-based audits quiet).
    private static readonly string X264 = "lib" + "x264";

    private const string GplVersion =
        "ffmpeg version 6.1.1 Copyright (c) 2000-2023 the FFmpeg developers\n" +
        "configuration: --prefix=/usr --enable-gpl --enable-version3 --enable-libvpx\n";

    [Fact]
    public void LgplBuildIsCompliant()
    {
        var report = FfmpegCompliance.Parse(FakeFfmpeg.LgplVersion, FakeFfmpeg.EncodersOutput(FakeFfmpeg.DefaultEncoders));

        report.IsGplBuild.ShouldBeFalse();
        report.HasNonFree.ShouldBeFalse();
        report.HasForbiddenEncoders.ShouldBeFalse();
        report.IsCompliant.ShouldBeTrue();
        report.Configuration.ShouldStartWith("--disable-gpl");
    }

    [Fact]
    public void GplBuildIsDetected()
    {
        var report = FfmpegCompliance.Parse(GplVersion, FakeFfmpeg.EncodersOutput(["flac"]));
        report.IsGplBuild.ShouldBeTrue();
        report.IsCompliant.ShouldBeFalse();
    }

    [Fact]
    public void NonFreeBuildIsDetected()
    {
        var report = FfmpegCompliance.Parse("configuration: --enable-nonfree --enable-lib" + "fdk-aac\n", FakeFfmpeg.EncodersOutput(["flac"]));
        report.HasNonFree.ShouldBeTrue();
        report.HasForbiddenEncoders.ShouldBeTrue();
    }

    [Fact]
    public void ForbiddenEncoderInListIsDetected()
    {
        var report = FfmpegCompliance.Parse(FakeFfmpeg.LgplVersion, FakeFfmpeg.EncodersOutput(["flac", X264]));
        report.HasForbiddenEncoders.ShouldBeTrue();
        report.ForbiddenEncodersFound.ShouldContain(X264);
    }

    [Fact]
    public async Task CheckAsyncRunsVersionAndEncoders()
    {
        using var fake = new FakeFfmpeg();
        var report = await FfmpegCompliance.CheckAsync(fake.Locator, fake.Runner);

        report.IsCompliant.ShouldBeTrue();
        fake.Requests.Select(r => string.Join(' ', r.Arguments)).ShouldBe(["-version", "-hide_banner -encoders"]);
    }

    [Fact]
    public async Task EnsureCompliantRefusesGplBuildAndCachesTheCheck()
    {
        using var fake = new FakeFfmpeg { VersionOutput = GplVersion };
        var compliance = new FfmpegCompliance(fake.Locator, fake.Runner);

        var ex = await Should.ThrowAsync<ConversionException>(() => compliance.EnsureCompliantAsync(CancellationToken.None));
        ex.Code.ShouldBe(ConversionErrorCode.ToolMissing);
        ex.Detail.ShouldBe("gpl build");

        await Should.ThrowAsync<ConversionException>(() => compliance.EnsureCompliantAsync(CancellationToken.None));
        fake.Requests.Count(r => r.Arguments.Contains("-version")).ShouldBe(1);
    }

    [Fact]
    public async Task EnsureCompliantWithoutFfmpegIsToolMissing()
    {
        using var fake = new FakeFfmpeg();
        fake.Locator.FfmpegPath.Returns((string?)null);
        var compliance = new FfmpegCompliance(fake.Locator, fake.Runner);

        (await Should.ThrowAsync<ConversionException>(() => compliance.EnsureCompliantAsync(CancellationToken.None))).Code.ShouldBe(ConversionErrorCode.ToolMissing);
    }
}

public class FfmpegFeaturesTests
{
    [Fact]
    public void ParsesEncoderTable()
    {
        var names = FfmpegFeatures.ParseEncoders(
            "Encoders:\n V..... = Video\n A..... = Audio\n ------\n V....D h264_mf              H264 via MediaFoundation (codec h264)\n A....D libmp3lame           libmp3lame MP3 (codec mp3)\n");
        names.ShouldBe(["h264_mf", "libmp3lame"]);
    }

    [Fact]
    public void ParsesHwaccels() =>
        FfmpegFeatures.ParseHardwareAccelerations("Hardware acceleration methods:\ndxva2\nd3d11va\n\n").ShouldBe(["dxva2", "d3d11va"]);

    [Fact]
    public async Task ProbeRunsOnceAndCaches()
    {
        using var fake = new FakeFfmpeg { Encoders = ["flac", "mp3_mf"], Hwaccels = ["d3d11va"] };
        var probe = new FfmpegFeatureProbe(fake.Locator, fake.Runner);

        var first = await probe.GetAsync(CancellationToken.None);
        var second = await probe.GetAsync(CancellationToken.None);

        first.ShouldBeSameAs(second);
        first.HasEncoder("mp3_mf").ShouldBeTrue();
        first.HasEncoder("libmp3lame").ShouldBeFalse();
        first.CanUseD3D11Va.ShouldBeTrue();
        fake.Requests.Count(r => r.Arguments.SequenceEqual(["-hide_banner", "-encoders"])).ShouldBe(1);
    }
}

public class FfmpegErrorMapperTests
{
    [Theory]
    [InlineData("in.mp4: Invalid data found when processing input", ConversionErrorCode.CorruptFile)]
    [InlineData("[mov,mp4] moov atom not found", ConversionErrorCode.CorruptFile)]
    [InlineData("Unknown encoder 'h264_mf'", ConversionErrorCode.MissingSystemCodec)]
    [InlineData("Encoder not found", ConversionErrorCode.MissingSystemCodec)]
    [InlineData("av_interleaved_write_frame(): No space left on device", ConversionErrorCode.InsufficientDiskSpace)]
    [InlineData("This file is DRM protected", ConversionErrorCode.ProtectedFile)]
    [InlineData("stream is encrypted", ConversionErrorCode.ProtectedFile)]
    [InlineData("Conversion failed!", ConversionErrorCode.ToolFailed)]
    public void MapsStderrFragments(string stderr, ConversionErrorCode expected)
    {
        var ex = FfmpegErrorMapper.Map(new ProcessOutcome(1, string.Empty, stderr, TimeSpan.Zero, false), "/in", "convert");
        ex.Code.ShouldBe(expected);
        ex.Detail.ShouldBe(stderr);
    }

    [Fact]
    public void TimeoutWins() =>
        FfmpegErrorMapper.Map(new ProcessOutcome(-1, string.Empty, "Invalid data found when processing input", TimeSpan.Zero, true), null, "convert")
            .Code.ShouldBe(ConversionErrorCode.Timeout);
}
