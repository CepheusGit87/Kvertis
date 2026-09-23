using Kvertis.Engine.Abstractions;
using Kvertis.Engine.Ffmpeg;
using Kvertis.Engine.Formats;
using Kvertis.Engine.Probing;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Kvertis.Engine.Tests.Ffmpeg;

public class FfprobeReaderTests
{
    [Fact]
    public void ParsesVideoAndAudioStreams()
    {
        var info = FfprobeReader.Parse(TestMedia.VideoJson());

        info.Duration.ShouldBe(TimeSpan.FromSeconds(60));
        info.Width.ShouldBe(1920);
        info.Height.ShouldBe(1080);
        info.VideoCodec.ShouldBe("h264");
        info.AudioCodec.ShouldBe("aac");
        info.HasVideo.ShouldBeTrue();
        info.HasAudio.ShouldBeTrue();
        info.AudioTrackCount.ShouldBe(1);
        info.IsVariableFrameRate.ShouldBeFalse();
        info.IsInterlaced.ShouldBeFalse();
        info.IsEncrypted.ShouldBeFalse();
        info.FrameRate.ShouldBe(30);
        info.VideoBitrateKbps.ShouldBe(8000);
        info.SampleRateHz.ShouldBe(48000);
    }

    [Fact]
    public void DetectsVariableFrameRate()
    {
        FfprobeReader.Parse(TestMedia.VideoJson(rRate: "60/1", avgRate: "2997/100")).IsVariableFrameRate.ShouldBeTrue();
        FfprobeReader.Parse(TestMedia.VideoJson(rRate: "30000/1001", avgRate: "30000/1001")).IsVariableFrameRate.ShouldBeFalse();
    }

    [Theory]
    [InlineData("tt", true)]
    [InlineData("bb", true)]
    [InlineData("progressive", false)]
    [InlineData("unknown", false)]
    public void DetectsInterlacing(string fieldOrder, bool interlaced) =>
        FfprobeReader.Parse(TestMedia.VideoJson(fieldOrder: fieldOrder)).IsInterlaced.ShouldBe(interlaced);

    [Fact]
    public void DetectsHevc() => FfprobeReader.Parse(TestMedia.VideoJson(codec: "hevc")).IsHevc.ShouldBeTrue();

    [Fact]
    public void MissingDurationIsNull() => FfprobeReader.Parse(TestMedia.VideoJson(duration: "\"N/A\"")).Duration.ShouldBeNull();

    [Fact]
    public void CoverArtIsNotAVideoStream()
    {
        const string json = """
            { "streams": [
                { "codec_type": "audio", "codec_name": "aac" },
                { "codec_type": "video", "codec_name": "mjpeg", "width": 600, "height": 600, "disposition": { "attached_pic": 1 } } ],
              "format": { "duration": "200.5" } }
            """;
        var info = FfprobeReader.Parse(json);
        info.HasVideo.ShouldBeFalse();
        info.Duration.ShouldBe(TimeSpan.FromSeconds(200.5));
    }

    [Fact]
    public void CountsAudioTracks()
    {
        const string json = """
            { "streams": [ { "codec_type": "audio", "codec_name": "aac" }, { "codec_type": "audio", "codec_name": "ac3" } ], "format": {} }
            """;
        FfprobeReader.Parse(json).AudioTrackCount.ShouldBe(2);
    }

    [Theory]
    [InlineData("""{ "streams": [ { "codec_type": "audio", "codec_tag_string": "enca" } ], "format": {} }""")]
    [InlineData("""{ "streams": [ { "codec_type": "video", "codec_tag_string": "drmi" } ], "format": {} }""")]
    [InlineData("""{ "streams": [ { "codec_type": "audio" } ], "format": { "tags": { "com.example.encryption": "cenc" } } }""")]
    [InlineData("""{ "streams": [ { "codec_type": "video", "side_data_list": [ { "side_data_type": "Encryption initialization data" } ] } ], "format": {} }""")]
    public void DetectsEncryptionHints(string json) => FfprobeReader.Parse(json).IsEncrypted.ShouldBeTrue();

    [Fact]
    public void InvalidJsonIsCorruptFile() =>
        Should.Throw<ConversionException>(() => FfprobeReader.Parse("not json")).Code.ShouldBe(ConversionErrorCode.CorruptFile);

    [Fact]
    public async Task ReadAsyncRunsFfprobeWithAnalysisTimeout()
    {
        using var fake = new FakeFfmpeg { ProbeJson = TestMedia.VideoJson() };
        var reader = new FfprobeReader(fake.Locator, fake.Runner);
        var path = fake.CreateInputFile(".mp4");

        await reader.ReadAsync(path, MediaKind.Video, CancellationToken.None);

        var request = fake.Requests.ShouldHaveSingleItem();
        request.ExecutablePath.ShouldBe(FakeFfmpeg.FfprobePath);
        request.Arguments.ShouldBe(["-v", "error", "-print_format", "json", "-show_format", "-show_streams", "-protocol_whitelist", "file", path]);
        request.Timeout.ShouldBe(TimeSpan.FromSeconds(30));
        request.CaptureStdout.ShouldBeTrue();
    }
}

public class MediaProberTests
{
    private static InputInfo Input(string path, FormatId format, MediaKind kind) =>
        new(path, format, kind, 4096, null, null, null, null, []);

    [Fact]
    public async Task EnrichesVideoAndAddsWarnings()
    {
        using var fake = new FakeFfmpeg { ProbeJson = TestMedia.VideoJson(rRate: "60/1", avgRate: "24/1", fieldOrder: "tt") };
        var cache = new MediaInfoCache();
        var prober = new MediaProber(new FfprobeReader(fake.Locator, fake.Runner), fake.Locator, cache);
        var path = fake.CreateInputFile(".mp4");

        var info = await prober.ProbeAsync(Input(path, FormatRegistry.Mp4, MediaKind.Video), CancellationToken.None);

        info.Duration.ShouldBe(TimeSpan.FromSeconds(60));
        info.Height.ShouldBe(1080);
        info.HasWarning(InputWarning.VariableFrameRate).ShouldBeTrue();
        info.HasWarning(InputWarning.Interlaced).ShouldBeTrue();
        cache.TryGet(path, out var cached).ShouldBeTrue();
        cached.VideoCodec.ShouldBe("h264");
    }

    [Fact]
    public async Task Mp4WithoutVideoBecomesM4a()
    {
        using var fake = new FakeFfmpeg { ProbeJson = """{ "streams": [ { "codec_type": "audio", "codec_name": "aac" } ], "format": { "duration": "12.0" } }""" };
        var prober = new MediaProber(new FfprobeReader(fake.Locator, fake.Runner), fake.Locator, new MediaInfoCache());
        var path = fake.CreateInputFile(".mp4");

        var info = await prober.ProbeAsync(Input(path, FormatRegistry.Mp4, MediaKind.Video), CancellationToken.None);

        info.Format.ShouldBe(FormatRegistry.M4a);
        info.Kind.ShouldBe(MediaKind.Audio);
    }

    [Fact]
    public async Task MissingDurationAddsWarning()
    {
        using var fake = new FakeFfmpeg { ProbeJson = """{ "streams": [ { "codec_type": "audio", "codec_name": "mp3" } ], "format": {} }""" };
        var prober = new MediaProber(new FfprobeReader(fake.Locator, fake.Runner), fake.Locator, new MediaInfoCache());
        var info = await prober.ProbeAsync(Input(fake.CreateInputFile(".mp3"), FormatRegistry.Mp3, MediaKind.Audio), CancellationToken.None);

        info.HasWarning(InputWarning.DurationUnknown).ShouldBeTrue();
    }

    [Fact]
    public async Task WithoutFfmpegInfoIsUnchanged()
    {
        using var fake = new FakeFfmpeg();
        fake.Locator.IsAvailable.Returns(false);
        var prober = new MediaProber(new FfprobeReader(fake.Locator, fake.Runner), fake.Locator, new MediaInfoCache());
        var input = Input(fake.CreateInputFile(".mp3"), FormatRegistry.Mp3, MediaKind.Audio);

        (await prober.ProbeAsync(input, CancellationToken.None)).ShouldBeSameAs(input);
        fake.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task FfprobeFailureLeavesInfoUnchanged()
    {
        using var fake = new FakeFfmpeg { ProbeJson = "garbage" };
        var prober = new MediaProber(new FfprobeReader(fake.Locator, fake.Runner), fake.Locator, new MediaInfoCache());
        var input = Input(fake.CreateInputFile(".mp3"), FormatRegistry.Mp3, MediaKind.Audio);

        (await prober.ProbeAsync(input, CancellationToken.None)).ShouldBeSameAs(input);
    }

    [Fact]
    public async Task EncryptedStreamIsProtectedFile()
    {
        using var fake = new FakeFfmpeg { ProbeJson = """{ "streams": [ { "codec_type": "audio", "codec_tag_string": "drms" } ], "format": {} }""" };
        var prober = new MediaProber(new FfprobeReader(fake.Locator, fake.Runner), fake.Locator, new MediaInfoCache());
        var input = Input(fake.CreateInputFile(".m4a"), FormatRegistry.M4a, MediaKind.Audio);

        (await Should.ThrowAsync<ConversionException>(() => prober.ProbeAsync(input, CancellationToken.None))).Code.ShouldBe(ConversionErrorCode.ProtectedFile);
    }

    [Fact]
    public void SupportsOnlyAudioAndVideo()
    {
        using var fake = new FakeFfmpeg();
        var prober = new MediaProber(new FfprobeReader(fake.Locator, fake.Runner), fake.Locator, new MediaInfoCache());
        prober.Supports(MediaKind.Audio).ShouldBeTrue();
        prober.Supports(MediaKind.Video).ShouldBeTrue();
        prober.Supports(MediaKind.Image).ShouldBeFalse();
    }
}

public class FfmpegProgressParserTests
{
    [Theory]
    [InlineData("out_time_us=1500000", 1.5)]
    [InlineData("out_time_ms=2000000", 2.0)]
    [InlineData("  out_time_us=0  ", 0)]
    public void ParsesOutTimeAsMicroseconds(string line, double seconds) =>
        FfmpegProgressParser.ParseOutTime(line).ShouldBe(TimeSpan.FromSeconds(seconds));

    [Theory]
    [InlineData("out_time_us=N/A")]
    [InlineData("frame=12")]
    [InlineData("out_time=00:00:01.500000")]
    [InlineData("progress=continue")]
    [InlineData("")]
    public void IgnoresOtherLines(string line) => FfmpegProgressParser.ParseOutTime(line).ShouldBeNull();

    [Fact]
    public void ReportsFractionOfDurationInConvertingPhase()
    {
        var reports = new List<ConversionProgress>();
        var parser = new FfmpegProgressParser(TimeSpan.FromSeconds(10), new SyncProgress<ConversionProgress>(reports.Add));

        parser.Report("frame=10");
        parser.Report("out_time_us=5000000");
        parser.Report("out_time_us=5000000");
        parser.Report("out_time_us=20000000");

        reports.Count.ShouldBe(2);
        reports[0].Phase.ShouldBe(ConversionPhase.Converting);
        reports[0].Fraction.ShouldBe(0.5, 0.0001);
        reports[1].Fraction.ShouldBe(FfmpegProgressParser.ConvertEnd, 0.0001);
    }

    [Fact]
    public void UnknownDurationReportsIndeterminateHalf()
    {
        var reports = new List<ConversionProgress>();
        var parser = new FfmpegProgressParser(null, new SyncProgress<ConversionProgress>(reports.Add));

        parser.Report("out_time_us=1000000");
        parser.Report("out_time_us=9000000");

        reports.ShouldHaveSingleItem().Fraction.ShouldBe(0.5);
    }

    [Fact]
    public void FractionIsClamped()
    {
        FfmpegProgressParser.FractionOf(TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(4)).ShouldBe(0.75);
        FfmpegProgressParser.FractionOf(TimeSpan.FromSeconds(9), TimeSpan.FromSeconds(4)).ShouldBe(1);
    }
}

/// <summary>Synchronous IProgress (Progress&lt;T&gt; posts to the thread pool, which makes tests racy).</summary>
internal sealed class SyncProgress<T>(Action<T> handler) : IProgress<T>
{
    public void Report(T value) => handler(value);
}
