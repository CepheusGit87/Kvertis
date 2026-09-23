using Kvertis.Engine.Abstractions;
using Kvertis.Engine.Conversion.Video;
using Kvertis.Engine.Ffmpeg;
using Kvertis.Engine.Formats;
using Kvertis.Engine.Platform;
using Kvertis.Engine.Probing;
using Kvertis.Engine.Tests.Ffmpeg;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Kvertis.Engine.Tests.Video;

public class VideoConverterTests
{
    private static readonly IProgress<ConversionProgress> NoProgress = new SyncProgress<ConversionProgress>(_ => { });

    [Fact]
    public async Task ConvertsToMp4WithMediaFoundationEncoders()
    {
        using var fake = new FakeFfmpeg { ProbeJson = TestMedia.VideoJson() };
        var converter = new VideoConverter(fake.CreateToolset());
        var input = TestMedia.Video(fake.CreateInputFile(".mov"), format: FormatRegistry.Mov);
        var output = fake.NewOutputPath(".mp4");

        var result = await converter.ConvertAsync(input, output, new ConversionSettings(FormatRegistry.Mp4), NoProgress, CancellationToken.None);

        File.Exists(result.OutputPath).ShouldBeTrue();
        var request = fake.ConversionRequests.ShouldHaveSingleItem();
        FfmpegArgumentsTests.ValueAfter(request.Arguments, "-c:v").ShouldBe("h264_mf");
        FfmpegArgumentsTests.ValueAfter(request.Arguments, "-c:a").ShouldBe("aac_mf");
        FfmpegArgumentsTests.ValueAfter(request.Arguments, "-f").ShouldBe("mp4");
        request.Arguments[^1].ShouldBe(output + ".kvertis-tmp");
        request.Timeout.ShouldBe(TimeSpan.FromHours(6));
    }

    [Fact]
    public async Task Mp4WithoutH264CapabilityIsMissingSystemCodec()
    {
        using var fake = new FakeFfmpeg { ProbeJson = TestMedia.VideoJson() };
        var converter = new VideoConverter(fake.CreateToolset(NullSystemCodecCapabilities.Instance));
        var input = TestMedia.Video(fake.CreateInputFile(".mp4"));

        var ex = await Should.ThrowAsync<ConversionException>(() =>
            converter.ConvertAsync(input, fake.NewOutputPath(".mp4"), new ConversionSettings(FormatRegistry.Mp4), NoProgress, CancellationToken.None));

        ex.Code.ShouldBe(ConversionErrorCode.MissingSystemCodec);
        fake.ConversionRequests.ShouldBeEmpty();
    }

    [Fact]
    public async Task HevcSourceWithoutSystemDecoderIsMissingSystemCodec()
    {
        using var fake = new FakeFfmpeg { ProbeJson = TestMedia.VideoJson(codec: "hevc"), Hwaccels = ["d3d11va"] };
        var codecs = new TestCodecs { CanEncodeH264 = true, CanEncodeAac = true, CanDecodeHevc = false };
        var converter = new VideoConverter(fake.CreateToolset(codecs));
        var input = TestMedia.Video(fake.CreateInputFile(".mp4"));

        var ex = await Should.ThrowAsync<ConversionException>(() =>
            converter.ConvertAsync(input, fake.NewOutputPath(".webm"), new ConversionSettings(FormatRegistry.WebM), NoProgress, CancellationToken.None));

        ex.Code.ShouldBe(ConversionErrorCode.MissingSystemCodec);
        fake.ConversionRequests.ShouldBeEmpty();
    }

    [Fact]
    public async Task UsesCachedProbeDataInsteadOfSecondFfprobe()
    {
        using var fake = new FakeFfmpeg { ProbeJson = TestMedia.VideoJson(rRate: "60/1", avgRate: "25/1") };
        var cache = new MediaInfoCache();
        var tools = fake.CreateToolset(cache: cache);
        var input = TestMedia.Video(fake.CreateInputFile(".mp4"));
        var prober = new MediaProber(tools.Reader, fake.Locator, cache);
        input = await prober.ProbeAsync(input, CancellationToken.None);

        await new VideoConverter(tools).ConvertAsync(input, fake.NewOutputPath(".mkv"), new ConversionSettings(FormatRegistry.Mkv), NoProgress, CancellationToken.None);

        fake.Requests.Count(r => r.ExecutablePath == FakeFfmpeg.FfprobePath).ShouldBe(1);
        var args = fake.ConversionRequests.ShouldHaveSingleItem().Arguments;
        FfmpegArgumentsTests.ValueAfter(args, "-fps_mode").ShouldBe("cfr");
        FfmpegArgumentsTests.ValueAfter(args, "-c:v").ShouldBe("libvpx-vp9");
    }

    [Fact]
    public async Task ArchiveMkvCopiesStreams()
    {
        using var fake = new FakeFfmpeg { ProbeJson = TestMedia.VideoJson() };
        var converter = new VideoConverter(fake.CreateToolset(NullSystemCodecCapabilities.Instance));
        var input = TestMedia.Video(fake.CreateInputFile(".mp4"));

        await converter.ConvertAsync(input, fake.NewOutputPath(".mkv"), new ConversionSettings(FormatRegistry.Mkv, Preset: ConversionPreset.Archive), NoProgress, CancellationToken.None);

        var args = fake.ConversionRequests.ShouldHaveSingleItem().Arguments;
        FfmpegArgumentsTests.ValueAfter(args, "-c").ShouldBe("copy");
        args.ShouldNotContain("-c:v");
    }

    [Fact]
    public async Task DiskFullStderrIsInsufficientDiskSpace()
    {
        using var fake = new FakeFfmpeg
        {
            ProbeJson = TestMedia.VideoJson(),
            ConversionOutcome = new ProcessOutcome(1, string.Empty, "Error writing trailer: No space left on device", TimeSpan.Zero, false),
        };
        var converter = new VideoConverter(fake.CreateToolset());
        var input = TestMedia.Video(fake.CreateInputFile(".mp4"));

        (await Should.ThrowAsync<ConversionException>(() =>
            converter.ConvertAsync(input, fake.NewOutputPath(".webm"), new ConversionSettings(FormatRegistry.WebM), NoProgress, CancellationToken.None)))
            .Code.ShouldBe(ConversionErrorCode.InsufficientDiskSpace);
    }

    [Fact]
    public void SupportsOnlyVideoToVideoContainers()
    {
        using var fake = new FakeFfmpeg();
        var converter = new VideoConverter(fake.CreateToolset());
        converter.Name.ShouldBe("video");
        converter.Supports(TestMedia.Video("/v.avi", format: FormatRegistry.Avi), FormatRegistry.Mp4).ShouldBeTrue();
        converter.Supports(TestMedia.Video("/v.avi"), FormatRegistry.WebM).ShouldBeTrue();
        converter.Supports(TestMedia.Video("/v.avi"), FormatRegistry.Mp3).ShouldBeFalse();
        converter.Supports(TestMedia.Audio("/a.wav"), FormatRegistry.Mp4).ShouldBeFalse();
    }

    [Fact]
    public async Task PreviewExtractsOneFrameAtQuarterDuration()
    {
        using var fake = new FakeFfmpeg { ProbeJson = TestMedia.VideoJson() };
        var converter = new VideoConverter(fake.CreateToolset());
        var input = TestMedia.Video(fake.CreateInputFile(".mp4"));
        var settings = new ConversionSettings(FormatRegistry.Mp4, TargetSizeBytes: 16L * 1024 * 1024);

        var preview = await converter.PreviewAsync(input, settings, CancellationToken.None);

        preview.ShouldNotBeNull();
        fake.Track(preview.PreviewPath);
        preview.PreviewPath.ShouldEndWith(".png");
        var args = fake.ConversionRequests.ShouldHaveSingleItem().Arguments;
        FfmpegArgumentsTests.ValueAfter(args, "-ss").ShouldBe("15");
        FfmpegArgumentsTests.ValueAfter(args, "-frames:v").ShouldBe("1");
        FfmpegArgumentsTests.ValueAfter(args, "-c:v").ShouldBe("png");
        // (2044 + 192) kbit/s over 60 s.
        preview.EstimatedOutputBytes.ShouldBe((2044 + 192) * 1000L / 8 * 60);
    }

    private const string FallbackLine = "[hevc @ 0000020a] Failed setup for format d3d11va: hwaccel initialisation returned error.";

    [Fact]
    public async Task HevcSoftwareFallbackIsRefused()
    {
        using var fake = new FakeFfmpeg { ProbeJson = TestMedia.VideoJson(codec: "hevc"), Hwaccels = ["d3d11va"], StderrLines = [FallbackLine] };
        var converter = new VideoConverter(fake.CreateToolset());
        var input = TestMedia.Video(fake.CreateInputFile(".mp4"));
        var output = fake.NewOutputPath(".webm");

        var ex = await Should.ThrowAsync<ConversionException>(() =>
            converter.ConvertAsync(input, output, new ConversionSettings(FormatRegistry.WebM), NoProgress, CancellationToken.None));

        ex.Code.ShouldBe(ConversionErrorCode.MissingSystemCodec);
        ex.Detail.ShouldBe("hevc software fallback refused");
        fake.ConversionRequests.ShouldHaveSingleItem().Arguments.ShouldContain("-hwaccel");
        File.Exists(output).ShouldBeFalse();
        File.Exists(output + ".kvertis-tmp").ShouldBeFalse();
    }

    [Fact]
    public async Task HevcSoftwareFallbackCancelsTheRunningProcess()
    {
        using var fake = new FakeFfmpeg { ProbeJson = TestMedia.VideoJson(codec: "hevc"), Hwaccels = ["d3d11va"] };
        var tools = fake.CreateToolset();
        var input = TestMedia.Video(fake.CreateInputFile(".mp4"));
        var context = await tools.PrepareAsync(input, CancellationToken.None);
        var blocking = Substitute.For<IProcessRunner>();
        var observedCancel = false;
        blocking.RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<IProgress<string>?>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                var ct = call.Arg<CancellationToken>();
                call.Arg<IProgress<string>?>()!.Report(FallbackLine);
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(30), ct);
                }
                catch (OperationCanceledException)
                {
                    observedCancel = true;
                    throw new ConversionException(ConversionErrorCode.Cancelled, step: "ffmpeg");
                }
                return new ProcessOutcome(0, string.Empty, string.Empty, TimeSpan.Zero, false);
            });
        var guarded = new FfmpegToolset(fake.Locator, blocking, tools.Codecs, tools.Features, tools.Compliance, tools.Reader, tools.Cache, tools.Registry);

        var ex = await Should.ThrowAsync<ConversionException>(() =>
            guarded.RunFfmpegAsync(FakeFfmpeg.FfmpegPath, ["-hwaccel", "d3d11va", "-i", input.Path, "out"], input, null, "convert", CancellationToken.None, context.Media));

        observedCancel.ShouldBeTrue();
        ex.Code.ShouldBe(ConversionErrorCode.MissingSystemCodec);
        ex.Detail.ShouldBe("hevc software fallback refused");
    }

    [Fact]
    public async Task H264WithoutHardwareDecodeIsNotGuarded()
    {
        using var fake = new FakeFfmpeg { ProbeJson = TestMedia.VideoJson(), Hwaccels = ["d3d11va"], StderrLines = [FallbackLine] };
        var converter = new VideoConverter(fake.CreateToolset());
        var input = TestMedia.Video(fake.CreateInputFile(".mp4"));

        var result = await converter.ConvertAsync(input, fake.NewOutputPath(".webm"), new ConversionSettings(FormatRegistry.WebM), NoProgress, CancellationToken.None);

        File.Exists(result.OutputPath).ShouldBeTrue();
    }
}
