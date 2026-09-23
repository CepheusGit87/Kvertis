using Kvertis.Engine.Abstractions;
using Kvertis.Engine.Conversion.Audio;
using Kvertis.Engine.Formats;
using Kvertis.Engine.Platform;
using Kvertis.Engine.Tests.Ffmpeg;
using Shouldly;
using Xunit;

namespace Kvertis.Engine.Tests.Audio;

public class AudioConverterTests
{
    private static readonly IProgress<ConversionProgress> NoProgress = new SyncProgress<ConversionProgress>(_ => { });

    [Fact]
    public async Task ConvertsWavToMp3ThroughTempFileAndCommits()
    {
        using var fake = new FakeFfmpeg { OutputBytes = 1234 };
        var converter = new AudioConverter(fake.CreateToolset());
        var input = TestMedia.Audio(fake.CreateInputFile(".wav"));
        var output = fake.NewOutputPath(".mp3");
        var reports = new List<ConversionProgress>();

        var result = await converter.ConvertAsync(input, output, new ConversionSettings(FormatRegistry.Mp3), new SyncProgress<ConversionProgress>(reports.Add), CancellationToken.None);

        result.OutputPath.ShouldBe(output);
        result.OutputBytes.ShouldBe(1234);
        File.Exists(output).ShouldBeTrue();
        File.Exists(output + ".kvertis-tmp").ShouldBeFalse();

        var request = fake.ConversionRequests.ShouldHaveSingleItem();
        request.Arguments[^1].ShouldBe(output + ".kvertis-tmp");
        FfmpegArgumentsTests.ValueAfter(request.Arguments, "-f").ShouldBe("mp3");
        FfmpegArgumentsTests.ValueAfter(request.Arguments, "-c:a").ShouldBe("libmp3lame");
        request.Arguments.ShouldContain("-nostdin");
        request.Timeout.ShouldBe(TimeSpan.FromMinutes(30));
        request.CaptureStdout.ShouldBeFalse();

        reports.First().Phase.ShouldBe(ConversionPhase.Analyzing);
        reports.ShouldContain(r => r.Phase == ConversionPhase.Converting);
        reports.ShouldContain(r => r.Phase == ConversionPhase.Finalizing);
        reports.Last().ShouldBe(ConversionProgress.Complete);
    }

    [Fact]
    public async Task ProgressLinesBecomeFractions()
    {
        using var fake = new FakeFfmpeg { StderrLines = ["out_time_us=30000000", "progress=continue"] };
        var converter = new AudioConverter(fake.CreateToolset());
        var input = TestMedia.Audio(fake.CreateInputFile(".wav"));
        var reports = new List<ConversionProgress>();

        await converter.ConvertAsync(input, fake.NewOutputPath(".mp3"), new ConversionSettings(FormatRegistry.Mp3), new SyncProgress<ConversionProgress>(reports.Add), CancellationToken.None);

        // 30 s of 60 s: halfway through the converting band.
        reports.ShouldContain(r => r.Phase == ConversionPhase.Converting && Math.Abs(r.Fraction - 0.5) < 0.0001);
    }

    [Fact]
    public async Task M4aWithoutAacIsMissingSystemCodecBeforeFfmpegRuns()
    {
        using var fake = new FakeFfmpeg();
        var converter = new AudioConverter(fake.CreateToolset(NullSystemCodecCapabilities.Instance));
        var input = TestMedia.Audio(fake.CreateInputFile(".wav"));
        var output = fake.NewOutputPath(".m4a");

        var ex = await Should.ThrowAsync<ConversionException>(() =>
            converter.ConvertAsync(input, output, new ConversionSettings(FormatRegistry.M4a), NoProgress, CancellationToken.None));

        ex.Code.ShouldBe(ConversionErrorCode.MissingSystemCodec);
        fake.ConversionRequests.ShouldBeEmpty();
        File.Exists(output).ShouldBeFalse();
    }

    [Theory]
    [InlineData(1, "Invalid data found when processing input", false, ConversionErrorCode.CorruptFile)]
    [InlineData(1, "Conversion failed!", false, ConversionErrorCode.ToolFailed)]
    [InlineData(-1, "", true, ConversionErrorCode.Timeout)]
    public async Task FailedRunIsMappedAndLeavesNoFiles(int exitCode, string stderr, bool timedOut, ConversionErrorCode expected)
    {
        using var fake = new FakeFfmpeg { ConversionOutcome = new ProcessOutcome(exitCode, string.Empty, stderr, TimeSpan.Zero, timedOut) };
        var converter = new AudioConverter(fake.CreateToolset());
        var input = TestMedia.Audio(fake.CreateInputFile(".wav"));
        var output = fake.NewOutputPath(".flac");

        var ex = await Should.ThrowAsync<ConversionException>(() =>
            converter.ConvertAsync(input, output, new ConversionSettings(FormatRegistry.Flac), NoProgress, CancellationToken.None));

        ex.Code.ShouldBe(expected);
        if (expected == ConversionErrorCode.ToolFailed)
        {
            ex.Detail.ShouldBe(stderr);
        }
        File.Exists(output).ShouldBeFalse();
        File.Exists(output + ".kvertis-tmp").ShouldBeFalse();
    }

    [Fact]
    public async Task GplBuildIsRefused()
    {
        using var fake = new FakeFfmpeg { VersionOutput = "configuration: --enable-" + "gpl\n" };
        var converter = new AudioConverter(fake.CreateToolset());
        var input = TestMedia.Audio(fake.CreateInputFile(".wav"));

        var ex = await Should.ThrowAsync<ConversionException>(() =>
            converter.ConvertAsync(input, fake.NewOutputPath(".mp3"), new ConversionSettings(FormatRegistry.Mp3), NoProgress, CancellationToken.None));

        ex.Code.ShouldBe(ConversionErrorCode.ToolMissing);
        ex.Detail.ShouldBe("gpl build");
        fake.ConversionRequests.ShouldBeEmpty();
    }

    [Fact]
    public async Task VideoInputExtractsSoundWithVn()
    {
        using var fake = new FakeFfmpeg { ProbeJson = TestMedia.VideoJson(codec: "hevc") };
        var converter = new AudioConverter(fake.CreateToolset(NullSystemCodecCapabilities.Instance));
        var input = TestMedia.Video(fake.CreateInputFile(".mp4"));

        await converter.ConvertAsync(input, fake.NewOutputPath(".mp3"), new ConversionSettings(FormatRegistry.Mp3), NoProgress, CancellationToken.None);

        var request = fake.ConversionRequests.ShouldHaveSingleItem();
        request.Arguments.ShouldContain("-vn");
        request.Timeout.ShouldBe(TimeSpan.FromHours(6));
    }

    [Fact]
    public void SupportsAudioOutputsFromAudioAndVideo()
    {
        using var fake = new FakeFfmpeg();
        var converter = new AudioConverter(fake.CreateToolset());
        converter.Name.ShouldBe("audio");
        converter.Supports(TestMedia.Audio("/a.wav"), FormatRegistry.Mp3).ShouldBeTrue();
        converter.Supports(TestMedia.Video("/v.mp4"), FormatRegistry.Flac).ShouldBeTrue();
        converter.Supports(TestMedia.Audio("/a.wav"), FormatRegistry.Wma).ShouldBeFalse();
        converter.Supports(TestMedia.Audio("/a.wav"), FormatRegistry.Mp4).ShouldBeFalse();
    }

    [Fact]
    public async Task PreviewEncodesTenSecondsFromTheMiddleAndExtrapolates()
    {
        using var fake = new FakeFfmpeg { OutputBytes = 5000, ProbeJson = TestMedia.AudioJson(120) };
        var converter = new AudioConverter(fake.CreateToolset());
        var input = TestMedia.Audio(fake.CreateInputFile(".wav"), TimeSpan.FromSeconds(120));

        var preview = await converter.PreviewAsync(input, new ConversionSettings(FormatRegistry.Mp3), CancellationToken.None);

        preview.ShouldNotBeNull();
        fake.Track(preview.PreviewPath);
        preview.ExcerptDuration.ShouldBe(TimeSpan.FromSeconds(10));
        preview.EstimatedOutputBytes.ShouldBe(5000 * 12);
        var args = fake.ConversionRequests.ShouldHaveSingleItem().Arguments;
        FfmpegArgumentsTests.ValueAfter(args, "-ss").ShouldBe("55");
        FfmpegArgumentsTests.ValueAfter(args, "-t").ShouldBe("10");
        args[^1].ShouldBe(preview.PreviewPath);
    }

    [Fact]
    public void ExcerptWindowHandlesShortAndUnknownDurations()
    {
        AudioConverter.ExcerptWindow(TimeSpan.FromSeconds(4)).ShouldBe((null, TimeSpan.FromSeconds(4)));
        AudioConverter.ExcerptWindow(null).ShouldBe((null, TimeSpan.FromSeconds(10)));
        AudioConverter.ExcerptWindow(TimeSpan.FromSeconds(60)).ShouldBe((TimeSpan.FromSeconds(25), TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public async Task PreviewWithoutFfmpegIsNull()
    {
        using var fake = new FakeFfmpeg();
        NSubstitute.SubstituteExtensions.Returns(fake.Locator.IsAvailable, false);
        var converter = new AudioConverter(fake.CreateToolset());

        (await converter.PreviewAsync(TestMedia.Audio("/a.wav"), new ConversionSettings(FormatRegistry.Mp3), CancellationToken.None)).ShouldBeNull();
    }
}
