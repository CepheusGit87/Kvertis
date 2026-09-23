using Kvertis.Engine.Abstractions;
using Kvertis.Engine.Ffmpeg;
using Kvertis.Engine.Formats;

namespace Kvertis.Engine.Conversion.Video;

/// <summary>Output containers the system transcoder (Windows Media Foundation) produces in Phase 1.</summary>
public enum TranscodeContainer
{
    /// <summary>MP4 with H.264 video and AAC audio.</summary>
    Mp4,
    /// <summary>M4A with AAC audio.</summary>
    M4a,
    Mp3,
    Wav,
    Flac,
}

/// <summary>
/// Platform-neutral decisions for the Media Foundation transcoder (ADR-015): which inputs it takes and which
/// encoding parameters it uses. The Windows class only maps a plan onto a <c>MediaEncodingProfile</c>.
/// Bitrates follow the ffmpeg path (quality slider 0.02–0.12 bits per pixel and frame, audio steps,
/// single-pass target size) so both paths produce comparable results.
/// </summary>
/// <param name="Container">Output container and codec family.</param>
/// <param name="Width">Output video width (even), or null to keep the source size (unknown source size).</param>
/// <param name="Height">Output video height (even), or null to keep the source size.</param>
/// <param name="VideoBitrateKbps">Average video bitrate; null for audio-only outputs.</param>
/// <param name="FrameRate">Output frame rate; null keeps the source rate (or the encoder default when unknown).</param>
/// <param name="IncludeAudio">False when the source has no audio stream (MP4 then carries video only).</param>
/// <param name="AudioBitrateKbps">Audio bitrate; null for lossless outputs (WAV, FLAC) or when there is no audio.</param>
/// <param name="SampleRateHz">Explicit sample rate, or null to keep the encoder default for the profile.</param>
public sealed record TranscodePlan(
    TranscodeContainer Container,
    int? Width,
    int? Height,
    int? VideoBitrateKbps,
    double? FrameRate,
    bool IncludeAudio,
    int? AudioBitrateKbps,
    int? SampleRateHz)
{
    /// <summary>
    /// Bitrates the system AAC encoder accepts (96, 128, 160, 192 kbit/s; the encoder rejects anything else).
    /// </summary>
    public static readonly IReadOnlyList<int> AacBitrateSteps = [96, 128, 160, 192];

    /// <summary>The outputs the system transcoder produces. No WMV, MKV or WebM (Phase 2).</summary>
    public static readonly IReadOnlyList<FormatId> Outputs =
        [FormatRegistry.Mp4, FormatRegistry.M4a, FormatRegistry.Mp3, FormatRegistry.Wav, FormatRegistry.Flac];

    /// <summary>
    /// Containers Media Foundation reads on Windows 10/11 (MKV since 1607). FLV, WebM and OGG are not among
    /// them; encumbered streams in those containers have no path in Phase 1.
    /// </summary>
    private static readonly HashSet<FormatId> ReadableContainers =
    [
        FormatRegistry.Mp4, FormatRegistry.Mov, FormatRegistry.M4a, FormatRegistry.Wmv, FormatRegistry.Wma,
        FormatRegistry.Avi, FormatRegistry.ThreeGp, FormatRegistry.Mpeg, FormatRegistry.Ts, FormatRegistry.Mkv,
    ];

    /// <summary>
    /// Always false: <c>MediaTranscoder</c> has no switch to drop container metadata, so <see cref="MetadataPolicy.Strip"/>
    /// cannot be honoured on this path (GPS/EXIF blocks of camera files may be carried over). The prober reports
    /// <see cref="InputWarning.MetadataNotStrippable"/> for inputs routed here so the UI can say so.
    /// </summary>
    public bool CanStripMetadata => false;

    /// <summary>True when the output has no video track.</summary>
    public bool AudioOnly => Container != TranscodeContainer.Mp4;

    /// <summary>
    /// Whether the system transcoder handles <paramref name="input"/> → <paramref name="output"/>:
    /// audio/video input in a container Media Foundation reads, output in <see cref="Outputs"/> and either probe data showing a patent-encumbered
    /// stream, or no probe data and a container family that usually carries such streams. Patent-free inputs
    /// stay with the ffmpeg converters. MP4/M4A additionally need the system H.264/AAC encoders when
    /// <paramref name="codecs"/> is given.
    /// </summary>
    public static bool Supports(InputInfo input, FormatId output, MediaInfo? media, ISystemCodecCapabilities? codecs = null)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.Kind is not (MediaKind.Audio or MediaKind.Video) || !Outputs.Contains(output) || !ReadableContainers.Contains(input.Format))
        {
            return false;
        }
        if (output == FormatRegistry.Mp4 && input.Kind != MediaKind.Video)
        {
            return false;
        }
        if (codecs is not null)
        {
            if (output == FormatRegistry.Mp4 && !codecs.CanEncodeH264)
            {
                return false;
            }
            if ((output == FormatRegistry.Mp4 || output == FormatRegistry.M4a) && !codecs.CanEncodeAac)
            {
                return false;
            }
        }
        if (media is null)
        {
            return EncumberedCodecs.IsSystemDecodingFamily(input.Format);
        }
        if (!media.RequiresSystemDecoding)
        {
            return false;
        }
        if (output == FormatRegistry.Mp4)
        {
            return media.HasVideo;
        }
        return media.HasAudio;
    }

    /// <summary>
    /// Builds the plan. Throws UnsupportedFormat for outputs the transcoder does not produce and
    /// TargetSizeUnreachable like the ffmpeg path (duration unknown, bitrate too low).
    /// </summary>
    public static TranscodePlan Create(InputInfo input, MediaInfo? media, ConversionSettings settings)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(settings);
        var container = ContainerFor(settings.Output)
                        ?? throw new ConversionException(ConversionErrorCode.UnsupportedFormat, input.Path, "transcode-plan", $"no system profile for '{settings.Output}'");
        var sampleRate = settings.GetAdvancedInt(ConversionSettings.AdvancedKeys.SampleRateHz) is { } rate and > 0 ? rate : (int?)null;

        if (container != TranscodeContainer.Mp4)
        {
            if (media is { HasAudio: false })
            {
                throw new ConversionException(ConversionErrorCode.UnsupportedFormat, input.Path, "transcode-plan", "input has no audio stream");
            }
            int? audioKbps = container switch
            {
                TranscodeContainer.Wav or TranscodeContainer.Flac => null,
                TranscodeContainer.M4a => EnsureFitsTargetSize(AacStep(FfmpegArguments.ChooseAudioKbps(input, media, settings, isVideo: false)), input, media, settings),
                _ => FfmpegArguments.ChooseAudioKbps(input, media, settings, isVideo: false),
            };
            return new TranscodePlan(container, null, null, null, null, true, audioKbps, sampleRate);
        }

        if (input.Kind != MediaKind.Video || media is { HasVideo: false })
        {
            throw new ConversionException(ConversionErrorCode.UnsupportedFormat, input.Path, "transcode-plan", "video output needs a video input");
        }

        var includeAudio = media is not { HasAudio: false };
        var videoAudioKbps = includeAudio
            ? EnsureFitsTargetSize(AacStep(FfmpegArguments.ChooseAudioKbps(input, media, settings, isVideo: true)), input, media, settings)
            : (int?)null;
        var videoKbps = settings.TargetSizeBytes is { } target
            ? FfmpegArguments.VideoBitrateForTargetSize(target, media?.Duration ?? input.Duration, videoAudioKbps ?? 0, input.Path)
            : FfmpegArguments.QualityVideoKbps(input, media, settings);

        var (width, height) = OutputSize(input, media, settings);
        return new TranscodePlan(container, width, height, videoKbps, OutputFrameRate(media, settings), includeAudio, videoAudioKbps, sampleRate);
    }

    /// <summary>Rough output size in bytes for the disk-space check and previews; 0 when the duration is unknown.</summary>
    public long EstimateBytes(TimeSpan? duration)
    {
        if (duration is not { } d || d <= TimeSpan.Zero)
        {
            return 0;
        }
        var seconds = d.TotalSeconds;
        var rate = SampleRateHz ?? 44100;
        var pcm = (long)(rate * 2 * 2 * seconds);
        return Container switch
        {
            TranscodeContainer.Wav => pcm,
            TranscodeContainer.Flac => pcm * 6 / 10,
            _ => (long)(((VideoBitrateKbps ?? 0) + (AudioBitrateKbps ?? 0)) * 1000 / 8.0 * seconds),
        };
    }

    /// <summary>The container for an output format, or null when the system transcoder does not produce it.</summary>
    public static TranscodeContainer? ContainerFor(FormatId output) => output.Id switch
    {
        "mp4" => TranscodeContainer.Mp4,
        "m4a" => TranscodeContainer.M4a,
        "mp3" => TranscodeContainer.Mp3,
        "wav" => TranscodeContainer.Wav,
        "flac" => TranscodeContainer.Flac,
        _ => null,
    };

    /// <summary>
    /// The AAC encoder's floor (96 kbit/s) can exceed a small target-size budget. Like the ffmpeg path, a bitrate
    /// that does not fit the budget is TargetSizeUnreachable instead of a file larger than requested.
    /// </summary>
    private static int EnsureFitsTargetSize(int audioKbps, InputInfo input, MediaInfo? media, ConversionSettings settings)
    {
        if (settings.TargetSizeBytes is not { } target)
        {
            return audioKbps;
        }
        if ((media?.Duration ?? input.Duration) is not { } d || d <= TimeSpan.Zero)
        {
            throw new ConversionException(ConversionErrorCode.TargetSizeUnreachable, input.Path, "transcode-plan", "duration unknown");
        }
        var budgetKbps = target * 8.0 / d.TotalSeconds / 1000.0;
        if (audioKbps > budgetKbps)
        {
            throw new ConversionException(ConversionErrorCode.TargetSizeUnreachable, input.Path, "transcode-plan",
                string.Create(System.Globalization.CultureInfo.InvariantCulture, $"aac needs {audioKbps} kbit/s, budget is {budgetKbps:0.#}"));
        }
        return audioKbps;
    }

    /// <summary>Largest accepted AAC bitrate at or below <paramref name="kbps"/>; 96 as the floor.</summary>
    public static int AacStep(int kbps)
    {
        var result = AacBitrateSteps[0];
        foreach (var step in AacBitrateSteps)
        {
            if (step <= kbps)
            {
                result = step;
            }
        }
        return result;
    }

    /// <summary>
    /// Output size: the source size, scaled down to the requested height (never up), width rounded to an even
    /// number to keep the aspect ratio. Null/null when the source size is unknown.
    /// </summary>
    internal static (int? Width, int? Height) OutputSize(InputInfo input, MediaInfo? media, ConversionSettings settings)
    {
        var srcW = media?.Width ?? input.Width;
        var srcH = media?.Height ?? input.Height;
        if (srcW is not > 0 || srcH is not > 0)
        {
            return (null, null);
        }
        var height = FfmpegArguments.TargetHeight(input, media, settings) ?? srcH.Value;
        var width = (int)Math.Round(srcW.Value * (double)height / srcH.Value / 2.0) * 2;
        return (Math.Max(2, width), Math.Max(2, height - (height % 2)));
    }

    private static double? OutputFrameRate(MediaInfo? media, ConversionSettings settings)
    {
        if (settings.GetAdvanced(ConversionSettings.AdvancedKeys.FrameRate) is { Length: > 0 } text
            && double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var fps) && fps > 0)
        {
            return fps;
        }
        return media?.FrameRate is { } rate && rate > 0 ? Math.Min(rate, 60) : null;
    }
}
