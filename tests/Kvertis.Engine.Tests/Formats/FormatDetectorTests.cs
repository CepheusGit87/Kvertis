using Kvertis.Engine.Abstractions;
using Kvertis.Engine.Formats;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Kvertis.Engine.Tests.Formats;

public sealed class FormatDetectorTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "kvertis-tests", Guid.NewGuid().ToString("N"));
    private readonly FormatDetector _detector = new(new FormatRegistry(), []);

    public FormatDetectorTests()
    {
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // Best effort cleanup.
        }
    }

    private string Write(string fileName, byte[] content)
    {
        var path = Path.Combine(_directory, fileName);
        File.WriteAllBytes(path, content);
        return path;
    }

    public static TheoryData<string, string, MediaKind> Files => new()
    {
        { "image.png", "png", MediaKind.Image },
        { "image.jpg", "jpg", MediaKind.Image },
        { "image.gif", "gif", MediaKind.Image },
        { "image.webp", "webp", MediaKind.Image },
        { "photo.heic", "heic", MediaKind.Image },
        { "vector.svg", "svg", MediaKind.Image },
        { "sound.wav", "wav", MediaKind.Audio },
        { "sound.flac", "flac", MediaKind.Audio },
        { "sound.ogg", "ogg", MediaKind.Audio },
        { "sound.opus", "opus", MediaKind.Audio },
        { "sound.mp3", "mp3", MediaKind.Audio },
        { "movie.mkv", "mkv", MediaKind.Video },
        { "movie.webm", "webm", MediaKind.Video },
        { "movie.mp4", "mp4", MediaKind.Video },
        { "paper.pdf", "pdf", MediaKind.Document },
        { "letter.docx", "docx", MediaKind.Document },
        { "notes.txt", "txt", MediaKind.Document },
    };

    private static byte[] ContentFor(string format) => format switch
    {
        "png" => FormatSamples.Png,
        "jpg" => FormatSamples.Jpg,
        "gif" => FormatSamples.Gif,
        "webp" => FormatSamples.WebP,
        "heic" => FormatSamples.Heic,
        "svg" => FormatSamples.Svg,
        "wav" => FormatSamples.Wav,
        "flac" => FormatSamples.Flac,
        "ogg" => FormatSamples.OggVorbis,
        "opus" => FormatSamples.Opus,
        "mp3" => FormatSamples.Mp3Id3,
        "mkv" => FormatSamples.Mkv,
        "webm" => FormatSamples.WebM,
        "mp4" => FormatSamples.Mp4,
        "pdf" => FormatSamples.Pdf,
        "docx" => FormatSamples.Docx,
        "txt" => FormatSamples.Text,
        _ => throw new ArgumentOutOfRangeException(nameof(format)),
    };

    [Theory]
    [MemberData(nameof(Files))]
    public async Task Detects_format_kind_and_size(string fileName, string format, MediaKind kind)
    {
        var content = ContentFor(format);
        var path = Write(fileName, content);

        var info = await _detector.DetectAsync(path, CancellationToken.None);

        info.Format.ShouldBe(new FormatId(format));
        info.Kind.ShouldBe(kind);
        info.SizeBytes.ShouldBe(content.Length);
        info.Path.ShouldBe(path);
        info.Warnings.ShouldBeEmpty();
    }

    [Fact]
    public async Task Wrong_extension_raises_extension_mismatch()
    {
        var path = Write("actually-png.jpg", FormatSamples.Png);

        var info = await _detector.DetectAsync(path, CancellationToken.None);

        info.Format.ShouldBe(FormatRegistry.Png);
        info.HasWarning(InputWarning.ExtensionMismatch).ShouldBeTrue();
    }

    [Fact]
    public async Task Same_container_family_does_not_raise_mismatch()
    {
        var path = Write("clip.webm", FormatSamples.Mkv);

        var info = await _detector.DetectAsync(path, CancellationToken.None);

        info.Format.ShouldBe(FormatRegistry.Mkv);
        info.HasWarning(InputWarning.ExtensionMismatch).ShouldBeFalse();
    }

    [Fact]
    public async Task Empty_file_is_corrupt()
    {
        var path = Write("empty.png", []);

        var ex = await Should.ThrowAsync<ConversionException>(() => _detector.DetectAsync(path, CancellationToken.None));

        ex.Code.ShouldBe(ConversionErrorCode.CorruptFile);
    }

    [Fact]
    public async Task Missing_file_is_not_readable()
    {
        var ex = await Should.ThrowAsync<ConversionException>(
            () => _detector.DetectAsync(Path.Combine(_directory, "missing.png"), CancellationToken.None));

        ex.Code.ShouldBe(ConversionErrorCode.InputNotReadable);
    }

    [Fact]
    public async Task Unknown_binary_is_unsupported()
    {
        var path = Write("blob.dat", Enumerable.Range(0, 256).Select(i => (byte)i).ToArray());

        var ex = await Should.ThrowAsync<ConversionException>(() => _detector.DetectAsync(path, CancellationToken.None));

        ex.Code.ShouldBe(ConversionErrorCode.UnsupportedFormat);
    }

    [Fact]
    public async Task Legacy_office_is_recognized_but_unsupported()
    {
        var path = Write("old.doc", FormatSamples.LegacyOffice);

        var ex = await Should.ThrowAsync<ConversionException>(() => _detector.DetectAsync(path, CancellationToken.None));

        ex.Code.ShouldBe(ConversionErrorCode.UnsupportedFormat);
    }

    [Fact]
    public async Task Plain_zip_is_unsupported()
    {
        var path = Write("archive.zip", FormatSamples.PlainZip);

        var ex = await Should.ThrowAsync<ConversionException>(() => _detector.DetectAsync(path, CancellationToken.None));

        ex.Code.ShouldBe(ConversionErrorCode.UnsupportedFormat);
    }

    [Fact]
    public async Task Only_probers_for_the_detected_kind_run()
    {
        var path = Write("sound.wav", FormatSamples.Wav);
        var audio = Substitute.For<IMediaProber>();
        audio.Supports(MediaKind.Audio).Returns(true);
        audio.ProbeAsync(Arg.Any<InputInfo>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<InputInfo>() with { Duration = TimeSpan.FromSeconds(3) });
        var image = Substitute.For<IMediaProber>();
        image.Supports(MediaKind.Image).Returns(true);
        var detector = new FormatDetector(new FormatRegistry(), [image, audio]);

        var info = await detector.DetectAsync(path, CancellationToken.None);

        info.Duration.ShouldBe(TimeSpan.FromSeconds(3));
        await image.DidNotReceiveWithAnyArgs().ProbeAsync(default!, default);
    }

    [Fact]
    public async Task Utf16_text_with_bom_is_text_not_mp3()
    {
        var path = Write("notes-utf16.txt", System.Text.Encoding.Unicode.GetPreamble()
            .Concat(System.Text.Encoding.Unicode.GetBytes("Hallo Welt, das ist Text.\n")).ToArray());

        var info = await _detector.DetectAsync(path, CancellationToken.None);

        info.Format.ShouldBe(FormatRegistry.Txt);
    }

    [Fact]
    public async Task Text_starting_with_G_is_not_a_transport_stream()
    {
        var path = Write("brief", System.Text.Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("Guten Tag, liebe Leute. ", 40))));

        var info = await _detector.DetectAsync(path, CancellationToken.None);

        info.Format.ShouldBe(FormatRegistry.Txt);
    }

    [Fact]
    public async Task Transport_stream_needs_three_sync_bytes_in_the_sample()
    {
        var path = Write("clip.ts", FormatSamples.TransportStream(packets: 4));

        var info = await _detector.DetectAsync(path, CancellationToken.None);

        info.Format.ShouldBe(FormatRegistry.Ts);
    }

    [Fact]
    public async Task Avif_is_recognized_but_blocked()
    {
        var path = Write("photo.avif", FormatSamples.Avif);

        var ex = await Should.ThrowAsync<ConversionException>(() => _detector.DetectAsync(path, CancellationToken.None));

        ex.Code.ShouldBe(ConversionErrorCode.UnsupportedFormat);
        ex.Detail.ShouldBe(FormatRegistry.AvifBlockedDetail);
    }

    [Fact]
    public async Task File_above_the_soft_limit_gets_large_file_warning()
    {
        var path = Write("big.png", FormatSamples.Png);
        var detector = new FormatDetector(new FormatRegistry(), [], new Kvertis.Engine.Validation.InputValidator(new Kvertis.Engine.Validation.InputLimits(MaxImageBytes: 10)));

        var info = await detector.DetectAsync(path, CancellationToken.None);

        info.HasWarning(InputWarning.LargeFile).ShouldBeTrue();
    }

    [Fact]
    public async Task File_below_the_soft_limit_has_no_large_file_warning()
    {
        var path = Write("small.png", FormatSamples.Png);

        var info = await _detector.DetectAsync(path, CancellationToken.None);

        info.HasWarning(InputWarning.LargeFile).ShouldBeFalse();
    }
}
