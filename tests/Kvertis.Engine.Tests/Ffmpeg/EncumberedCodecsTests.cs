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

    [Theory]
    [InlineData("h264_qsv", true)]
    [InlineData("hevc_cuvid", true)]
    [InlineData("h264_amf", true)]
    [InlineData("vc1_cuvid", true)]
    [InlineData("mpeg4_mediacodec", true)]
    [InlineData("aac_fixed", true)]
    [InlineData("wmv3image", true)]
    [InlineData("vc1image", true)]
    [InlineData("flv", true)]
    [InlineData("vp9_qsv", false)]
    [InlineData("av1_cuvid", false)]
    [InlineData("libdav1d", false)]
    [InlineData("mp3", false)]
    [InlineData("_qsv", false)]
    [InlineData("", false)]
    public void ForbiddenDecodersIncludeHardwareVariants(string decoder, bool expected) =>
        EncumberedCodecs.IsForbiddenDecoder(decoder).ShouldBe(expected);

    /// <summary>
    /// One source for the forbidden decoders: every decoder the release build check refuses is known to
    /// <see cref="EncumberedCodecs"/> (and therefore to <see cref="FfmpegCompliance"/>).
    /// </summary>
    [Fact]
    public void BuildCheckDecodersAreSubsetOfEncumberedCodecs()
    {
        var script = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "tools", "ffmpeg", "check-build.sh"));
        var match = System.Text.RegularExpressions.Regex.Match(script, @"for dec in (?<list>[^;]+); do");
        match.Success.ShouldBeTrue("decoder loop not found in check-build.sh");
        var names = match.Groups["list"].Value
            .Split(new[] { ' ', '\\', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries);

        names.Length.ShouldBeGreaterThan(30);
        names.Where(n => !EncumberedCodecs.IsForbiddenDecoder(n)).ShouldBeEmpty();
        foreach (var added in new[] { "vv" + "c", "fl" + "v", "h26" + "3p", "h26" + "3i", "ml" + "p", "wm" + "v3image", "vc" + "1image" })
        {
            names.ShouldContain(added);
        }

        // The Windows variant of the check lists the same decoders.
        var ps1 = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "tools", "ffmpeg", "check-build.ps1"));
        var psMatch = System.Text.RegularExpressions.Regex.Match(ps1, @"\$dec in @\((?<list>[^)]+)\)");
        psMatch.Success.ShouldBeTrue("decoder loop not found in check-build.ps1");
        var psNames = psMatch.Groups["list"].Value
            .Split(new[] { ',', ' ', '\'', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        psNames.ShouldBe(names, ignoreOrder: true);
    }

    [Fact]
    public void ComplianceUsesTheCanonicalDecoderList()
    {
        var decoders = FakeFfmpeg.DecodersOutput(["vp9", "h26" + "3p", "fl" + "v", "he" + "vc_amf"]);
        var report = FfmpegCompliance.Parse(FakeFfmpeg.LgplVersion, FakeFfmpeg.EncodersOutput(FakeFfmpeg.DefaultEncoders), decoders, FakeFfmpeg.AllowlistProtocols);

        report.ForbiddenDecodersFound.ShouldBe(["h26" + "3p", "fl" + "v", "he" + "vc_amf"], ignoreOrder: true);
    }

    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Kvertis.sln")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? throw new InvalidOperationException("Kvertis.sln not found above " + AppContext.BaseDirectory);
    }
}
