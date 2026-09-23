using Kvertis.Engine.Abstractions;
using Kvertis.Engine.Ffmpeg;
using Kvertis.Engine.Formats;
using Kvertis.Engine.Platform;
using Kvertis.Engine.Probing;
using NSubstitute;

namespace Kvertis.Engine.Tests.Ffmpeg;

/// <summary>
/// NSubstitute-backed ffmpeg/ffprobe: records every ProcessRequest and answers with canned output.
/// Conversion runs write a few bytes to the output path (last argument) unless configured otherwise.
/// </summary>
internal sealed class FakeFfmpeg : IDisposable
{
    public const string FfmpegPath = "/fake/ffmpeg";
    public const string FfprobePath = "/fake/ffprobe";

    public const string LgplVersion =
        "ffmpeg version 7.1 Copyright (c) 2000-2024 the FFmpeg developers\n" +
        "built with gcc 14.2.0\n" +
        "configuration: --disable-gpl --disable-nonfree --disable-version3 --enable-mediafoundation --enable-libmp3lame --enable-libopus --enable-libvorbis --enable-libvpx\n" +
        "libavutil      59. 39.100 / 59. 39.100\n";

    public const string EncodersHeader =
        "Encoders:\n" +
        " V..... = Video\n" +
        " A..... = Audio\n" +
        " S..... = Subtitle\n" +
        " .F.... = Frame-level multithreading\n" +
        " ------\n";

    public static readonly string[] DefaultEncoders =
    [
        "h264_mf", "hevc_mf", "aac_mf", "mp3_mf", "libmp3lame", "libopus", "libvorbis", "flac", "pcm_s16le", "pcm_s16be", "libvpx-vp9", "png",
    ];

    /// <summary>Decoders of the Kvertis allowlist build (patent-free only).</summary>
    public static readonly string[] AllowlistDecoders =
    [
        "vp8", "vp9", "libdav1d", "theora", "mpeg1video", "mpeg2video", "mjpeg", "png", "libopus", "libvorbis", "flac", "mp3", "ac3", "alac", "pcm_s16le",
    ];

    /// <summary><c>ffmpeg -protocols</c> of the allowlist build: only file and pipe.</summary>
    public const string AllowlistProtocols = "Supported file protocols:\nInput:\n  file\n  pipe\nOutput:\n  file\n  pipe\n";

    private readonly List<string> _tempFiles = [];

    public FakeFfmpeg()
    {
        Runner = Substitute.For<IProcessRunner>();
        Locator = Substitute.For<IFfmpegLocator>();
        Locator.FfmpegPath.Returns(FfmpegPath);
        Locator.FfprobePath.Returns(FfprobePath);
        Locator.IsAvailable.Returns(true);

        Runner.RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<IProgress<string>?>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(Answer(call.Arg<ProcessRequest>(), call.Arg<IProgress<string>?>())));
    }

    public IProcessRunner Runner { get; }
    public IFfmpegLocator Locator { get; }
    public List<ProcessRequest> Requests { get; } = [];

    public string VersionOutput { get; set; } = LgplVersion;
    public IReadOnlyList<string> Encoders { get; set; } = DefaultEncoders;
    public IReadOnlyList<string> Decoders { get; set; } = AllowlistDecoders;
    public string ProtocolsOutput { get; set; } = AllowlistProtocols;
    public string ProbeJson { get; set; } = TestMedia.AudioJson(60);
    public ProcessOutcome? ConversionOutcome { get; set; }

    /// <summary>When set, every ffprobe run answers with this outcome instead of <see cref="ProbeJson"/>.</summary>
    public ProcessOutcome? ProbeOutcome { get; set; }

    /// <summary>A failed ffprobe run (unreadable header).</summary>
    public static readonly ProcessOutcome FailedProbe = new(1, string.Empty, "Invalid data found when processing input", TimeSpan.FromMilliseconds(5), false);
    public IReadOnlyList<string> StderrLines { get; set; } = [];
    public int OutputBytes { get; set; } = 1000;

    public IReadOnlyList<ProcessRequest> ConversionRequests =>
        Requests.Where(r => r.ExecutablePath == FfmpegPath && r.Arguments.Contains("-i")).ToList();

    public FfmpegToolset CreateToolset(ISystemCodecCapabilities? codecs = null, MediaInfoCache? cache = null) =>
        new(
            Locator,
            Runner,
            codecs ?? AllSystemCodecCapabilities.Instance,
            new FfmpegFeatureProbe(Locator, Runner),
            new FfmpegCompliance(Locator, Runner),
            new FfprobeReader(Locator, Runner),
            cache ?? new MediaInfoCache(),
            new FormatRegistry());

    public static string EncodersOutput(IEnumerable<string> encoders) =>
        EncodersHeader + string.Concat(encoders.Select(e => $" A....D {e,-20} Some encoder\n"));

    public static string DecodersOutput(IEnumerable<string> decoders) =>
        "Decoders:\n V..... = Video\n A..... = Audio\n S..... = Subtitle\n ------\n" + string.Concat(decoders.Select(d => $" VFS..D {d,-20} Some decoder\n"));

    /// <summary>A real, non-empty file in the temp folder, deleted on Dispose.</summary>
    public string CreateInputFile(string extension, int bytes = 4096)
    {
        var path = Path.Combine(Path.GetTempPath(), "kvertis-test-" + Guid.NewGuid().ToString("N") + extension);
        File.WriteAllBytes(path, new byte[bytes]);
        _tempFiles.Add(path);
        return path;
    }

    public string NewOutputPath(string extension)
    {
        var path = Path.Combine(Path.GetTempPath(), "kvertis-out-" + Guid.NewGuid().ToString("N") + extension);
        _tempFiles.Add(path);
        _tempFiles.Add(path + ".kvertis-tmp");
        return path;
    }

    public void Track(string path) => _tempFiles.Add(path);

    public void Dispose()
    {
        foreach (var file in _tempFiles)
        {
            try
            {
                File.Delete(file);
            }
            catch (IOException)
            {
            }
        }
    }

    private ProcessOutcome Answer(ProcessRequest request, IProgress<string>? stderr)
    {
        lock (Requests)
        {
            Requests.Add(request);
        }
        var args = request.Arguments;
        if (request.ExecutablePath == FfprobePath)
        {
            return ProbeOutcome ?? Ok(ProbeJson);
        }
        if (args.Contains("-version"))
        {
            return Ok(VersionOutput);
        }
        if (args.Contains("-encoders"))
        {
            return Ok(EncodersOutput(Encoders));
        }
        if (args.Contains("-decoders"))
        {
            return Ok(DecodersOutput(Decoders));
        }
        if (args.Contains("-protocols"))
        {
            return Ok(ProtocolsOutput);
        }

        foreach (var line in StderrLines)
        {
            stderr?.Report(line);
        }
        if (ConversionOutcome is { } outcome)
        {
            return outcome;
        }
        var output = args[^1];
        _tempFiles.Add(output);
        File.WriteAllBytes(output, new byte[OutputBytes]);
        return Ok(string.Empty);
    }

    private static ProcessOutcome Ok(string stdout) => new(0, stdout, string.Empty, TimeSpan.FromMilliseconds(5), false);
}

internal static class TestMedia
{
    public static InputInfo Audio(string path, TimeSpan? duration = null, FormatId? format = null) =>
        new(path, format ?? FormatRegistry.Wav, MediaKind.Audio, 4096, duration ?? TimeSpan.FromSeconds(60), null, null, null, []);

    public static InputInfo Video(string path, int width = 1920, int height = 1080, TimeSpan? duration = null, FormatId? format = null) =>
        new(path, format ?? FormatRegistry.Mp4, MediaKind.Video, 50_000_000, duration ?? TimeSpan.FromSeconds(60), width, height, null, []);

    public static MediaInfo AudioInfo(double seconds = 60) =>
        new(TimeSpan.FromSeconds(seconds), null, null, null, "pcm_s16le", false, true, 1, false, false, false, SampleRateHz: 44100);

    /// <summary>Probe data of a video; patent-free VP9/Opus by default (the ffmpeg path).</summary>
    public static MediaInfo VideoInfo(string codec = "vp9", int width = 1920, int height = 1080, double seconds = 60, bool vfr = false, bool hasAudio = true, string audioCodec = "opus") =>
        new(TimeSpan.FromSeconds(seconds), width, height, codec, hasAudio ? audioCodec : null, true, hasAudio, hasAudio ? 1 : 0, vfr, false, false, 30)
        {
            StreamCodecs = hasAudio ? [codec, audioCodec] : [codec],
        };

    public static string AudioJson(double seconds) => $$"""
        {
          "streams": [
            { "index": 0, "codec_name": "pcm_s16le", "codec_type": "audio", "sample_rate": "44100", "channels": 2, "bit_rate": "1411200" }
          ],
          "format": { "format_name": "wav", "duration": "{{seconds.ToString(System.Globalization.CultureInfo.InvariantCulture)}}", "size": "4096" }
        }
        """;

    /// <summary>ffprobe JSON of a video; patent-free VP9/Opus by default (the ffmpeg path).</summary>
    public static string VideoJson(string codec = "vp9", string audioCodec = "opus", int height = 1080, string rRate = "30/1", string avgRate = "30/1", string fieldOrder = "progressive", string duration = "\"60.000000\"") => $$"""
        {
          "streams": [
            { "index": 0, "codec_name": "{{codec}}", "codec_type": "video", "width": 1920, "height": {{height}},
              "r_frame_rate": "{{rRate}}", "avg_frame_rate": "{{avgRate}}", "field_order": "{{fieldOrder}}", "bit_rate": "8000000",
              "disposition": { "default": 1, "attached_pic": 0 } },
            { "index": 1, "codec_name": "{{audioCodec}}", "codec_type": "audio", "sample_rate": "48000", "channels": 2, "bit_rate": "128000" }
          ],
          "format": { "format_name": "mov,mp4,m4a,3gp,3g2,mj2", "duration": {{duration}}, "size": "50000000" }
        }
        """;
}
