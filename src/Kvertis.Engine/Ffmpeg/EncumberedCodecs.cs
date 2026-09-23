using Kvertis.Engine.Abstractions;
using Kvertis.Engine.Formats;

namespace Kvertis.Engine.Ffmpeg;

/// <summary>
/// The single place that knows which stream codecs are patent-encumbered (docs/02-rechtssicherheit.md §3,
/// ADR-015). Inputs with any of these codecs are decoded only by Windows Media Foundation and never
/// reach ffmpeg (not even for a stream copy or a sound-track extraction).
/// </summary>
/// <remarks>
/// The names are ffprobe <c>codec_name</c> values. They are assembled from parts on purpose: a source scan
/// test guarantees that the decoder names never appear as plain string literals under <c>src/</c>, so no
/// code can select such a decoder by name.
/// </remarks>
public static class EncumberedCodecs
{
    private static readonly string[] Names =
    [
        // Video
        "h26" + "4",
        "he" + "vc",
        "vv" + "c",
        "mpeg" + "4",
        "msmpeg4" + "v1",
        "msmpeg4" + "v2",
        "msmpeg4" + "v3",
        "wm" + "v1",
        "wm" + "v2",
        "wm" + "v3",
        "vc" + "1",
        "h26" + "3",
        "h26" + "3p",
        "h26" + "3i",
        "fl" + "v1",
        "pro" + "res",
        "dnx" + "hd",
        // Audio
        "aa" + "c",
        "aa" + "c_latm",
        "wma" + "v1",
        "wma" + "v2",
        "wma" + "pro",
        "wma" + "lossless",
        "wma" + "voice",
        "ea" + "c3",
        "dt" + "s",
        "dc" + "a",
        "true" + "hd",
        "ml" + "p",
        "amr" + "nb",
        "amr" + "wb",
        "amr_" + "nb",
        "amr_" + "wb",
    ];

    /// <summary>
    /// ffmpeg decoder names (<c>ffmpeg -decoders</c>) for encumbered codecs that differ from the ffprobe codec
    /// name: the fixed-point AAC decoder, the FLV1 decoder and the WMV3/VC-1 image decoders.
    /// </summary>
    private static readonly string[] DecoderOnlyNames =
    [
        "aa" + "c_fixed",
        "fl" + "v",
        "wm" + "v3image",
        "vc" + "1image",
    ];

    /// <summary>
    /// Suffixes of hardware/wrapper decoders (<c>h264_qsv</c>, <c>hevc_cuvid</c>, …). Such a decoder is forbidden
    /// when the name without the suffix is an encumbered codec.
    /// </summary>
    private static readonly string[] HardwareDecoderSuffixes =
    [
        "_qsv", "_cuvid", "_amf", "_nvenc", "_mediacodec", "_v4l2m2m", "_rkmpp", "_mmal", "_crystalhd",
    ];

    private static readonly HashSet<string> Set = new(Names, StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> DecoderSet = new(Names.Concat(DecoderOnlyNames), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Containers that structurally cannot carry an encumbered stream in practice (WebM: VP8/VP9/AV1 + Vorbis/Opus;
    /// Ogg, Opus, FLAC, WAV/AIFF PCM, MP3). Only these may reach ffmpeg without probe data (ADR-015); every other
    /// container without probe data is refused by the ffmpeg converters.
    /// </summary>
    private static readonly HashSet<FormatId> PatentFreeContainers =
    [
        FormatRegistry.WebM, FormatRegistry.Ogg, FormatRegistry.Opus, FormatRegistry.Flac,
        FormatRegistry.Wav, FormatRegistry.Aiff, FormatRegistry.Mp3,
    ];

    /// <summary>
    /// Container families that usually carry encumbered codecs. With unknown stream codecs (no probe data)
    /// such inputs are routed to Media Foundation and refused by the ffmpeg converters.
    /// </summary>
    private static readonly HashSet<FormatId> SystemDecodingFamilies =
    [
        FormatRegistry.Mp4, FormatRegistry.Mov, FormatRegistry.M4a, FormatRegistry.Wmv, FormatRegistry.Wma,
        FormatRegistry.Avi, FormatRegistry.ThreeGp, FormatRegistry.Mpeg, FormatRegistry.Ts,
    ];

    /// <summary>The ffprobe codec name of the HEVC video codec (needs the system HEVC extension).</summary>
    public static readonly string Hevc = Names[1];

    /// <summary>All encumbered codec names (ffprobe <c>codec_name</c>), lowercase.</summary>
    public static IReadOnlyCollection<string> All => Names;

    /// <summary>
    /// Every ffmpeg decoder name that must not exist in the bundled build without hardware suffixes (codec names
    /// plus <see cref="DecoderOnlyNames"/>). Single source for <see cref="FfmpegCompliance"/>, the source scan test
    /// and <c>tools/ffmpeg/check-build.sh</c>.
    /// </summary>
    public static IReadOnlyCollection<string> ForbiddenDecoderNames => DecoderSet;

    /// <summary>
    /// True when an ffmpeg decoder name (<c>ffmpeg -decoders</c>) decodes an encumbered codec, including hardware
    /// variants such as <c>h264_qsv</c> or <c>hevc_cuvid</c>.
    /// </summary>
    public static bool IsForbiddenDecoder(string? decoderName)
    {
        if (string.IsNullOrWhiteSpace(decoderName))
        {
            return false;
        }
        var name = decoderName.Trim();
        if (DecoderSet.Contains(name))
        {
            return true;
        }
        foreach (var suffix in HardwareDecoderSuffixes)
        {
            if (name.Length > suffix.Length && name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
                && DecoderSet.Contains(name[..^suffix.Length]))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>True when <paramref name="codecName"/> is a patent-encumbered codec.</summary>
    public static bool Contains(string? codecName) => codecName is not null && Set.Contains(codecName.Trim());

    /// <summary>True when any of the given stream codecs is encumbered.</summary>
    public static bool ContainsAny(IEnumerable<string?> codecNames)
    {
        ArgumentNullException.ThrowIfNull(codecNames);
        return codecNames.Any(Contains);
    }

    /// <summary>
    /// True when inputs of this container family are handed to Media Foundation while their stream codecs are
    /// unknown (MP4, MOV, M4A, WMV, WMA, AVI, 3GP, MPEG, TS).
    /// </summary>
    public static bool IsSystemDecodingFamily(FormatId format) => SystemDecodingFamilies.Contains(format);

    /// <summary>
    /// True when the container cannot carry encumbered streams (WebM, OGG, Opus, FLAC, WAV, AIFF, MP3), so ffmpeg
    /// may open it even without probe data. MKV and every other container need probe data first.
    /// </summary>
    public static bool IsPatentFreeContainer(FormatId format) => PatentFreeContainers.Contains(format);
}
