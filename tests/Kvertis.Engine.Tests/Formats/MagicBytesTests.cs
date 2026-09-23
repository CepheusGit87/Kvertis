using Kvertis.Engine.Abstractions;
using Kvertis.Engine.Formats;
using Shouldly;
using Xunit;

namespace Kvertis.Engine.Tests.Formats;

public sealed class MagicBytesTests
{
    public static TheoryData<string, byte[], string> Signatures => new()
    {
        { "png", FormatSamples.Png, "x.bin" },
        { "jpg", FormatSamples.Jpg, "x.bin" },
        { "gif", FormatSamples.Gif, "x.bin" },
        { "webp", FormatSamples.WebP, "x.bin" },
        { "wav", FormatSamples.Wav, "x.bin" },
        { "flac", FormatSamples.Flac, "x.bin" },
        { "ogg", FormatSamples.OggVorbis, "x.bin" },
        { "opus", FormatSamples.Opus, "x.ogg" },
        { "mp3", FormatSamples.Mp3Id3, "x.bin" },
        { "mp3", FormatSamples.Mp3FrameSync, "x.bin" },
        { "mkv", FormatSamples.Mkv, "x.bin" },
        { "webm", FormatSamples.WebM, "x.mkv" },
        { "mp4", FormatSamples.Mp4, "x.bin" },
        { "mov", FormatSamples.Mov, "x.bin" },
        { "heic", FormatSamples.Heic, "x.jpg" },
        { "avif", FormatSamples.Avif, "x.bin" },
        { "pdf", FormatSamples.Pdf, "x.bin" },
        { "svg", FormatSamples.Svg, "x.bin" },
        { "doc", FormatSamples.LegacyOffice, "x.docx" },
    };

    [Theory]
    [MemberData(nameof(Signatures))]
    public void Detects_format_from_content_not_extension(string expected, byte[] content, string path)
    {
        var header = content.AsSpan(0, Math.Min(content.Length, MagicBytes.HeaderLength));

        MagicBytes.Detect(header, path).ShouldBe(new FormatId(expected));
    }

    [Fact]
    public void Tiff_container_is_raw_only_with_raw_extension()
    {
        byte[] tiff = FormatSamples.Pad([0x49, 0x49, 0x2A, 0x00, 0x08, 0x00, 0x00, 0x00]);

        MagicBytes.Detect(tiff, "scan.tif").ShouldBe(FormatRegistry.Tiff);
        MagicBytes.Detect(tiff, "photo.nef").ShouldBe(FormatRegistry.Raw);
    }

    [Fact]
    public void Mp4_with_generic_brand_and_m4a_extension_is_audio()
    {
        MagicBytes.Detect(FormatSamples.Mp4, "song.m4a").ShouldBe(FormatRegistry.M4a);
    }

    [Fact]
    public void Zip_needs_archive_inspection()
    {
        MagicBytes.Detect(FormatSamples.Docx.AsSpan(0, MagicBytes.HeaderLength), "x.docx").ShouldBeNull();
    }

    [Theory]
    [InlineData("docx")]
    [InlineData("xlsx")]
    [InlineData("pptx")]
    public void Office_zip_is_classified_by_content_types_and_folder(string expected)
    {
        var content = expected switch
        {
            "docx" => FormatSamples.Docx,
            "xlsx" => FormatSamples.Xlsx,
            _ => FormatSamples.Pptx,
        };
        using var stream = new MemoryStream(content);

        MagicBytes.DetectZipBased(stream).ShouldBe(new FormatId(expected));
    }

    [Fact]
    public void Plain_zip_is_not_an_office_document()
    {
        using var stream = new MemoryStream(FormatSamples.PlainZip);

        MagicBytes.DetectZipBased(stream).ShouldBeNull();
    }

    [Fact]
    public void Broken_zip_is_not_an_office_document()
    {
        using var stream = new MemoryStream(FormatSamples.Pad(FormatSamples.Ascii("PK\u0003\u0004garbage")));

        MagicBytes.DetectZipBased(stream).ShouldBeNull();
    }

    [Theory]
    [InlineData("notes.txt", "txt")]
    [InlineData("readme.md", "md")]
    [InlineData("table.csv", "csv")]
    [InlineData("noext", "txt")]
    public void Text_is_detected_by_printable_content(string path, string expected)
    {
        MagicBytes.DetectText(FormatSamples.Text, path).ShouldBe(new FormatId(expected));
    }

    [Fact]
    public void Html_without_extension_is_recognized()
    {
        MagicBytes.DetectText(FormatSamples.Ascii("<!DOCTYPE html><html><body>x</body></html>"), "page").ShouldBe(FormatRegistry.Html);
    }

    [Fact]
    public void Binary_garbage_is_not_text()
    {
        var garbage = Enumerable.Range(0, 256).Select(i => (byte)i).ToArray();

        MagicBytes.Detect(garbage.AsSpan(0, MagicBytes.HeaderLength), "x.bin").ShouldBeNull();
        MagicBytes.DetectText(garbage, "x.bin").ShouldBeNull();
    }

    [Fact]
    public void Too_short_input_is_unknown()
    {
        MagicBytes.Detect([0x89, 0x50], "x.png").ShouldBeNull();
        MagicBytes.Detect([], "x.png").ShouldBeNull();
        MagicBytes.DetectText([], "x.txt").ShouldBeNull();
    }
}
