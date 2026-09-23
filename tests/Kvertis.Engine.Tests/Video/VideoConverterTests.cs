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
    public async Task ProbedH264Mp4IsNotSupportedAndNeverStartsAProcess()
    {
        using var fake = new FakeFfmpeg();
        var cache = new MediaInfoCache();
        var input = TestMedia.Video(fake.CreateInputFile(".mp4"));
        cache.Set(input.Path, FfprobeReader.Parse(TestMedia.VideoJson("h264", "aac")));
        var tools = fake.CreateToolset(cache: cache);
        var video = new VideoConverter(tools);
        var audio = new Kvertis.Engine.Conversion.Audio.AudioConverter(tools);

        video.Supports(input, FormatRegistry.Mp4).ShouldBeFalse();
        video.Supports(input, FormatRegistry.WebM).ShouldBeFalse();
        audio.Supports(input, FormatRegistry.Mp3).ShouldBeFalse();

        foreach (var output in new[] { FormatRegistry.Mp4, FormatRegistry.WebM })
        {
            var ex = await Should.ThrowAsync<ConversionException>(() =>
                video.ConvertAsync(input, fake.NewOutputPath("." + output.Id), new ConversionSettings(output), NoProgress, CancellationToken.None));
            ex.Code.ShouldBe(ConversionErrorCode.UnsupportedFormat);
            ex.Detail.ShouldBe("requires system decoding");
        }
        // Archive stream copy and sound extraction are refused as well.
        (await Should.ThrowAsync<ConversionException>(() => video.ConvertAsync(input, fake.NewOutputPath(".mkv"),
            new ConversionSettings(FormatRegistry.Mkv, Preset: ConversionPreset.Archive), NoProgress, CancellationToken.None))).Code.ShouldBe(ConversionErrorCode.UnsupportedFormat);
        (await Should.ThrowAsync<ConversionException>(() => audio.ConvertAsync(input, fake.NewOutputPath(".mp3"),
            new ConversionSettings(FormatRegistry.Mp3), NoProgress, CancellationToken.None))).Code.ShouldBe(ConversionErrorCode.UnsupportedFormat);
        (await video.PreviewAsync(input, new ConversionSettings(FormatRegistry.WebM), CancellationToken.None)).ShouldBeNull();

        fake.Runner.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task UnprobedHevcIsRefusedAfterProbingBeforeFfmpegStarts()
    {
        using var fake = new FakeFfmpeg { ProbeJson = TestMedia.VideoJson(codec: "hevc") };
        var converter = new VideoConverter(fake.CreateToolset());
        var input = TestMedia.Video(fake.CreateInputFile(".mkv"), format: FormatRegistry.Mkv);

        converter.Supports(input, FormatRegistry.WebM).ShouldBeTrue(); // no probe data yet
        var ex = await Should.ThrowAsync<ConversionException>(() =>
            converter.ConvertAsync(input, fake.NewOutputPath(".webm"), new ConversionSettings(FormatRegistry.WebM), NoProgress, CancellationToken.None));

        ex.Code.ShouldBe(ConversionErrorCode.UnsupportedFormat);
        ex.Detail.ShouldBe("requires system decoding");
        fake.Requests.ShouldHaveSingleItem().ExecutablePath.ShouldBe(FakeFfmpeg.FfprobePath);
    }

    [Fact]
    public async Task PatentFreeWebmToMp4UsesMediaFoundationEncoders()
    {
        using var fake = new FakeFfmpeg { ProbeJson = TestMedia.VideoJson("vp9", "opus") };
        var converter = new VideoConverter(fake.CreateToolset());
        var input = TestMedia.Video(fake.CreateInputFile(".webm"), format: FormatRegistry.WebM);

        await converter.ConvertAsync(input, fake.NewOutputPath(".mp4"), new ConversionSettings(FormatRegistry.Mp4), NoProgress, CancellationToken.None);

        var args = fake.ConversionRequests.ShouldHaveSingleItem().Arguments;
        FfmpegArgumentsTests.ValueAfter(args, "-c:v").ShouldBe("h264_mf");
        FfmpegArgumentsTests.ValueAfter(args, "-c:a").ShouldBe("aac_mf");
        args.ShouldNotContain("-hwaccel");
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
}
