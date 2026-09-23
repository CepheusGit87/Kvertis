using System.Globalization;
using Kvertis.Engine.Abstractions;
using Kvertis.Engine.Formats;

namespace Kvertis.Engine.Ffmpeg;

/// <summary>Extra knobs for a run that is not a plain full conversion (preview excerpts).</summary>
public sealed record FfmpegJobOptions
{
    /// <summary>Input seek position (placed before <c>-i</c> for a fast seek).</summary>
    public TimeSpan? ExcerptStart { get; init; }

    /// <summary>Output duration limit (<c>-t</c>).</summary>
    public TimeSpan? ExcerptDuration { get; init; }

    /// <summary>Whether to emit <c>-progress pipe:2 -nostats</c>. Previews do not need it.</summary>
    public bool ReportProgress { get; init; } = true;
}

/// <summary>
/// Pure command builder for ffmpeg. Returns an argument LIST (never a shell string) for
/// <see cref="ProcessRequest"/>. Encoder policy (docs/02 §3, ADR-003):
/// H.264 only via h264_mf, HEVC only via hevc_mf, AAC only via aac_mf. Everything else uses free codecs.
/// Decoder policy (ADR-015): ffmpeg only ever decodes patent-free streams; media that needs system decoding
/// is refused with UnsupportedFormat "requires system decoding" (also for stream copy and sound extraction).
/// Throws <see cref="ConversionException"/> (MissingSystemCodec, TargetSizeUnreachable, UnsupportedFormat)
/// before any process is started.
/// </summary>
public static class FfmpegArguments
{
    public const string H264Encoder = "h264_mf";
    public const string HevcEncoder = "hevc_mf";
    public const string AacEncoder = "aac_mf";
    public const string Mp3Encoder = "libmp3lame";
    public const string Mp3SystemEncoder = "mp3_mf";
    public const string OpusEncoder = "libopus";
    public const string VorbisEncoder = "libvorbis";
    public const string FlacEncoder = "flac";
    public const string WavEncoder = "pcm_s16le";
    public const string AiffEncoder = "pcm_s16be";
    public const string Vp9Encoder = "libvpx-vp9";

    /// <summary>Audio bitrate steps in kbit/s (docs/05-formate.md §Audio).</summary>
    public static readonly IReadOnlyList<int> AudioBitrateSteps = [64, 96, 128, 160, 192, 256, 320];

    /// <summary>Below this, a video bitrate derived from a target size is not watchable.</summary>
    public const int MinVideoKbps = 100;

    private const int DefaultVideoAudioKbpsCap = 192;

    public static bool IsAudioOutput(FormatId output, FormatRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        return registry.KindOf(output) == MediaKind.Audio;
    }

    public static bool IsVideoOutput(FormatId output) =>
        output == FormatRegistry.Mp4 || output == FormatRegistry.Mkv || output == FormatRegistry.WebM;

    /// <summary>ffmpeg muxer name for an output format. Needed because temp files end in ".kvertis-tmp".</summary>
    public static string MuxerFor(FormatId output)
    {
        return output.Id switch
        {
            "mp3" => "mp3",
            "wav" => "wav",
            "flac" => "flac",
            "ogg" => "ogg",
            "opus" => "opus",
            "m4a" => "ipod",
            "aiff" => "aiff",
            "mp4" => "mp4",
            "mkv" => "matroska",
            "webm" => "webm",
            "png" => "image2",
            _ => throw new ConversionException(ConversionErrorCode.UnsupportedFormat, step: "ffmpeg-args", detail: $"no muxer for '{output}'"),
        };
    }

    /// <summary>True when the Archive preset asks for an MKV stream copy (no re-encode).</summary>
    public static bool IsStreamCopy(ConversionSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return settings.Preset == ConversionPreset.Archive && settings.Output == FormatRegistry.Mkv;
    }

    /// <summary>Builds the full argument list for converting <paramref name="input"/> into <paramref name="tempOutputPath"/>.</summary>
    public static IReadOnlyList<string> Build(
        InputInfo input,
        MediaInfo? media,
        string tempOutputPath,
        ConversionSettings settings,
        FormatRegistry registry,
        ISystemCodecCapabilities codecs,
        FfmpegFeatures features,
        FfmpegJobOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentException.ThrowIfNullOrWhiteSpace(tempOutputPath);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(codecs);
        ArgumentNullException.ThrowIfNull(features);
        options ??= new FfmpegJobOptions();
        FfmpegToolset.EnsureNoSystemDecoding(input, media);

        var output = settings.Output;
        var audioOut = IsAudioOutput(output, registry);
        if (!audioOut && !IsVideoOutput(output))
        {
            throw new ConversionException(ConversionErrorCode.UnsupportedFormat, input.Path, "ffmpeg-args", $"'{output}' is not an audio/video output");
        }
        if (!audioOut && input.Kind != MediaKind.Video)
        {
            throw new ConversionException(ConversionErrorCode.UnsupportedFormat, input.Path, "ffmpeg-args", "video output needs a video input");
        }
        if (media is { HasAudio: false } && audioOut)
        {
            throw new ConversionException(ConversionErrorCode.UnsupportedFormat, input.Path, "ffmpeg-args", "input has no audio stream");
        }

        // Stream copy only for patent-free streams: encumbered inputs were refused above.
        var copy = !audioOut && IsStreamCopy(settings);

        var args = new List<string>(48);
        AddCommonHead(args, options.ReportProgress);
        if (options.ExcerptStart is { } start)
        {
            args.AddRange(["-ss", Seconds(start)]);
        }
        AddInput(args, input.Path);
        if (options.ExcerptDuration is { } length)
        {
            args.AddRange(["-t", Seconds(length)]);
        }

        if (audioOut)
        {
            AddAudioOnly(args, input, media, settings, codecs, features);
        }
        else if (copy)
        {
            // Archive preset: keep every video/audio track bit-exact. Filters and rates do not apply.
            args.AddRange(["-map", "0:v?", "-map", "0:a?", "-c", "copy"]);
        }
        else
        {
            AddVideo(args, input, media, settings, codecs, features);
        }

        AddMetadata(args, settings);
        if (output == FormatRegistry.Mp4 || output == FormatRegistry.M4a)
        {
            args.AddRange(["-movflags", "+faststart"]);
        }
        args.AddRange(["-f", MuxerFor(output), tempOutputPath]);
        return args;
    }

    /// <summary>
    /// Builds the arguments that extract a single PNG frame at <paramref name="at"/> (video preview).
    /// Patent-free sources only, like <see cref="Build"/>.
    /// </summary>
    public static IReadOnlyList<string> BuildFrameExtraction(
        InputInfo input,
        MediaInfo? media,
        string outputPngPath,
        TimeSpan at,
        ConversionSettings settings)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPngPath);
        ArgumentNullException.ThrowIfNull(settings);
        FfmpegToolset.EnsureNoSystemDecoding(input, media);

        var args = new List<string>(32);
        AddCommonHead(args, reportProgress: false);
        args.AddRange(["-ss", Seconds(at)]);
        AddInput(args, input.Path);
        args.AddRange(["-map", "0:v:0", "-frames:v", "1"]);
        if (!IsStreamCopy(settings))
        {
            AddVideoFilters(args, input, media, settings);
        }
        args.AddRange(["-map_metadata", "-1", "-c:v", "png", "-f", MuxerFor(FormatRegistry.Png), outputPngPath]);
        return args;
    }

    /// <summary>
    /// Audio bitrate that fits <paramref name="targetBytes"/> over <paramref name="duration"/>, floored to the
    /// next lower step. Throws TargetSizeUnreachable below 64 kbit/s or when the duration is unknown.
    /// </summary>
    public static int AudioBitrateForTargetSize(long targetBytes, TimeSpan? duration, string? filePath = null)
    {
        if (duration is not { } d || d <= TimeSpan.Zero)
        {
            throw new ConversionException(ConversionErrorCode.TargetSizeUnreachable, filePath, "ffmpeg-args", "duration unknown");
        }
        var kbps = targetBytes * 8.0 / d.TotalSeconds / 1000.0;
        var step = FloorToStep(kbps);
        return step ?? throw new ConversionException(ConversionErrorCode.TargetSizeUnreachable, filePath, "ffmpeg-args",
            string.Create(CultureInfo.InvariantCulture, $"needs {kbps:0.#} kbit/s, minimum is {AudioBitrateSteps[0]}"));
    }

    /// <summary>Largest step &lt;= kbps, or null when kbps is below the lowest step.</summary>
    public static int? FloorToStep(double kbps)
    {
        int? result = null;
        foreach (var step in AudioBitrateSteps)
        {
            if (step <= kbps)
            {
                result = step;
            }
        }
        return result;
    }

    /// <summary>Maps the 0..100 quality slider onto the bitrate steps (80 → 192 kbit/s).</summary>
    public static int AudioBitrateForQuality(int quality)
    {
        var index = (int)Math.Floor(Math.Clamp(quality, 0, 100) / 100.0 * (AudioBitrateSteps.Count - 1));
        return AudioBitrateSteps[index];
    }

    /// <summary>
    /// Phase-1 approximation of a video target size: one pass with an average bitrate
    /// (<c>-b:v</c>, capped by <c>-maxrate</c>/<c>-bufsize</c>) of target*8/duration minus the audio bitrate.
    /// Exact two-pass encoding is Phase 2 (docs/05-formate.md §Video).
    /// </summary>
    public static int VideoBitrateForTargetSize(long targetBytes, TimeSpan? duration, int audioKbps, string? filePath = null)
    {
        if (duration is not { } d || d <= TimeSpan.Zero)
        {
            throw new ConversionException(ConversionErrorCode.TargetSizeUnreachable, filePath, "ffmpeg-args", "duration unknown");
        }
        var total = targetBytes * 8.0 / d.TotalSeconds / 1000.0;
        var video = (int)Math.Floor(total - audioKbps);
        if (video < MinVideoKbps)
        {
            throw new ConversionException(ConversionErrorCode.TargetSizeUnreachable, filePath, "ffmpeg-args",
                string.Create(CultureInfo.InvariantCulture, $"video would get {video} kbit/s, minimum is {MinVideoKbps}"));
        }
        return video;
    }

    /// <summary>Rough output size for the disk-space check and for previews. 0 when unknown.</summary>
    public static long EstimateOutputBytes(InputInfo input, MediaInfo? media, ConversionSettings settings, FormatRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(settings);
        var duration = media?.Duration ?? input.Duration;
        if (IsVideoOutput(settings.Output) && IsStreamCopy(settings))
        {
            return input.SizeBytes;
        }
        if (duration is not { } d || d <= TimeSpan.Zero)
        {
            return 0;
        }
        var seconds = d.TotalSeconds;
        var output = settings.Output;
        if (output == FormatRegistry.Wav || output == FormatRegistry.Aiff || output == FormatRegistry.Flac)
        {
            var rate = settings.GetAdvancedInt(ConversionSettings.AdvancedKeys.SampleRateHz) ?? media?.SampleRateHz ?? 44100;
            var pcm = (long)(rate * 2 * 2 * seconds);
            return output == FormatRegistry.Flac ? pcm * 6 / 10 : pcm;
        }
        try
        {
            if (IsAudioOutput(output, registry))
            {
                return (long)(ChooseAudioKbps(input, media, settings, isVideo: false) * 1000 / 8.0 * seconds);
            }
            var audioKbps = media is { HasAudio: false } ? 0 : ChooseAudioKbps(input, media, settings, isVideo: true);
            var videoKbps = settings.TargetSizeBytes is { } target
                ? VideoBitrateForTargetSize(target, duration, audioKbps)
                : QualityVideoKbps(input, media, settings);
            if (output != FormatRegistry.Mp4 && settings.TargetSizeBytes is null)
            {
                videoKbps = videoKbps * 7 / 10; // constant-quality VP9 is usually well below the H.264 budget
            }
            return (long)((audioKbps + videoKbps) * 1000 / 8.0 * seconds);
        }
        catch (ConversionException)
        {
            return 0;
        }
    }

    private static void AddCommonHead(List<string> args, bool reportProgress)
    {
        args.AddRange(["-nostdin", "-hide_banner", "-y"]);
        if (reportProgress)
        {
            args.AddRange(["-progress", "pipe:2", "-nostats"]);
        }
    }

    private static void AddInput(List<string> args, string path)
    {
        // Only local files may be opened: keeps crafted playlists from reaching the network.
        args.AddRange(["-protocol_whitelist", "file", "-i", Path.GetFullPath(path)]);
    }

    private static void AddAudioOnly(List<string> args, InputInfo input, MediaInfo? media, ConversionSettings settings, ISystemCodecCapabilities codecs, FfmpegFeatures features)
    {
        var output = settings.Output;
        args.AddRange(["-map", "0:a:0", "-vn"]);

        var encoder = AudioEncoderFor(output, input.Path, codecs, features);
        args.AddRange(["-c:a", encoder]);
        if (!IsLosslessAudio(output))
        {
            args.AddRange(["-b:a", Kbps(ChooseAudioKbps(input, media, settings, isVideo: false))]);
        }
        AddSampleRate(args, settings);
    }

    private static void AddVideo(List<string> args, InputInfo input, MediaInfo? media, ConversionSettings settings, ISystemCodecCapabilities codecs, FfmpegFeatures features)
    {
        var mp4 = settings.Output == FormatRegistry.Mp4;
        args.AddRange(["-map", "0:v:0", "-map", "0:a:0?"]);

        var audioKbps = ChooseAudioKbps(input, media, settings, isVideo: true);
        int? targetVideoKbps = settings.TargetSizeBytes is { } target
            ? VideoBitrateForTargetSize(target, media?.Duration ?? input.Duration, media is { HasAudio: false } ? 0 : audioKbps, input.Path)
            : null;

        if (mp4)
        {
            // Without an audio stream there is nothing for aac_mf to encode; only an unknown layout (no probe data) needs it.
            var hasAudio = media is not { HasAudio: false };
            RequireH264(input.Path, codecs, features);
            if (hasAudio)
            {
                RequireAac(input.Path, codecs, features);
            }
            args.AddRange(["-c:v", H264Encoder]);
            var kbps = targetVideoKbps ?? QualityVideoKbps(input, media, settings);
            args.AddRange(["-b:v", Kbps(kbps)]);
            if (targetVideoKbps is not null)
            {
                args.AddRange(["-maxrate", Kbps(kbps), "-bufsize", Kbps(kbps * 2)]);
            }
            if (hasAudio)
            {
                args.AddRange(["-c:a", AacEncoder, "-b:a", Kbps(audioKbps)]);
            }
        }
        else
        {
            // MKV and WebM: patent-free VP9 + Opus, available on every machine.
            RequireEncoder(Vp9Encoder, input.Path, features);
            RequireEncoder(OpusEncoder, input.Path, features);
            args.AddRange(["-c:v", Vp9Encoder]);
            if (targetVideoKbps is { } kbps)
            {
                args.AddRange(["-b:v", Kbps(kbps), "-maxrate", Kbps(kbps), "-bufsize", Kbps(kbps * 2)]);
            }
            else
            {
                args.AddRange(["-crf", Vp9Crf(settings.QualityClamped).ToString(CultureInfo.InvariantCulture), "-b:v", "0"]);
            }
            args.AddRange(["-row-mt", "1", "-deadline", "good", "-cpu-used", "4"]);
            args.AddRange(["-c:a", OpusEncoder, "-b:a", Kbps(audioKbps)]);
        }

        AddSampleRate(args, settings);
        AddVideoFilters(args, input, media, settings);

        if (settings.GetAdvanced(ConversionSettings.AdvancedKeys.FrameRate) is { Length: > 0 } rate
            && double.TryParse(rate, NumberStyles.Float, CultureInfo.InvariantCulture, out var fps) && fps > 0)
        {
            args.AddRange(["-r", fps.ToString(CultureInfo.InvariantCulture)]);
        }
        if (input.HasWarning(InputWarning.VariableFrameRate) || media is { IsVariableFrameRate: true })
        {
            args.AddRange(["-fps_mode", "cfr"]);
        }
    }

    private static void AddVideoFilters(List<string> args, InputInfo input, MediaInfo? media, ConversionSettings settings)
    {
        var filters = new List<string>(2);
        if (settings.GetAdvancedBool(ConversionSettings.AdvancedKeys.Deinterlace))
        {
            filters.Add("yadif");
        }
        if (TargetHeight(input, media, settings) is { } height)
        {
            filters.Add(string.Create(CultureInfo.InvariantCulture, $"scale=-2:{height}"));
        }
        if (filters.Count > 0)
        {
            args.AddRange(["-vf", string.Join(',', filters)]);
        }
    }

    /// <summary>The output height when scaling down is needed; null when the source already fits. Never upscales.</summary>
    internal static int? TargetHeight(InputInfo input, MediaInfo? media, ConversionSettings settings)
    {
        var max = settings.GetAdvancedInt(ConversionSettings.AdvancedKeys.MaxDimension) ?? PresetHeight(settings.Preset);
        if (max is not > 0)
        {
            return null;
        }
        var sourceHeight = media?.Height ?? input.Height;
        return sourceHeight is { } h && h > max ? max : null;
    }

    private static void AddMetadata(List<string> args, ConversionSettings settings)
    {
        if (settings.Metadata == MetadataPolicy.Strip)
        {
            args.AddRange(["-map_metadata", "-1"]);
        }
    }

    private static void AddSampleRate(List<string> args, ConversionSettings settings)
    {
        if (settings.GetAdvancedInt(ConversionSettings.AdvancedKeys.SampleRateHz) is { } rate and > 0)
        {
            args.AddRange(["-ar", rate.ToString(CultureInfo.InvariantCulture)]);
        }
    }

    private static string AudioEncoderFor(FormatId output, string path, ISystemCodecCapabilities codecs, FfmpegFeatures features)
    {
        switch (output.Id)
        {
            case "mp3":
                if (features.HasEncoder(Mp3Encoder))
                {
                    return Mp3Encoder;
                }
                if (features.HasEncoder(Mp3SystemEncoder))
                {
                    return Mp3SystemEncoder;
                }
                throw new ConversionException(ConversionErrorCode.MissingSystemCodec, path, "ffmpeg-args", "mp3 encoder");
            case "m4a":
                RequireAac(path, codecs, features);
                return AacEncoder;
            case "opus":
                return RequireEncoder(OpusEncoder, path, features);
            case "ogg":
                return RequireEncoder(VorbisEncoder, path, features);
            case "flac":
                return FlacEncoder;
            case "wav":
                return WavEncoder;
            case "aiff":
                return AiffEncoder;
            default:
                throw new ConversionException(ConversionErrorCode.UnsupportedFormat, path, "ffmpeg-args", $"no audio encoder for '{output}'");
        }
    }

    internal static bool IsLosslessAudio(FormatId output) =>
        output == FormatRegistry.Wav || output == FormatRegistry.Flac || output == FormatRegistry.Aiff;

    private static void RequireH264(string path, ISystemCodecCapabilities codecs, FfmpegFeatures features)
    {
        if (!codecs.CanEncodeH264)
        {
            throw new ConversionException(ConversionErrorCode.MissingSystemCodec, path, "ffmpeg-args", "h264 encode");
        }
        RequireEncoder(H264Encoder, path, features);
    }

    private static void RequireAac(string path, ISystemCodecCapabilities codecs, FfmpegFeatures features)
    {
        if (!codecs.CanEncodeAac)
        {
            throw new ConversionException(ConversionErrorCode.MissingSystemCodec, path, "ffmpeg-args", "aac encode");
        }
        RequireEncoder(AacEncoder, path, features);
    }

    private static string RequireEncoder(string encoder, string path, FfmpegFeatures features)
    {
        if (!features.HasEncoder(encoder))
        {
            throw new ConversionException(ConversionErrorCode.MissingSystemCodec, path, "ffmpeg-args", $"encoder '{encoder}' not in this ffmpeg build");
        }
        return encoder;
    }

    /// <summary>Priority: target size (audio outputs only) → explicit bitrate → preset → quality slider.</summary>
    internal static int ChooseAudioKbps(InputInfo input, MediaInfo? media, ConversionSettings settings, bool isVideo)
    {
        if (!isVideo && settings.TargetSizeBytes is { } target && !IsLosslessAudio(settings.Output))
        {
            return AudioBitrateForTargetSize(target, media?.Duration ?? input.Duration, input.Path);
        }
        if (settings.GetAdvancedInt(ConversionSettings.AdvancedKeys.AudioBitrateKbps) is { } explicitKbps and > 0)
        {
            return explicitKbps;
        }
        if (PresetAudioKbps(settings.Preset) is { } preset)
        {
            return preset;
        }
        var byQuality = AudioBitrateForQuality(settings.QualityClamped);
        return isVideo ? Math.Min(byQuality, DefaultVideoAudioKbpsCap) : byQuality;
    }

    /// <summary>Preset table from docs/05-formate.md §Presets.</summary>
    private static int? PresetAudioKbps(ConversionPreset preset) => preset switch
    {
        ConversionPreset.Messenger => 96,
        ConversionPreset.Email => 128,
        ConversionPreset.SocialMedia => 192,
        ConversionPreset.Website => 96,
        _ => null,
    };

    private static int? PresetHeight(ConversionPreset preset) => preset switch
    {
        ConversionPreset.Messenger or ConversionPreset.Email => 720,
        ConversionPreset.SocialMedia or ConversionPreset.Website => 1080,
        _ => null,
    };

    /// <summary>Average bitrate for the quality slider: 0.02..0.12 bits per pixel per frame.</summary>
    internal static int QualityVideoKbps(InputInfo input, MediaInfo? media, ConversionSettings settings)
    {
        var srcW = media?.Width ?? input.Width ?? 1920;
        var srcH = media?.Height ?? input.Height ?? 1080;
        var outH = TargetHeight(input, media, settings) ?? srcH;
        var outW = srcH > 0 ? srcW * outH / (double)srcH : srcW;
        var fps = settings.GetAdvancedInt(ConversionSettings.AdvancedKeys.FrameRate) is { } r and > 0
            ? r
            : Math.Min(media?.FrameRate ?? 30, 60);
        var bitsPerPixel = 0.02 + (0.10 * settings.QualityClamped / 100.0);
        var kbps = outW * outH * fps * bitsPerPixel / 1000.0;
        return (int)Math.Clamp(Math.Round(kbps), 250, 50_000);
    }

    /// <summary>VP9 constant quality: slider 100 → crf 20, 80 → 26, 0 → 50.</summary>
    internal static int Vp9Crf(int quality) => (int)Math.Round(50 - (Math.Clamp(quality, 0, 100) * 0.3));

    private static string Kbps(int kbps) => kbps.ToString(CultureInfo.InvariantCulture) + "k";

    private static string Seconds(TimeSpan value) => value.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture);
}
