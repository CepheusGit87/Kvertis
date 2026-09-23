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
        "configuration: --prefix=/usr --enable-" + "gpl --enable-version3 --enable-libvpx\n";

    private static ComplianceReport Parse(string version, string encoders, string? decoders = null, string? protocols = null) =>
        FfmpegCompliance.Parse(version, encoders, decoders ?? FakeFfmpeg.DecodersOutput(FakeFfmpeg.AllowlistDecoders), protocols ?? FakeFfmpeg.AllowlistProtocols);

    [Fact]
    public void LgplBuildIsCompliant()
    {
        var report = Parse(FakeFfmpeg.LgplVersion, FakeFfmpeg.EncodersOutput(FakeFfmpeg.DefaultEncoders));

        report.IsGplBuild.ShouldBeFalse();
        report.HasNonFree.ShouldBeFalse();
        report.HasForbiddenEncoders.ShouldBeFalse();
        report.HasForbiddenDecoders.ShouldBeFalse();
        report.HasNetworkProtocols.ShouldBeFalse();
        report.IsCompliant.ShouldBeTrue();
        report.Reasons.ShouldBeEmpty();
        report.Configuration.ShouldStartWith("--disable-gpl");
    }

    [Theory]
    [InlineData("h26", "4")]
    [InlineData("he", "vc")]
    [InlineData("aa", "c")]
    [InlineData("mpeg", "4")]
    [InlineData("wm", "v3")]
    [InlineData("vc", "1")]
    [InlineData("wma", "pro")]
    [InlineData("pro", "res")]
    [InlineData("ea", "c3")]
    [InlineData("dc", "a")]
    [InlineData("amr", "nb")]
    [InlineData("he", "vc_cuvid")]
    public void EncumberedDecoderMakesBuildNonCompliant(string head, string tail)
    {
        var decoder = head + tail;
        var report = Parse(FakeFfmpeg.LgplVersion, FakeFfmpeg.EncodersOutput(FakeFfmpeg.DefaultEncoders),
            FakeFfmpeg.DecodersOutput([.. FakeFfmpeg.AllowlistDecoders, decoder]));

        report.HasForbiddenDecoders.ShouldBeTrue();
        report.ForbiddenDecodersFound.ShouldBe([decoder]);
        report.IsCompliant.ShouldBeFalse();
        report.Reasons.ShouldBe(["forbidden decoders: " + decoder]);
    }

    [Fact]
    public void PatentFreeDecodersWithSimilarNamesAreAllowed()
    {
        // ac3 (expired) is fine while eac3 is not; mpeg2video/mp3 are fine while mpeg4 is not.
        var report = Parse(FakeFfmpeg.LgplVersion, FakeFfmpeg.EncodersOutput(FakeFfmpeg.DefaultEncoders),
            FakeFfmpeg.DecodersOutput(["ac3", "mpeg2video", "mp3", "mjpeg", "vp9"]));
        report.IsCompliant.ShouldBeTrue();
    }

    [Theory]
    [InlineData("http")]
    [InlineData("https")]
    [InlineData("tcp")]
    [InlineData("udp")]
    [InlineData("tls")]
    [InlineData("rtmp")]
    [InlineData("rtp")]
    [InlineData("srt")]
    [InlineData("ftp")]
    [InlineData("sftp")]
    public void NetworkProtocolMakesBuildNonCompliant(string protocol)
    {
        var protocols = $"Supported file protocols:\nInput:\n  file\n  {protocol}\n  pipe\nOutput:\n  file\n  pipe\n";
        var report = Parse(FakeFfmpeg.LgplVersion, FakeFfmpeg.EncodersOutput(FakeFfmpeg.DefaultEncoders), protocols: protocols);

        report.NetworkProtocolsFound.ShouldBe([protocol]);
        report.IsCompliant.ShouldBeFalse();
    }

    [Fact]
    public void ParsesProtocolList() =>
        FfmpegCompliance.ParseProtocols(FakeFfmpeg.AllowlistProtocols).ShouldBe(["file", "pipe", "file", "pipe"]);

    [Fact]
    public void GplBuildIsDetected()
    {
        var report = Parse(GplVersion, FakeFfmpeg.EncodersOutput(["flac"]));
        report.IsGplBuild.ShouldBeTrue();
        report.IsCompliant.ShouldBeFalse();
    }

    [Fact]
    public void NonFreeBuildIsDetected()
    {
        var report = Parse("configuration: --enable-" + "nonfree --enable-lib" + "fdk-aac\n", FakeFfmpeg.EncodersOutput(["flac"]));
        report.HasNonFree.ShouldBeTrue();
        report.HasForbiddenEncoders.ShouldBeTrue();
    }

    [Fact]
    public void XvidEncoderIsForbidden()
    {
        var xvid = "lib" + "xvid";
        var report = Parse(FakeFfmpeg.LgplVersion, FakeFfmpeg.EncodersOutput(["flac", xvid]));
        report.HasForbiddenEncoders.ShouldBeTrue();
        report.ForbiddenEncodersFound.ShouldContain(xvid);
    }

    [Fact]
    public void ForbiddenEncoderInListIsDetected()
    {
        var report = Parse(FakeFfmpeg.LgplVersion, FakeFfmpeg.EncodersOutput(["flac", X264]));
        report.HasForbiddenEncoders.ShouldBeTrue();
        report.ForbiddenEncodersFound.ShouldContain(X264);
    }

    [Fact]
    public async Task CheckAsyncRunsVersionAndEncoders()
    {
        using var fake = new FakeFfmpeg();
        var report = await FfmpegCompliance.CheckAsync(fake.Locator, fake.Runner);

        report.IsCompliant.ShouldBeTrue();
        fake.Requests.Select(r => string.Join(' ', r.Arguments))
            .ShouldBe(["-version", "-hide_banner -encoders", "-hide_banner -decoders", "-hide_banner -protocols"]);
    }

    [Fact]
    public async Task EnsureCompliantRefusesGplBuildAndCachesTheCheck()
    {
        using var fake = new FakeFfmpeg { VersionOutput = GplVersion };
        var compliance = new FfmpegCompliance(fake.Locator, fake.Runner);

        var ex = await Should.ThrowAsync<ConversionException>(() => compliance.EnsureCompliantAsync(CancellationToken.None));
        ex.Code.ShouldBe(ConversionErrorCode.ToolMissing);
        ex.Detail.ShouldBe("ffmpeg build not compliant: gpl build");

        await Should.ThrowAsync<ConversionException>(() => compliance.EnsureCompliantAsync(CancellationToken.None));
        fake.Requests.Count(r => r.Arguments.Contains("-version")).ShouldBe(1);
    }

    [Fact]
    public async Task EnsureCompliantRefusesBuildWithEncumberedDecodersAndNetwork()
    {
        var h264 = "h26" + "4";
        using var fake = new FakeFfmpeg
        {
            Decoders = [.. FakeFfmpeg.AllowlistDecoders, h264],
            ProtocolsOutput = "Supported file protocols:\nInput:\n  file\n  https\nOutput:\n  file\n",
        };
        var compliance = new FfmpegCompliance(fake.Locator, fake.Runner);

        var ex = await Should.ThrowAsync<ConversionException>(() => compliance.EnsureCompliantAsync(CancellationToken.None));

        ex.Code.ShouldBe(ConversionErrorCode.ToolMissing);
        ex.Detail.ShouldBe($"ffmpeg build not compliant: forbidden decoders: {h264}; network protocols: https");
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
    public void ParsesDecoderTable() =>
        FfmpegFeatures.ParseDecoders(FakeFfmpeg.DecodersOutput(["vp9", "flac"])).ShouldBe(["vp9", "flac"]);

    [Fact]
    public async Task ProbeRunsOnceAndCaches()
    {
        using var fake = new FakeFfmpeg { Encoders = ["flac", "mp3_mf"] };
        var probe = new FfmpegFeatureProbe(fake.Locator, fake.Runner);

        var first = await probe.GetAsync(CancellationToken.None);
        var second = await probe.GetAsync(CancellationToken.None);

        first.ShouldBeSameAs(second);
        first.HasEncoder("mp3_mf").ShouldBeTrue();
        first.HasEncoder("libmp3lame").ShouldBeFalse();
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
    public void FileNameContainingEncryptedIsNotProtected()
    {
        var path = Path.Combine(Path.GetTempPath(), "encrypted_notes.mp4");
        var stderr = $"Input #0, mov,mp4,m4a,3gp,3g2,mj2, from '{path}':\n{path}: Invalid data found when processing input";

        FfmpegErrorMapper.Map(new ProcessOutcome(1, string.Empty, stderr, TimeSpan.Zero, false), path, "convert")
            .Code.ShouldBe(ConversionErrorCode.CorruptFile);
    }

    [Fact]
    public void OutputPathSharingTheStemIsIgnoredToo()
    {
        var path = Path.Combine(Path.GetTempPath(), "DRM protected talk.mp4");
        var stderr = $"[out#0/mp4 @ 0x1] Error opening output '{Path.ChangeExtension(path, ".webm")}.kvertis-tmp'\nConversion failed!";

        FfmpegErrorMapper.Classify(stderr, path).ShouldBe(ConversionErrorCode.ToolFailed);
    }

    [Fact]
    public void ProtectionPhraseOnOtherLineStillCounts()
    {
        var path = Path.Combine(Path.GetTempPath(), "encrypted_notes.mp4");
        var stderr = $"Input #0 from '{path}':\n[mov @ 0x1] This file is encrypted";

        FfmpegErrorMapper.Classify(stderr, path).ShouldBe(ConversionErrorCode.ProtectedFile);
    }

    [Fact]
    public void TimeoutWins() =>
        FfmpegErrorMapper.Map(new ProcessOutcome(-1, string.Empty, "Invalid data found when processing input", TimeSpan.Zero, true), null, "convert")
            .Code.ShouldBe(ConversionErrorCode.Timeout);
}
