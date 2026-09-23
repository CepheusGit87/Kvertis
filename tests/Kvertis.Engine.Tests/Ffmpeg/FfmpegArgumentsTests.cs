using Kvertis.Engine.Abstractions;
using Kvertis.Engine.Ffmpeg;
using Kvertis.Engine.Formats;
using Kvertis.Engine.Platform;
using Shouldly;
using Xunit;

namespace Kvertis.Engine.Tests.Ffmpeg;

public class FfmpegArgumentsTests
{
    private static readonly string InputPath = Path.Combine(Path.GetTempPath(), "in.wav");
    private static readonly string VideoPath = Path.Combine(Path.GetTempPath(), "in.mp4");
    private const string Temp = "/out/file.mp3.kvertis-tmp";

    private static readonly FormatRegistry Registry = new();
    private static readonly FfmpegFeatures AllFeatures = new(FakeFfmpeg.DefaultEncoders);

    private static IReadOnlyList<string> BuildAudio(ConversionSettings settings, MediaInfo? media = null, InputInfo? input = null, FfmpegFeatures? features = null, ISystemCodecCapabilities? codecs = null) =>
        FfmpegArguments.Build(input ?? TestMedia.Audio(InputPath), media ?? TestMedia.AudioInfo(), Temp, settings, Registry,
            codecs ?? AllSystemCodecCapabilities.Instance, features ?? AllFeatures);

    private static IReadOnlyList<string> BuildVideo(ConversionSettings settings, MediaInfo? media = null, InputInfo? input = null, FfmpegFeatures? features = null, ISystemCodecCapabilities? codecs = null) =>
        FfmpegArguments.Build(input ?? TestMedia.Video(VideoPath), media ?? TestMedia.VideoInfo(), "/out/v.kvertis-tmp", settings, Registry,
            codecs ?? AllSystemCodecCapabilities.Instance, features ?? AllFeatures);

    private static Dictionary<string, string> Adv(params (string Key, string Value)[] pairs) => pairs.ToDictionary(p => p.Key, p => p.Value);

    [Fact]
    public void WavToMp3ProducesExactArgumentList()
    {
        var args = BuildAudio(new ConversionSettings(FormatRegistry.Mp3, Quality: 80));

        args.ShouldBe(
        [
            "-nostdin", "-hide_banner", "-y", "-progress", "pipe:2", "-nostats",
            "-protocol_whitelist", "file", "-i", Path.GetFullPath(InputPath),
            "-map", "0:a:0", "-vn", "-c:a", "libmp3lame", "-b:a", "192k",
            "-map_metadata", "-1",
            "-f", "mp3", Temp,
        ]);
    }

    [Theory]
    [InlineData("mp3", "libmp3lame", "mp3")]
    [InlineData("ogg", "libvorbis", "ogg")]
    [InlineData("opus", "libopus", "opus")]
    [InlineData("flac", "flac", "flac")]
    [InlineData("wav", "pcm_s16le", "wav")]
    [InlineData("aiff", "pcm_s16be", "aiff")]
    [InlineData("m4a", "aac_mf", "ipod")]
    public void AudioEncoderAndMuxerPerFormat(string format, string encoder, string muxer)
    {
        var args = BuildAudio(new ConversionSettings(new FormatId(format)));

        ValueAfter(args, "-c:a").ShouldBe(encoder);
        ValueAfter(args, "-f").ShouldBe(muxer);
        args[^1].ShouldBe(Temp);
    }

    [Fact]
    public void Mp3FallsBackToSystemEncoderWhenLameMissing()
    {
        var features = new FfmpegFeatures(FakeFfmpeg.DefaultEncoders.Where(e => e != "libmp3lame"));
        ValueAfter(BuildAudio(new ConversionSettings(FormatRegistry.Mp3), features: features), "-c:a").ShouldBe("mp3_mf");
    }

    [Fact]
    public void Mp3WithoutAnyEncoderIsMissingSystemCodec()
    {
        var features = new FfmpegFeatures(["flac"]);
        Should.Throw<ConversionException>(() => BuildAudio(new ConversionSettings(FormatRegistry.Mp3), features: features))
            .Code.ShouldBe(ConversionErrorCode.MissingSystemCodec);
    }

    [Fact]
    public void LosslessOutputsHaveNoBitrate()
    {
        BuildAudio(new ConversionSettings(FormatRegistry.Flac)).ShouldNotContain("-b:a");
        BuildAudio(new ConversionSettings(FormatRegistry.Wav, TargetSizeBytes: 10)).ShouldNotContain("-b:a");
    }

    [Fact]
    public void KeepMetadataOmitsMapMetadata()
    {
        BuildAudio(new ConversionSettings(FormatRegistry.Mp3, Metadata: MetadataPolicy.Keep)).ShouldNotContain("-map_metadata");
    }

    [Fact]
    public void SampleRateAndExplicitBitrate()
    {
        var args = BuildAudio(new ConversionSettings(FormatRegistry.Opus, Advanced: Adv(
            (ConversionSettings.AdvancedKeys.SampleRateHz, "48000"),
            (ConversionSettings.AdvancedKeys.AudioBitrateKbps, "96"))));

        ValueAfter(args, "-ar").ShouldBe("48000");
        ValueAfter(args, "-b:a").ShouldBe("96k");
    }

    [Theory]
    // 60 s: 1 MB -> 139.8 kbit/s -> 128; 2.5 MB -> 349 -> 320; 600 kB -> 80 -> 64; 1.44 MB -> exactly 192.
    [InlineData(1_048_576, 128)]
    [InlineData(2_621_440, 320)]
    [InlineData(600_000, 64)]
    [InlineData(1_440_000, 192)]
    public void TargetSizeFloorsToBitrateStep(long bytes, int expectedKbps)
    {
        FfmpegArguments.AudioBitrateForTargetSize(bytes, TimeSpan.FromSeconds(60)).ShouldBe(expectedKbps);
        ValueAfter(BuildAudio(new ConversionSettings(FormatRegistry.Mp3, TargetSizeBytes: bytes)), "-b:a").ShouldBe($"{expectedKbps}k");
    }

    [Fact]
    public void TargetSizeBelow64KbpsIsUnreachable()
    {
        // 60 s at 400 kB = 53 kbit/s.
        Should.Throw<ConversionException>(() => BuildAudio(new ConversionSettings(FormatRegistry.Mp3, TargetSizeBytes: 400_000)))
            .Code.ShouldBe(ConversionErrorCode.TargetSizeUnreachable);
    }

    [Fact]
    public void TargetSizeWithUnknownDurationIsUnreachable()
    {
        var input = TestMedia.Audio(InputPath) with { Duration = null };
        var media = TestMedia.AudioInfo() with { Duration = null };
        Should.Throw<ConversionException>(() => BuildAudio(new ConversionSettings(FormatRegistry.Mp3, TargetSizeBytes: 1_000_000), media, input))
            .Code.ShouldBe(ConversionErrorCode.TargetSizeUnreachable);
    }

    [Fact]
    public void M4aWithoutAacCapabilityIsMissingSystemCodec()
    {
        Should.Throw<ConversionException>(() => BuildAudio(new ConversionSettings(FormatRegistry.M4a), codecs: NullSystemCodecCapabilities.Instance))
            .Code.ShouldBe(ConversionErrorCode.MissingSystemCodec);
    }

    [Fact]
    public void AudioExtractionFromVideoUsesVnAndNoVideoDecode()
    {
        var args = BuildAudio(new ConversionSettings(FormatRegistry.Mp3), TestMedia.VideoInfo(), TestMedia.Video(VideoPath),
            codecs: NullSystemCodecCapabilities.Instance);

        args.ShouldContain("-vn");
        args.ShouldNotContain("-hwaccel");
        ValueAfter(args, "-map").ShouldBe("0:a:0");
    }

    [Fact]
    public void AudioExtractionFromVideoWithoutSoundIsUnsupported()
    {
        Should.Throw<ConversionException>(() => BuildAudio(new ConversionSettings(FormatRegistry.Mp3), TestMedia.VideoInfo(hasAudio: false), TestMedia.Video(VideoPath)))
            .Code.ShouldBe(ConversionErrorCode.UnsupportedFormat);
    }

    [Fact]
    public void Mp4FromSilentVideoDoesNotNeedAac()
    {
        var codecs = new TestCodecs { CanEncodeH264 = true, CanEncodeAac = false };
        var args = BuildVideo(new ConversionSettings(FormatRegistry.Mp4), TestMedia.VideoInfo(hasAudio: false), codecs: codecs);

        ValueAfter(args, "-c:v").ShouldBe("h264_mf");
        args.ShouldNotContain("-c:a");
    }

    [Fact]
    public void Mp4WithAudioStillNeedsAac()
    {
        var codecs = new TestCodecs { CanEncodeH264 = true, CanEncodeAac = false };
        Should.Throw<ConversionException>(() => BuildVideo(new ConversionSettings(FormatRegistry.Mp4), TestMedia.VideoInfo(hasAudio: true), codecs: codecs))
            .Code.ShouldBe(ConversionErrorCode.MissingSystemCodec);
    }

    [Fact]
    public void Mp4UsesOnlyMediaFoundationEncoders()
    {
        var args = BuildVideo(new ConversionSettings(FormatRegistry.Mp4));

        ValueAfter(args, "-c:v").ShouldBe("h264_mf");
        ValueAfter(args, "-c:a").ShouldBe("aac_mf");
        ValueAfter(args, "-f").ShouldBe("mp4");
        args.ShouldContain("+faststart");
        args.ShouldContain("-map_metadata");
        args[^1].ShouldBe("/out/v.kvertis-tmp");
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void Mp4WithoutSystemEncoderIsMissingSystemCodec(bool h264, bool aac)
    {
        var codecs = new TestCodecs { CanEncodeH264 = h264, CanEncodeAac = aac };
        Should.Throw<ConversionException>(() => BuildVideo(new ConversionSettings(FormatRegistry.Mp4), codecs: codecs))
            .Code.ShouldBe(ConversionErrorCode.MissingSystemCodec);
    }

    [Theory]
    [InlineData("webm", "webm")]
    [InlineData("mkv", "matroska")]
    public void WebmAndMkvUseVp9AndOpus(string format, string muxer)
    {
        var args = BuildVideo(new ConversionSettings(new FormatId(format), Quality: 80), codecs: NullSystemCodecCapabilities.Instance);

        ValueAfter(args, "-c:v").ShouldBe("libvpx-vp9");
        ValueAfter(args, "-c:a").ShouldBe("libopus");
        ValueAfter(args, "-crf").ShouldBe("26");
        ValueAfter(args, "-f").ShouldBe(muxer);
    }

    [Fact]
    public void ArchiveMkvCopiesStreams()
    {
        var args = BuildVideo(new ConversionSettings(FormatRegistry.Mkv, Preset: ConversionPreset.Archive, Metadata: MetadataPolicy.Keep),
            TestMedia.VideoInfo("av1"), codecs: NullSystemCodecCapabilities.Instance);

        args.ShouldBe(
        [
            "-nostdin", "-hide_banner", "-y", "-progress", "pipe:2", "-nostats",
            "-protocol_whitelist", "file", "-i", Path.GetFullPath(VideoPath),
            "-map", "0:v?", "-map", "0:a?", "-c", "copy",
            "-f", "matroska", "/out/v.kvertis-tmp",
        ]);
    }

    [Fact]
    public void VariableFrameRateForcesCfr()
    {
        var args = BuildVideo(new ConversionSettings(FormatRegistry.Mp4), TestMedia.VideoInfo(vfr: true));
        ValueAfter(args, "-fps_mode").ShouldBe("cfr");

        var fromWarning = BuildVideo(new ConversionSettings(FormatRegistry.Mp4), input: TestMedia.Video(VideoPath).WithWarning(InputWarning.VariableFrameRate));
        fromWarning.ShouldContain("-fps_mode");

        BuildVideo(new ConversionSettings(FormatRegistry.Mp4)).ShouldNotContain("-fps_mode");
    }

    [Fact]
    public void ScaleDownOnlyWhenSourceIsTaller()
    {
        var down = BuildVideo(new ConversionSettings(FormatRegistry.Mp4, Advanced: Adv((ConversionSettings.AdvancedKeys.MaxDimension, "720"))));
        ValueAfter(down, "-vf").ShouldBe("scale=-2:720");

        var up = BuildVideo(new ConversionSettings(FormatRegistry.Mp4, Advanced: Adv((ConversionSettings.AdvancedKeys.MaxDimension, "2160"))));
        up.ShouldNotContain("-vf");

        var equal = BuildVideo(new ConversionSettings(FormatRegistry.Mp4, Advanced: Adv((ConversionSettings.AdvancedKeys.MaxDimension, "1080"))));
        equal.ShouldNotContain("-vf");

        var keep = BuildVideo(new ConversionSettings(FormatRegistry.Mp4, Advanced: Adv((ConversionSettings.AdvancedKeys.MaxDimension, "0"))));
        keep.ShouldNotContain("-vf");
    }

    [Fact]
    public void PresetHeightNeverUpscales()
    {
        var small = TestMedia.VideoInfo(width: 640, height: 480);
        BuildVideo(new ConversionSettings(FormatRegistry.Mp4, Preset: ConversionPreset.Messenger), small).ShouldNotContain("-vf");
        ValueAfter(BuildVideo(new ConversionSettings(FormatRegistry.Mp4, Preset: ConversionPreset.Messenger)), "-vf").ShouldBe("scale=-2:720");
    }

    [Fact]
    public void DeinterlaceAndScaleCombineAndFrameRateIsSet()
    {
        var args = BuildVideo(new ConversionSettings(FormatRegistry.Mp4, Advanced: Adv(
            (ConversionSettings.AdvancedKeys.Deinterlace, "true"),
            (ConversionSettings.AdvancedKeys.MaxDimension, "720"),
            (ConversionSettings.AdvancedKeys.FrameRate, "25"))));

        ValueAfter(args, "-vf").ShouldBe("yadif,scale=-2:720");
        ValueAfter(args, "-r").ShouldBe("25");
    }

    public static TheoryData<string, string, string?> EncumberedJobs => new()
    {
        // video codec, output, preset (null = none)
        { "h264", "mp4", null },
        { "hevc", "webm", null },
        { "h264", "mkv", "Archive" },   // no stream copy either
        { "mpeg4", "mp3", null },       // no sound extraction either
        { "vp9", "mp3", "aac-audio" },  // patent-free video, encumbered audio
    };

    [Theory]
    [MemberData(nameof(EncumberedJobs))]
    public void EncumberedMediaIsNeverHandedToFfmpeg(string videoCodec, string output, string? variant)
    {
        var media = TestMedia.VideoInfo(videoCodec, audioCodec: variant == "aac-audio" ? "aac" : "opus");
        var settings = new ConversionSettings(new FormatId(output), Preset: variant == "Archive" ? ConversionPreset.Archive : ConversionPreset.None);

        var ex = Should.Throw<ConversionException>(() => FfmpegArguments.Build(TestMedia.Video(VideoPath), media, "/out/v.kvertis-tmp", settings, Registry,
            AllSystemCodecCapabilities.Instance, AllFeatures));

        ex.Code.ShouldBe(ConversionErrorCode.UnsupportedFormat);
        ex.Detail.ShouldBe("requires system decoding");
    }

    [Fact]
    public void EncumberedFrameExtractionIsRefused()
    {
        Should.Throw<ConversionException>(() => FfmpegArguments.BuildFrameExtraction(TestMedia.Video(VideoPath), TestMedia.VideoInfo("hevc"), "/tmp/p.png",
                TimeSpan.Zero, new ConversionSettings(FormatRegistry.Mp4)))
            .Detail.ShouldBe("requires system decoding");
    }

    [Fact]
    public void UnknownCodecsInMp4FamilyAreRefusedButWebmIsAccepted()
    {
        Should.Throw<ConversionException>(() => FfmpegArguments.Build(TestMedia.Video(VideoPath), null, "/out/v.kvertis-tmp",
                new ConversionSettings(FormatRegistry.WebM), Registry, AllSystemCodecCapabilities.Instance, AllFeatures))
            .Code.ShouldBe(ConversionErrorCode.UnsupportedFormat);

        var webm = TestMedia.Video(VideoPath, format: FormatRegistry.WebM);
        FfmpegArguments.Build(webm, null, "/out/v.kvertis-tmp", new ConversionSettings(FormatRegistry.Mkv), Registry, AllSystemCodecCapabilities.Instance, AllFeatures)
            .ShouldContain("libvpx-vp9");
    }

    [Fact]
    public void PatentFreeWebmToMp4UsesMediaFoundationEncodersWithoutHardwareDecode()
    {
        var webm = TestMedia.Video(VideoPath, format: FormatRegistry.WebM);
        var args = BuildVideo(new ConversionSettings(FormatRegistry.Mp4), TestMedia.VideoInfo("vp9", audioCodec: "opus"), webm);

        ValueAfter(args, "-c:v").ShouldBe("h264_mf");
        ValueAfter(args, "-c:a").ShouldBe("aac_mf");
        args.ShouldNotContain("-hwaccel");
    }

    [Fact]
    public void VideoTargetSizeUsesSinglePassBitrateWithCaps()
    {
        // 60 s, 16 MB -> 2236.96 kbit/s total, minus 192 audio (quality 80, capped) = 2044 video.
        var args = BuildVideo(new ConversionSettings(FormatRegistry.Mp4, Quality: 80, TargetSizeBytes: 16L * 1024 * 1024));

        ValueAfter(args, "-b:v").ShouldBe("2044k");
        ValueAfter(args, "-maxrate").ShouldBe("2044k");
        ValueAfter(args, "-bufsize").ShouldBe("4088k");
        ValueAfter(args, "-b:a").ShouldBe("192k");
    }

    [Fact]
    public void VideoTargetSizeTooSmallIsUnreachable()
    {
        Should.Throw<ConversionException>(() => BuildVideo(new ConversionSettings(FormatRegistry.Mp4, TargetSizeBytes: 1_000_000)))
            .Code.ShouldBe(ConversionErrorCode.TargetSizeUnreachable);
    }

    [Fact]
    public void ExcerptOptionsPlaceSeekBeforeInputAndLengthAfter()
    {
        var args = FfmpegArguments.Build(TestMedia.Audio(InputPath), TestMedia.AudioInfo(), Temp, new ConversionSettings(FormatRegistry.Mp3), Registry,
            AllSystemCodecCapabilities.Instance, AllFeatures,
            new FfmpegJobOptions { ExcerptStart = TimeSpan.FromSeconds(25), ExcerptDuration = TimeSpan.FromSeconds(10), ReportProgress = false });

        args.ShouldNotContain("-progress");
        var ss = IndexOf(args, "-ss");
        var input = IndexOf(args, "-i");
        var t = IndexOf(args, "-t");
        ss.ShouldBeLessThan(input);
        t.ShouldBeGreaterThan(input);
        args[ss + 1].ShouldBe("25");
        args[t + 1].ShouldBe("10");
    }

    [Fact]
    public void FrameExtractionWritesOnePng()
    {
        var args = FfmpegArguments.BuildFrameExtraction(TestMedia.Video(VideoPath), TestMedia.VideoInfo(), "/tmp/p.png", TimeSpan.FromSeconds(15),
            new ConversionSettings(FormatRegistry.Mp4));

        args.ShouldBe(
        [
            "-nostdin", "-hide_banner", "-y", "-ss", "15", "-protocol_whitelist", "file", "-i", Path.GetFullPath(VideoPath),
            "-map", "0:v:0", "-frames:v", "1", "-map_metadata", "-1", "-c:v", "png", "-f", "image2", "/tmp/p.png",
        ]);
    }

    [Theory]
    [InlineData(0, 64)]
    [InlineData(50, 160)]
    [InlineData(80, 192)]
    [InlineData(100, 320)]
    public void QualityMapsToBitrateSteps(int quality, int kbps) => FfmpegArguments.AudioBitrateForQuality(quality).ShouldBe(kbps);

    [Theory]
    [InlineData(63.9, null)]
    [InlineData(64, 64)]
    [InlineData(95.9, 64)]
    [InlineData(191, 160)]
    [InlineData(1000, 320)]
    public void FloorToStep(double kbps, int? expected) => FfmpegArguments.FloorToStep(kbps).ShouldBe(expected);

    [Fact]
    public void EstimateForWavIsPcmSize()
    {
        var bytes = FfmpegArguments.EstimateOutputBytes(TestMedia.Audio(InputPath), TestMedia.AudioInfo(10), new ConversionSettings(FormatRegistry.Wav), Registry);
        bytes.ShouldBe(44100L * 4 * 10);
    }

    internal static int IndexOf(IReadOnlyList<string> args, string value)
    {
        for (var i = 0; i < args.Count; i++)
        {
            if (args[i] == value)
            {
                return i;
            }
        }
        return -1;
    }

    internal static string? ValueAfter(IReadOnlyList<string> args, string option)
    {
        var i = IndexOf(args, option);
        return i >= 0 && i + 1 < args.Count ? args[i + 1] : null;
    }
}

internal sealed class TestCodecs : ISystemCodecCapabilities
{
    public bool CanEncodeH264 { get; init; }
    public bool CanEncodeHevc { get; init; }
    public bool CanEncodeAac { get; init; }
    public bool CanDecodeHevc { get; init; }
    public bool CanDecodeHeif { get; init; }
}
