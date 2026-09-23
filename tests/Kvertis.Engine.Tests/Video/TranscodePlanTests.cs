using Kvertis.Engine.Abstractions;
using Kvertis.Engine.Conversion.Video;
using Kvertis.Engine.Formats;
using Kvertis.Engine.Platform;
using Kvertis.Engine.Tests.Ffmpeg;
using Shouldly;
using Xunit;

namespace Kvertis.Engine.Tests.Video;

public class TranscodePlanTests
{
    private const string Path = "/in/clip.mp4";

    private static Dictionary<string, string> Adv(params (string Key, string Value)[] pairs) => pairs.ToDictionary(p => p.Key, p => p.Value);

    [Fact]
    public void Mp4FromH264UsesSourceSizeQualityBitrateAndAacStep()
    {
        var plan = TranscodePlan.Create(TestMedia.Video(Path), TestMedia.VideoInfo("h264", audioCodec: "aac"), new ConversionSettings(FormatRegistry.Mp4, Quality: 80));

        // 1920 x 1080 x 30 fps x 0.10 bit/pixel = 6221 kbit/s, like the ffmpeg path.
        plan.ShouldBe(new TranscodePlan(TranscodeContainer.Mp4, 1920, 1080, 6221, 30, true, 192, null));
        plan.AudioOnly.ShouldBeFalse();
        plan.EstimateBytes(TimeSpan.FromSeconds(60)).ShouldBe((long)((6221 + 192) * 1000 / 8.0 * 60));
    }

    [Fact]
    public void MaxDimensionScalesDownKeepingAspectAndNeverUp()
    {
        var down = TranscodePlan.Create(TestMedia.Video(Path), TestMedia.VideoInfo("h264"),
            new ConversionSettings(FormatRegistry.Mp4, Advanced: Adv((ConversionSettings.AdvancedKeys.MaxDimension, "720"))));
        (down.Width, down.Height).ShouldBe((1280, 720));
        down.VideoBitrateKbps.ShouldBe(2765);

        var up = TranscodePlan.Create(TestMedia.Video(Path), TestMedia.VideoInfo("h264", width: 640, height: 480),
            new ConversionSettings(FormatRegistry.Mp4, Advanced: Adv((ConversionSettings.AdvancedKeys.MaxDimension, "1080"))));
        (up.Width, up.Height).ShouldBe((640, 480));

        var portrait = TranscodePlan.Create(TestMedia.Video(Path), TestMedia.VideoInfo("hevc", width: 1080, height: 1920),
            new ConversionSettings(FormatRegistry.Mp4, Preset: ConversionPreset.Messenger));
        (portrait.Width, portrait.Height).ShouldBe((404, 720));
    }

    [Fact]
    public void UnknownSourceSizeKeepsSize()
    {
        var input = TestMedia.Video(Path) with { Width = null, Height = null };
        var plan = TranscodePlan.Create(input, null, new ConversionSettings(FormatRegistry.Mp4));
        plan.Width.ShouldBeNull();
        plan.Height.ShouldBeNull();
        plan.FrameRate.ShouldBeNull();
        plan.IncludeAudio.ShouldBeTrue();
    }

    [Fact]
    public void TargetSizeIsSinglePassAverageBitrate()
    {
        // 60 s, 16 MiB -> 2236.96 kbit/s total, minus 192 audio = 2044 video.
        var plan = TranscodePlan.Create(TestMedia.Video(Path), TestMedia.VideoInfo("h264"), new ConversionSettings(FormatRegistry.Mp4, TargetSizeBytes: 16L * 1024 * 1024));
        plan.VideoBitrateKbps.ShouldBe(2044);
        plan.AudioBitrateKbps.ShouldBe(192);

        Should.Throw<ConversionException>(() => TranscodePlan.Create(TestMedia.Video(Path), TestMedia.VideoInfo("h264"),
                new ConversionSettings(FormatRegistry.Mp4, TargetSizeBytes: 1_000_000)))
            .Code.ShouldBe(ConversionErrorCode.TargetSizeUnreachable);
    }

    [Fact]
    public void FrameRateSettingAndSilentVideo()
    {
        var plan = TranscodePlan.Create(TestMedia.Video(Path), TestMedia.VideoInfo("h264", hasAudio: false),
            new ConversionSettings(FormatRegistry.Mp4, Advanced: Adv((ConversionSettings.AdvancedKeys.FrameRate, "25"))));
        plan.FrameRate.ShouldBe(25);
        plan.IncludeAudio.ShouldBeFalse();
        plan.AudioBitrateKbps.ShouldBeNull();
    }

    [Theory]
    [InlineData("m4a", 100, TranscodeContainer.M4a, 192)] // 320 requested, AAC encoder max is 192
    [InlineData("m4a", 0, TranscodeContainer.M4a, 96)]    // 64 requested, AAC encoder min is 96
    [InlineData("mp3", 80, TranscodeContainer.Mp3, 192)]
    [InlineData("mp3", 100, TranscodeContainer.Mp3, 320)]
    [InlineData("wav", 80, TranscodeContainer.Wav, null)]
    [InlineData("flac", 80, TranscodeContainer.Flac, null)]
    public void AudioOutputs(string output, int quality, TranscodeContainer container, int? kbps)
    {
        var plan = TranscodePlan.Create(TestMedia.Video(Path), TestMedia.VideoInfo("h264"), new ConversionSettings(new FormatId(output), Quality: quality));
        plan.Container.ShouldBe(container);
        plan.AudioOnly.ShouldBeTrue();
        plan.AudioBitrateKbps.ShouldBe(kbps);
        plan.VideoBitrateKbps.ShouldBeNull();
        plan.Width.ShouldBeNull();
    }

    [Fact]
    public void ExplicitAudioBitrateAndSampleRate()
    {
        var plan = TranscodePlan.Create(TestMedia.Audio("/in/a.m4a", format: FormatRegistry.M4a), TestMedia.AudioInfo(),
            new ConversionSettings(FormatRegistry.Mp3, Advanced: Adv(
                (ConversionSettings.AdvancedKeys.AudioBitrateKbps, "128"),
                (ConversionSettings.AdvancedKeys.SampleRateHz, "48000"))));
        plan.AudioBitrateKbps.ShouldBe(128);
        plan.SampleRateHz.ShouldBe(48000);
    }

    [Fact]
    public void InvalidJobsAreRefused()
    {
        Should.Throw<ConversionException>(() => TranscodePlan.Create(TestMedia.Video(Path), TestMedia.VideoInfo("h264"), new ConversionSettings(FormatRegistry.WebM)))
            .Code.ShouldBe(ConversionErrorCode.UnsupportedFormat);
        Should.Throw<ConversionException>(() => TranscodePlan.Create(TestMedia.Video(Path), TestMedia.VideoInfo("h264", hasAudio: false), new ConversionSettings(FormatRegistry.Mp3)))
            .Code.ShouldBe(ConversionErrorCode.UnsupportedFormat);
        Should.Throw<ConversionException>(() => TranscodePlan.Create(TestMedia.Audio("/in/a.m4a"), TestMedia.AudioInfo(), new ConversionSettings(FormatRegistry.Mp4)))
            .Code.ShouldBe(ConversionErrorCode.UnsupportedFormat);
    }

    [Theory]
    [InlineData(64, 96)]
    [InlineData(96, 96)]
    [InlineData(150, 128)]
    [InlineData(192, 192)]
    [InlineData(320, 192)]
    public void AacSteps(int requested, int expected) => TranscodePlan.AacStep(requested).ShouldBe(expected);

    [Fact]
    public void EstimatesForLosslessAudio()
    {
        var wav = new TranscodePlan(TranscodeContainer.Wav, null, null, null, null, true, null, null);
        wav.EstimateBytes(TimeSpan.FromSeconds(10)).ShouldBe(44100L * 4 * 10);
        wav.EstimateBytes(null).ShouldBe(0);
    }

    public static TheoryData<string, string, string?, string?, bool> Routing => new()
    {
        // input format, output, video codec (null = no probe data), audio codec, expected
        { "mp4", "mp4", "h264", "aac", true },
        { "mp4", "mp3", "h264", "aac", true },
        { "mp4", "m4a", "hevc", "aac", true },
        { "mp4", "webm", "h264", "aac", false },     // Phase 2
        { "mp4", "mkv", "h264", "aac", false },
        { "mp4", "mp4", "vp9", "opus", false },      // patent-free: ffmpeg path
        { "mkv", "mp4", "h264", "aac", true },
        { "mkv", "webm", "h264", "aac", false },
        { "mkv", "mp4", "vp9", "opus", false },
        { "mkv", "mp4", "vp9", "aac", true },        // one encumbered stream is enough
        { "mkv", "mp3", "vp9", "aac", true },
        { "mp3", "mp3", "mjpeg", "mp3", false },     // cover art is patent-free: ffmpeg path
        { "mp3", "wav", "mjpeg", "mp3", false },
        { "webm", "mp4", "vp9", "opus", false },
        { "flv", "mp4", "h264", "aac", false },      // Media Foundation cannot read FLV
        { "mpeg", "mp4", "mpeg2video", "mp2", false },
        { "avi", "mp4", "mpeg4", "mp3", true },
        { "wmv", "mp4", "wmv3", "wmav2", true },
        { "wmv", "wmv", "wmv3", "wmav2", false },    // never produce WMV
        { "mp4", "mp4", null, null, true },          // unknown codecs, MP4 family
        { "mov", "flac", null, null, true },
        { "mkv", "mp4", null, null, false },         // unknown codecs, not an encumbered family
        { "webm", "mp4", null, null, false },
    };

    [Theory]
    [MemberData(nameof(Routing))]
    public void SupportsRoutesByStreamCodec(string format, string output, string? videoCodec, string? audioCodec, bool expected)
    {
        var input = TestMedia.Video(Path, format: new FormatId(format));
        var media = videoCodec is null ? null : TestMedia.VideoInfo(videoCodec, audioCodec: audioCodec!);
        TranscodePlan.Supports(input, new FormatId(output), media, AllSystemCodecCapabilities.Instance).ShouldBe(expected);
    }

    [Fact]
    public void AudioInputsAndMissingEncoders()
    {
        var m4a = TestMedia.Audio("/in/a.m4a", format: FormatRegistry.M4a);
        var aac = TestMedia.AudioInfo() with { AudioCodec = "aac", StreamCodecs = ["aac"] };
        TranscodePlan.Supports(m4a, FormatRegistry.Mp3, aac).ShouldBeTrue();
        TranscodePlan.Supports(m4a, FormatRegistry.Mp4, aac).ShouldBeFalse();
        TranscodePlan.Supports(m4a, FormatRegistry.Ogg, aac).ShouldBeFalse();
        TranscodePlan.Supports(m4a, FormatRegistry.M4a, aac, NullSystemCodecCapabilities.Instance).ShouldBeFalse();
        TranscodePlan.Supports(m4a, FormatRegistry.Mp3, aac, NullSystemCodecCapabilities.Instance).ShouldBeTrue();

        var video = TestMedia.Video(Path);
        TranscodePlan.Supports(video, FormatRegistry.Mp4, TestMedia.VideoInfo("h264"), NullSystemCodecCapabilities.Instance).ShouldBeFalse();
        TranscodePlan.Supports(video, FormatRegistry.Mp3, TestMedia.VideoInfo("h264", hasAudio: false)).ShouldBeFalse();
        TranscodePlan.Supports(TestMedia.Audio("/in/a.wav"), FormatRegistry.Mp3, TestMedia.AudioInfo()).ShouldBeFalse();
    }

    [Fact]
    public void RoutingPairsAreComplementary()
    {
        // MKV with VP9 + AAC: Media Foundation only, ffmpeg refuses it.
        var mkv = TestMedia.Video(Path, format: FormatRegistry.Mkv);
        var vp9Aac = TestMedia.VideoInfo("vp9", audioCodec: "aac");
        TranscodePlan.Supports(mkv, FormatRegistry.Mp4, vp9Aac, AllSystemCodecCapabilities.Instance).ShouldBeTrue();
        Kvertis.Engine.Ffmpeg.FfmpegToolset.MayDecodeWithFfmpeg(mkv, vp9Aac).ShouldBeFalse();

        // MP3 with an embedded JPEG cover: ffmpeg only.
        var mp3 = TestMedia.Audio("/in/song.mp3", format: FormatRegistry.Mp3);
        var cover = TestMedia.AudioInfo() with { AudioCodec = "mp3", VideoCodec = "mjpeg", HasVideo = true, StreamCodecs = ["mp3", "mjpeg"] };
        TranscodePlan.Supports(mp3, FormatRegistry.Wav, cover, AllSystemCodecCapabilities.Instance).ShouldBeFalse();
        Kvertis.Engine.Ffmpeg.FfmpegToolset.MayDecodeWithFfmpeg(mp3, cover).ShouldBeTrue();
    }

    [Fact]
    public void AacFloorAboveTargetSizeBudgetIsUnreachable()
    {
        // 600 s, 5 MB -> 66.7 kbit/s: the audio step is 64, but the AAC encoder needs at least 96.
        var m4a = TestMedia.Audio("/in/a.m4a", TimeSpan.FromSeconds(600), FormatRegistry.M4a);
        var aac = TestMedia.AudioInfo(600) with { AudioCodec = "aac", StreamCodecs = ["aac"] };
        var ex = Should.Throw<ConversionException>(() => TranscodePlan.Create(m4a, aac, new ConversionSettings(FormatRegistry.M4a, TargetSizeBytes: 5_000_000)));
        ex.Code.ShouldBe(ConversionErrorCode.TargetSizeUnreachable);

        // 60 s, 1 MB -> 133 kbit/s: 128 fits.
        var fits = TranscodePlan.Create(TestMedia.Audio("/in/a.m4a", TimeSpan.FromSeconds(60), FormatRegistry.M4a), TestMedia.AudioInfo(60) with { AudioCodec = "aac", StreamCodecs = ["aac"] },
            new ConversionSettings(FormatRegistry.M4a, TargetSizeBytes: 1_000_000));
        fits.AudioBitrateKbps.ShouldBe(128);
    }

    [Fact]
    public void SystemTranscoderCannotStripMetadata() =>
        TranscodePlan.Create(TestMedia.Video(Path), TestMedia.VideoInfo("h264", audioCodec: "aac"), new ConversionSettings(FormatRegistry.Mp4)).CanStripMetadata.ShouldBeFalse();
}
