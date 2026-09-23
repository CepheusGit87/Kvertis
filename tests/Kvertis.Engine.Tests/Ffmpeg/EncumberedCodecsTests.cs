using Kvertis.Engine.Abstractions;
using Kvertis.Engine.Ffmpeg;
using Kvertis.Engine.Formats;
using Shouldly;
using Xunit;

namespace Kvertis.Engine.Tests.Ffmpeg;

public class EncumberedCodecsTests
{
    [Theory]
    [InlineData("h264")]
    [InlineData("hevc")]
    [InlineData("HEVC")]
    [InlineData("aac")]
    [InlineData("mpeg4")]
    [InlineData("msmpeg4v1")]
    [InlineData("msmpeg4v2")]
    [InlineData("msmpeg4v3")]
    [InlineData("wmv1")]
    [InlineData("wmv2")]
    [InlineData("wmv3")]
    [InlineData("vc1")]
    [InlineData("wmav1")]
    [InlineData("wmav2")]
    [InlineData("wmapro")]
    [InlineData("wmalossless")]
    [InlineData("h263")]
    [InlineData("prores")]
    [InlineData("dnxhd")]
    [InlineData("eac3")]
    [InlineData("dts")]
    [InlineData("dca")]
    [InlineData("truehd")]
    [InlineData("amrnb")]
    [InlineData("amrwb")]
    [InlineData("amr_nb")]
    [InlineData("amr_wb")]
    public void EncumberedCodecsAreMembers(string codec) => EncumberedCodecs.Contains(codec).ShouldBeTrue();

    [Theory]
    [InlineData("vp8")]
    [InlineData("vp9")]
    [InlineData("av1")]
    [InlineData("theora")]
    [InlineData("mpeg1video")]
    [InlineData("mpeg2video")]
    [InlineData("mjpeg")]
    [InlineData("png")]
    [InlineData("opus")]
    [InlineData("vorbis")]
    [InlineData("flac")]
    [InlineData("mp3")]
    [InlineData("mp2")]
    [InlineData("ac3")]
    [InlineData("alac")]
    [InlineData("pcm_s16le")]
    [InlineData("")]
    [InlineData(null)]
    public void PatentFreeCodecsAreNotMembers(string? codec) => EncumberedCodecs.Contains(codec).ShouldBeFalse();

    [Fact]
    public void AnyEncumberedStreamCounts()
    {
        EncumberedCodecs.ContainsAny(["vp9", "opus"]).ShouldBeFalse();
        EncumberedCodecs.ContainsAny(["vp9", "opus", "eac3"]).ShouldBeTrue();
    }

    [Fact]
    public void MediaInfoChecksEveryStream()
    {
        var media = TestMedia.VideoInfo("mpeg2video", audioCodec: "ac3");
        media.RequiresSystemDecoding.ShouldBeFalse();
        (media with { StreamCodecs = ["mpeg2video", "ac3", "dts"] }).RequiresSystemDecoding.ShouldBeTrue();
        TestMedia.VideoInfo("h264").RequiresSystemDecoding.ShouldBeTrue();
        TestMedia.VideoInfo("vp9", audioCodec: "aac").RequiresSystemDecoding.ShouldBeTrue();
    }

    [Theory]
    [InlineData("mp4", true)]
    [InlineData("mov", true)]
    [InlineData("m4a", true)]
    [InlineData("wmv", true)]
    [InlineData("wma", true)]
    [InlineData("avi", true)]
    [InlineData("3gp", true)]
    [InlineData("mpeg", true)]
    [InlineData("ts", true)]
    [InlineData("mkv", false)]
    [InlineData("webm", false)]
    [InlineData("ogg", false)]
    [InlineData("mp3", false)]
    public void SystemDecodingFamilies(string format, bool expected) =>
        EncumberedCodecs.IsSystemDecodingFamily(new FormatId(format)).ShouldBe(expected);

    [Fact]
    public void HevcNameMatchesProbeOutput() => EncumberedCodecs.Hevc.ShouldBe("hevc");

    [Fact]
    public void RegistryKnowsAllFamilies() =>
        new FormatRegistry().All.Count(d => EncumberedCodecs.IsSystemDecodingFamily(d.Id)).ShouldBe(9);
}
