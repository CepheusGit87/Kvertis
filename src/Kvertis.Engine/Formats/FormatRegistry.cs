using Kvertis.Engine.Abstractions;

namespace Kvertis.Engine.Formats;

/// <summary>Static description of a format: kind, extensions, whether Kvertis can read/write it.</summary>
public sealed record FormatDescriptor(
    FormatId Id,
    MediaKind Kind,
    string DisplayName,
    IReadOnlyList<string> Extensions,
    bool CanRead,
    bool CanWrite,
    bool Lossless = false,
    bool SupportsTransparency = false,
    bool PatentEncumbered = false)
{
    public string PrimaryExtension => Extensions[0];
}

/// <summary>Which outputs an input can become, ordered with the suggested default first.</summary>
public sealed record OutputSuggestion(FormatId Default, IReadOnlyList<FormatId> Options);

/// <summary>
/// The format matrix from docs/05-formate.md, as code. Pure data; converters decide the details.
/// Formats that need a system codec are filtered at runtime via <see cref="ISystemCodecCapabilities"/>.
/// Whether a concrete file can become a listed output depends on its stream codecs (ADR-015); the UI asks
/// <see cref="IConverterResolver.CanConvert"/> for that.
/// </summary>
public sealed class FormatRegistry
{
    public static readonly FormatId Jpg = new("jpg");
    public static readonly FormatId Png = new("png");
    public static readonly FormatId WebP = new("webp");
    public static readonly FormatId Gif = new("gif");
    public static readonly FormatId Bmp = new("bmp");
    public static readonly FormatId Tiff = new("tiff");
    public static readonly FormatId Heic = new("heic");
    public static readonly FormatId Avif = new("avif");

    public static readonly FormatId Svg = new("svg");
    public static readonly FormatId Ico = new("ico");
    public static readonly FormatId Raw = new("raw");
    public static readonly FormatId Psd = new("psd");

    public static readonly FormatId Mp3 = new("mp3");
    public static readonly FormatId Wav = new("wav");
    public static readonly FormatId Flac = new("flac");
    public static readonly FormatId Ogg = new("ogg");
    public static readonly FormatId Opus = new("opus");
    public static readonly FormatId M4a = new("m4a");
    public static readonly FormatId Wma = new("wma");
    public static readonly FormatId Aiff = new("aiff");

    public static readonly FormatId Mp4 = new("mp4");
    public static readonly FormatId Mov = new("mov");
    public static readonly FormatId Mkv = new("mkv");
    public static readonly FormatId WebM = new("webm");
    public static readonly FormatId Avi = new("avi");
    public static readonly FormatId Wmv = new("wmv");
    public static readonly FormatId Flv = new("flv");
    public static readonly FormatId ThreeGp = new("3gp");
    public static readonly FormatId Mpeg = new("mpeg");
    public static readonly FormatId Ts = new("ts");

    public static readonly FormatId Pdf = new("pdf");
    public static readonly FormatId Docx = new("docx");
    public static readonly FormatId Xlsx = new("xlsx");
    public static readonly FormatId Pptx = new("pptx");
    public static readonly FormatId Txt = new("txt");
    public static readonly FormatId Markdown = new("md");
    public static readonly FormatId Html = new("html");
    public static readonly FormatId Csv = new("csv");
    /// <summary>Legacy binary office formats (DOC/XLS/PPT): recognized so we can say "not supported" clearly.</summary>
    public static readonly FormatId LegacyOffice = new("doc");

    private static readonly IReadOnlyList<FormatDescriptor> Descriptors =
    [
        new(Jpg, MediaKind.Image, "JPG", ["jpg", "jpeg", "jpe", "jfif"], true, true),
        new(Png, MediaKind.Image, "PNG", ["png"], true, true, Lossless: true, SupportsTransparency: true),
        new(WebP, MediaKind.Image, "WebP", ["webp"], true, true, SupportsTransparency: true),
        new(Gif, MediaKind.Image, "GIF", ["gif"], true, true, SupportsTransparency: true),
        new(Bmp, MediaKind.Image, "BMP", ["bmp", "dib"], true, true, Lossless: true),
        new(Tiff, MediaKind.Image, "TIFF", ["tif", "tiff"], true, true, Lossless: true, SupportsTransparency: true),
        new(Heic, MediaKind.Image, "HEIC", ["heic", "heif", "hif"], true, false, SupportsTransparency: true, PatentEncumbered: true),
        // AVIF is decoded only by the operating system (AV1 video extension via ISystemImageCodec), never bundled.
        new(Avif, MediaKind.Image, "AVIF", ["avif"], true, false, SupportsTransparency: true),
        // CanRead=false: no patent-free/low-risk library in Phase 1 (recognized so the user gets a clear message).
        new(Svg, MediaKind.Image, "SVG", ["svg"], false, false, SupportsTransparency: true),
        new(Ico, MediaKind.Image, "ICO", ["ico"], true, true, SupportsTransparency: true),
        // DNG is decoded by SkiaSharp; the other RAW variants need the system RAW image extension.
        new(Raw, MediaKind.Image, "RAW", ["dng", "cr2", "cr3", "nef", "arw", "orf", "raf", "rw2"], true, false),
        // CanRead=false: no patent-free/low-risk library in Phase 1.
        new(Psd, MediaKind.Image, "PSD", ["psd"], false, false, SupportsTransparency: true),

        new(Mp3, MediaKind.Audio, "MP3", ["mp3"], true, true),
        new(Wav, MediaKind.Audio, "WAV", ["wav"], true, true, Lossless: true),
        new(Flac, MediaKind.Audio, "FLAC", ["flac"], true, true, Lossless: true),
        new(Ogg, MediaKind.Audio, "OGG (Vorbis)", ["ogg", "oga"], true, true),
        new(Opus, MediaKind.Audio, "Opus", ["opus"], true, true),
        new(M4a, MediaKind.Audio, "M4A (AAC)", ["m4a", "aac"], true, true, PatentEncumbered: true),
        new(Wma, MediaKind.Audio, "WMA", ["wma"], true, false),
        new(Aiff, MediaKind.Audio, "AIFF", ["aiff", "aif"], true, true, Lossless: true),

        new(Mp4, MediaKind.Video, "MP4", ["mp4", "m4v"], true, true, PatentEncumbered: true),
        new(Mov, MediaKind.Video, "MOV", ["mov", "qt"], true, false, PatentEncumbered: true),
        new(Mkv, MediaKind.Video, "MKV", ["mkv"], true, true),
        new(WebM, MediaKind.Video, "WebM", ["webm"], true, true),
        new(Avi, MediaKind.Video, "AVI", ["avi"], true, false),
        new(Wmv, MediaKind.Video, "WMV", ["wmv", "asf"], true, false),
        new(Flv, MediaKind.Video, "FLV", ["flv"], true, false),
        new(ThreeGp, MediaKind.Video, "3GP", ["3gp", "3g2"], true, false),
        new(Mpeg, MediaKind.Video, "MPEG", ["mpg", "mpeg", "vob"], true, false),
        new(Ts, MediaKind.Video, "TS", ["ts", "mts", "m2ts"], true, false),

        new(Pdf, MediaKind.Document, "PDF", ["pdf"], true, true),
        new(Docx, MediaKind.Document, "DOCX", ["docx", "docm"], true, false),
        new(Xlsx, MediaKind.Document, "XLSX", ["xlsx", "xlsm"], true, false),
        new(Pptx, MediaKind.Document, "PPTX", ["pptx", "pptm"], true, false),
        new(Txt, MediaKind.Document, "Text", ["txt", "log"], true, true),
        new(Markdown, MediaKind.Document, "Markdown", ["md", "markdown"], true, true),
        new(Html, MediaKind.Document, "HTML", ["html", "htm"], true, true),
        new(Csv, MediaKind.Document, "CSV", ["csv"], true, true),
        new(LegacyOffice, MediaKind.Document, "DOC/XLS/PPT (legacy)", ["doc", "xls", "ppt"], false, false),
    ];

    private static readonly Dictionary<FormatId, IReadOnlyList<FormatId>> Matrix = new()
    {
        [Jpg] = [Png, WebP, Tiff, Bmp, Gif, Pdf, Jpg],
        [Png] = [Jpg, WebP, Tiff, Bmp, Gif, Ico, Pdf, Png],
        [WebP] = [Jpg, Png, Tiff, Gif],
        [Gif] = [Png, Jpg, WebP],
        [Bmp] = [Png, Jpg, WebP],
        [Tiff] = [Png, Jpg, WebP, Pdf],
        [Heic] = [Jpg, Png, WebP],
        [Avif] = [Jpg, Png, WebP],
        [Svg] = [Png, Jpg, WebP],
        [Ico] = [Png],
        [Raw] = [Jpg, Png, Tiff],
        [Psd] = [Png, Jpg],

        [Mp3] = [Wav, Flac, Ogg, Opus, M4a, Mp3],
        [Wav] = [Mp3, Flac, Ogg, Opus, M4a],
        [Flac] = [Mp3, Wav, Ogg, Opus, M4a],
        [Ogg] = [Mp3, Wav, Flac, Opus, M4a],
        [Opus] = [Mp3, Wav, Flac, Ogg, M4a],
        // AAC/WMA inputs are decoded by Media Foundation only (ADR-015), which writes MP3, WAV, FLAC and M4A.
        [M4a] = [Mp3, Wav, Flac, M4a],
        [Wma] = [Mp3, Wav, Flac, M4a],
        [Aiff] = [Mp3, Wav, Flac, Ogg, Opus, M4a],

        // Container families that usually carry H.264/HEVC/AAC/MPEG-4/WMV: Media Foundation, MP4/M4A/MP3/WAV/FLAC
        // only. H.264/HEVC → WebM/MKV is Phase 2 (ADR-015). MPEG/TS with MPEG-2 video would also work through
        // ffmpeg, but the matrix stays simple; the converters decide per stream codec.
        [Mp4] = [Mp4, Mp3, M4a, Wav, Flac],
        [Mov] = [Mp4, Mp3, M4a, Wav, Flac],
        [Avi] = [Mp4, Mp3, M4a, Wav, Flac],
        [Wmv] = [Mp4, Mp3, M4a, Wav, Flac],
        [ThreeGp] = [Mp4, Mp3, M4a, Wav, Flac],
        [Mpeg] = [Mp4, Mp3, M4a, Wav, Flac],
        [Ts] = [Mp4, Mp3, M4a, Wav, Flac],
        // MKV can hold anything: the static list is the union; IConverterResolver.CanConvert decides per file
        // (VP9/AV1 → all, H.264/HEVC → MP4/M4A/MP3/WAV/FLAC only).
        [Mkv] = [Mp4, WebM, Mkv, Mp3, Wav, Flac, M4a],
        [WebM] = [Mp4, Mkv, Mp3, Wav, Flac, M4a],
        // FLV: recognized only. The FFmpeg build has no FLV demuxer and Media Foundation cannot read FLV.

        [Pdf] = [Txt, Png, Jpg],
        [Docx] = [Txt, Markdown, Html],
        [Xlsx] = [Csv, Txt],
        [Pptx] = [Txt, Markdown],
        [Txt] = [Pdf, Html, Markdown],
        [Markdown] = [Pdf, Html, Txt],
        [Html] = [Txt, Markdown],
        [Csv] = [Txt],
    };

    private readonly Dictionary<FormatId, FormatDescriptor> _byId;
    private readonly Dictionary<string, FormatDescriptor> _byExtension;

    public FormatRegistry()
    {
        _byId = Descriptors.ToDictionary(d => d.Id);
        _byExtension = new Dictionary<string, FormatDescriptor>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in Descriptors)
        {
            foreach (var ext in d.Extensions)
            {
                _byExtension.TryAdd(ext, d);
            }
        }
    }

    public IReadOnlyCollection<FormatDescriptor> All => _byId.Values;

    public FormatDescriptor? Get(FormatId id) => _byId.GetValueOrDefault(id);

    public FormatDescriptor? GetByExtension(string extensionOrPath)
    {
        var ext = Path.GetExtension(extensionOrPath);
        if (string.IsNullOrEmpty(ext))
        {
            ext = extensionOrPath;
        }
        return _byExtension.GetValueOrDefault(ext.TrimStart('.'));
    }

    public MediaKind KindOf(FormatId id) => Get(id)?.Kind ?? MediaKind.Unknown;

    public string ExtensionFor(FormatId id) => Get(id)?.PrimaryExtension ?? id.Id;

    /// <summary>
    /// Suggests outputs for an input. The first entry is the default for lay users. Same-format entries
    /// (JPG → JPG for recompression) are kept but never suggested as default.
    /// </summary>
    public OutputSuggestion? Suggest(InputInfo input, ISystemCodecCapabilities? codecs = null)
    {
        if (!Matrix.TryGetValue(input.Format, out var outputs))
        {
            return null;
        }

        var options = outputs.Where(o => IsProducible(o, codecs)).ToList();
        if (options.Count == 0)
        {
            return null;
        }

        var @default = options.First(o => o != input.Format);
        return new OutputSuggestion(@default, options);
    }

    public bool IsProducible(FormatId output, ISystemCodecCapabilities? codecs)
    {
        var d = Get(output);
        if (d is null || !d.CanWrite)
        {
            return false;
        }
        if (codecs is null)
        {
            return true;
        }
        if (output == Mp4)
        {
            return codecs.CanEncodeH264 && codecs.CanEncodeAac;
        }
        if (output == M4a)
        {
            return codecs.CanEncodeAac;
        }
        return true;
    }
}
