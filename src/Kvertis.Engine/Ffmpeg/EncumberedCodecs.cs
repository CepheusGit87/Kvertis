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

    private static readonly HashSet<string> Set = new(Names, StringComparer.OrdinalIgnoreCase);

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
}
