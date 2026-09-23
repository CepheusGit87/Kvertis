using Kvertis.Engine.Abstractions;
using Kvertis.Engine.Conversion;
using Kvertis.Engine.Formats;
using Kvertis.Engine.Platform;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Kvertis.Engine.Tests.Formats;

/// <summary>Audio/video rows of the format matrix after ADR-015 (docs/05-formate.md).</summary>
public class FormatMatrixTests
{
    private static readonly FormatRegistry Registry = new();

    private static InputInfo Input(FormatId format, MediaKind kind) => new("/in/x." + format.Id, format, kind, 1000, TimeSpan.FromSeconds(10), null, null, null, []);

    [Theory]
    [InlineData("mp4")]
    [InlineData("mov")]
    [InlineData("avi")]
    [InlineData("wmv")]
    [InlineData("3gp")]
    [InlineData("mpeg")]
    [InlineData("ts")]
    public void EncumberedVideoFamiliesOfferOnlySystemOutputs(string format)
    {
        var suggestion = Registry.Suggest(Input(new FormatId(format), MediaKind.Video), AllSystemCodecCapabilities.Instance);

        suggestion.ShouldNotBeNull();
        suggestion.Options.Select(o => o.Id).OrderBy(o => o).ShouldBe(["flac", "m4a", "mp3", "mp4", "wav"]);
        // Video: MP4 is the default for every encumbered family, including MP4 itself ("smaller MP4" is the common wish).
        suggestion.Default.ShouldBe(FormatRegistry.Mp4);
    }

    [Theory]
    [InlineData("m4a")]
    [InlineData("wma")]
    public void EncumberedAudioFamiliesOfferMp3WavFlacM4a(string format) =>
        Registry.Suggest(Input(new FormatId(format), MediaKind.Audio), AllSystemCodecCapabilities.Instance)!
            .Options.Select(o => o.Id).ShouldBe(["mp3", "wav", "flac", "m4a"]);

    [Fact]
    public void MkvKeepsTheUnionAndWebmStaysOpen()
    {
        Registry.Suggest(Input(FormatRegistry.Mkv, MediaKind.Video))!.Options.Select(o => o.Id).ShouldBe(["mp4", "webm", "mkv", "mp3", "wav", "flac", "m4a"]);
        Registry.Suggest(Input(FormatRegistry.WebM, MediaKind.Video))!.Options.ShouldContain(FormatRegistry.Mkv);
    }

    [Fact]
    public void FlvHasNoOutputs() => Registry.Suggest(Input(FormatRegistry.Flv, MediaKind.Video)).ShouldBeNull();

    [Fact]
    public void Mp4AndM4aStillNeedSystemEncoders()
    {
        Registry.IsProducible(FormatRegistry.Mp4, NullSystemCodecCapabilities.Instance).ShouldBeFalse();
        Registry.IsProducible(FormatRegistry.M4a, NullSystemCodecCapabilities.Instance).ShouldBeFalse();
        Registry.Suggest(Input(FormatRegistry.Mov, MediaKind.Video), NullSystemCodecCapabilities.Instance)!
            .Options.Select(o => o.Id).ShouldBe(["mp3", "wav", "flac"]);
    }

    [Fact]
    public void ResolverCanConvertAsksTheConverters()
    {
        var input = Input(FormatRegistry.Mkv, MediaKind.Video);
        var converter = Substitute.For<IConverter>();
        converter.Supports(input, FormatRegistry.Mp4).Returns(true);
        IConverterResolver resolver = new ConverterResolver([converter]);

        resolver.CanConvert(input, FormatRegistry.Mp4).ShouldBeTrue();
        resolver.CanConvert(input, FormatRegistry.WebM).ShouldBeFalse();
    }
}
