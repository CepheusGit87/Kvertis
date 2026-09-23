using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using Kvertis.Engine.Abstractions;

namespace Kvertis.Engine.Formats;

/// <summary>
/// Content sniffing. Reads the first bytes of a file and maps them to a FormatId. This is the only
/// authority on what a file is; the extension is used solely to raise ExtensionMismatch.
/// </summary>
public static class MagicBytes
{
    /// <summary>Bytes the fixed-offset signatures look at. Everything except MPEG-TS is decided within this prefix.</summary>
    public const int HeaderLength = 64;

    /// <summary>Largest sample <see cref="Detect"/> makes use of (MPEG-TS sync bytes at 0, 188 and 376).</summary>
    public const int SampleLength = 4096;

    private const int TsPacketLength = 188;

    /// <summary>
    /// Classifies a file by its first bytes. <paramref name="sample"/> should be the file prefix up to
    /// <see cref="SampleLength"/> bytes; only the first <see cref="HeaderLength"/> bytes are used for
    /// the fixed signatures, the rest only for the MPEG-TS packet check.
    /// </summary>
    public static FormatId? Detect(ReadOnlySpan<byte> sample, string? path = null)
    {
        var header = sample[..Math.Min(sample.Length, HeaderLength)];
        if (header.Length < 4)
        {
            return null;
        }

        if (StartsWith(header, [0xFF, 0xD8, 0xFF]))
        {
            return FormatRegistry.Jpg;
        }
        if (StartsWith(header, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]))
        {
            return FormatRegistry.Png;
        }
        if (StartsWith(header, "GIF87a"u8) || StartsWith(header, "GIF89a"u8))
        {
            return FormatRegistry.Gif;
        }
        if (StartsWith(header, "BM"u8) && header.Length >= 14)
        {
            return FormatRegistry.Bmp;
        }
        if (StartsWith(header, [0x49, 0x49, 0x2A, 0x00]) || StartsWith(header, [0x4D, 0x4D, 0x00, 0x2A]))
        {
            // Little/big endian TIFF. Many camera RAW formats are TIFF containers; keep the extension as a hint.
            return IsRawExtension(path) ? FormatRegistry.Raw : FormatRegistry.Tiff;
        }
        if (StartsWith(header, "8BPS"u8))
        {
            return FormatRegistry.Psd;
        }
        if (StartsWith(header, [0x00, 0x00, 0x01, 0x00]))
        {
            return FormatRegistry.Ico;
        }
        if (StartsWith(header, "RIFF"u8) && header.Length >= 12)
        {
            var riffType = header.Slice(8, 4);
            if (riffType.SequenceEqual("WEBP"u8))
            {
                return FormatRegistry.WebP;
            }
            if (riffType.SequenceEqual("WAVE"u8))
            {
                return FormatRegistry.Wav;
            }
            if (riffType.SequenceEqual("AVI "u8))
            {
                return FormatRegistry.Avi;
            }
        }
        if (StartsWith(header, "FORM"u8) && header.Length >= 12 && (header.Slice(8, 4).SequenceEqual("AIFF"u8) || header.Slice(8, 4).SequenceEqual("AIFC"u8)))
        {
            return FormatRegistry.Aiff;
        }
        if (StartsWith(header, "fLaC"u8))
        {
            return FormatRegistry.Flac;
        }
        if (StartsWith(header, "OggS"u8))
        {
            // Vorbis and Opus share the container; the first packet header tells them apart.
            return header.Length >= 36 && IndexOf(header, "OpusHead"u8) >= 0 ? FormatRegistry.Opus : FormatRegistry.Ogg;
        }
        if (HasTextBom(header))
        {
            // UTF-8/UTF-16 byte order marks: FF FE would otherwise pass as an MP3 frame sync. DetectText handles these.
            return null;
        }
        if (StartsWith(header, "ID3"u8) || IsMp3FrameHeader(header))
        {
            return FormatRegistry.Mp3;
        }
        if (StartsWith(header, [0x1A, 0x45, 0xDF, 0xA3]))
        {
            return IndexOf(header, "webm"u8) >= 0 ? FormatRegistry.WebM : FormatRegistry.Mkv;
        }
        if (StartsWith(header, [0x30, 0x26, 0xB2, 0x75, 0x8E, 0x66, 0xCF, 0x11]))
        {
            // ASF container: WMV or WMA. Prefer the extension hint; default to WMV.
            return string.Equals(Path.GetExtension(path), ".wma", StringComparison.OrdinalIgnoreCase) ? FormatRegistry.Wma : FormatRegistry.Wmv;
        }
        if (StartsWith(header, "FLV"u8))
        {
            return FormatRegistry.Flv;
        }
        if (StartsWith(header, [0x00, 0x00, 0x01, 0xBA]) || StartsWith(header, [0x00, 0x00, 0x01, 0xB3]))
        {
            return FormatRegistry.Mpeg;
        }
        if (IsTransportStream(sample))
        {
            return FormatRegistry.Ts;
        }
        if (header.Length >= 12 && header.Slice(4, 4).SequenceEqual("ftyp"u8))
        {
            return DetectIsoBmff(header, path);
        }
        if (StartsWith(header, "%PDF"u8))
        {
            return FormatRegistry.Pdf;
        }
        if (StartsWith(header, [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1]))
        {
            // OLE compound file: legacy .doc/.xls/.ppt or an encrypted OOXML file.
            return FormatRegistry.LegacyOffice;
        }
        if (StartsWith(header, "PK"u8))
        {
            return null; // ZIP: needs DetectZipBased with the full file.
        }
        if (LooksLikeSvg(header))
        {
            return FormatRegistry.Svg;
        }
        return null;
    }

    private static FormatId? DetectIsoBmff(ReadOnlySpan<byte> box, string? path)
    {
        // box = the whole ftyp box: size, "ftyp", major brand, minor version, compatible brands.
        var size = (int)Math.Min(ReadUInt32BigEndian(box), (uint)box.Length);
        var major = Encoding.ASCII.GetString(box.Slice(8, 4));
        var compatible = new List<string>();
        for (var offset = 16; offset + 4 <= Math.Max(size, 16); offset += 4)
        {
            compatible.Add(Encoding.ASCII.GetString(box.Slice(offset, 4)));
        }

        // The major brand decides first. Only when it is generic (mif1, isom, ...) do the compatible brands
        // count, and the HEIF family wins there: a HEIC that lists "avif" as compatible stays HEIC, so it can
        // never be routed as AVIF.
        if (ClassifyBrand(major) is { } byMajor)
        {
            return byMajor;
        }
        if (compatible.Any(IsHeifBrand))
        {
            return FormatRegistry.Heic;
        }
        foreach (var brand in compatible)
        {
            if (ClassifyBrand(brand) is { } byCompatible)
            {
                return byCompatible;
            }
        }
        if (IsGenericHeifBrand(major) || compatible.Any(IsGenericHeifBrand))
        {
            return FormatRegistry.Heic;
        }
        // isom, mp41, mp42, avc1, dash, iso2 ... A .m4a with a generic brand is audio-only; ffprobe sorts that out later.
        return string.Equals(Path.GetExtension(path), ".m4a", StringComparison.OrdinalIgnoreCase) ? FormatRegistry.M4a : FormatRegistry.Mp4;
    }

    private static FormatId? ClassifyBrand(string brand)
    {
        if (IsHeifBrand(brand))
        {
            return FormatRegistry.Heic;
        }
        return brand switch
        {
            "avif" or "avis" => FormatRegistry.Avif,
            "qt  " => FormatRegistry.Mov,
            "M4A " or "M4B " => FormatRegistry.M4a,
            "crx " => FormatRegistry.Raw, // CR3
            _ when brand.StartsWith("3gp", StringComparison.Ordinal) || brand.StartsWith("3g2", StringComparison.Ordinal) => FormatRegistry.ThreeGp,
            _ => null,
        };
    }

    /// <summary>Brands that name HEVC-coded HEIF content.</summary>
    private static bool IsHeifBrand(string brand) =>
        brand is "heic" or "heix" or "hevc" or "hevx" or "heim" or "heis" or "hevm" or "hevs";

    /// <summary>Structural HEIF brands shared by HEIC and AVIF; they only decide when nothing more specific is present.</summary>
    private static bool IsGenericHeifBrand(string brand) => brand is "mif1" or "msf1";

    /// <summary>
    /// For ZIP-based files (OOXML): opens the archive and looks at [Content_Types].xml and the top-level folder.
    /// Returns null for a ZIP that is not an office document.
    /// </summary>
    public static FormatId? DetectZipBased(Stream stream)
    {
        try
        {
            using var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
            var names = zip.Entries.Select(e => e.FullName).ToList();
            if (!names.Contains("[Content_Types].xml"))
            {
                return null;
            }
            if (names.Any(n => n.StartsWith("word/", StringComparison.Ordinal)))
            {
                return FormatRegistry.Docx;
            }
            if (names.Any(n => n.StartsWith("xl/", StringComparison.Ordinal)))
            {
                return FormatRegistry.Xlsx;
            }
            if (names.Any(n => n.StartsWith("ppt/", StringComparison.Ordinal)))
            {
                return FormatRegistry.Pptx;
            }
            return null;
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    /// <summary>Plain text heuristics for files without a signature: UTF-8/UTF-16 BOM or mostly printable bytes.</summary>
    public static FormatId? DetectText(ReadOnlySpan<byte> sample, string? path)
    {
        if (sample.Length == 0)
        {
            return null;
        }
        var hasBom = StartsWith(sample, [0xEF, 0xBB, 0xBF]) || StartsWith(sample, [0xFF, 0xFE]) || StartsWith(sample, [0xFE, 0xFF]);
        if (!hasBom)
        {
            var control = 0;
            foreach (var b in sample)
            {
                if (b == 0 || (b < 0x20 && b is not (0x09 or 0x0A or 0x0D or 0x0C)))
                {
                    control++;
                }
            }
            if (control * 100 / sample.Length > 2)
            {
                return null;
            }
        }
        var ext = Path.GetExtension(path)?.TrimStart('.').ToLowerInvariant();
        return ext switch
        {
            "md" or "markdown" => FormatRegistry.Markdown,
            "html" or "htm" => FormatRegistry.Html,
            "csv" => FormatRegistry.Csv,
            "svg" => FormatRegistry.Svg,
            _ => LooksLikeHtml(sample) ? FormatRegistry.Html : FormatRegistry.Txt,
        };
    }

    private static bool LooksLikeSvg(ReadOnlySpan<byte> header)
    {
        var text = Encoding.ASCII.GetString(header).TrimStart('﻿', ' ', '\t', '\r', '\n');
        return text.StartsWith("<svg", StringComparison.OrdinalIgnoreCase) ||
               (text.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase) && text.Contains("svg", StringComparison.OrdinalIgnoreCase));
    }

    private static bool LooksLikeHtml(ReadOnlySpan<byte> sample)
    {
        var text = Encoding.ASCII.GetString(sample[..Math.Min(sample.Length, 256)]).TrimStart();
        return text.StartsWith("<!DOCTYPE html", StringComparison.OrdinalIgnoreCase) || text.StartsWith("<html", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasTextBom(ReadOnlySpan<byte> header) =>
        StartsWith(header, [0xEF, 0xBB, 0xBF]) || StartsWith(header, [0xFF, 0xFE]) || StartsWith(header, [0xFE, 0xFF]);

    /// <summary>MPEG audio frame header: 11 sync bits plus valid version, layer, bitrate and sample-rate fields.</summary>
    private static bool IsMp3FrameHeader(ReadOnlySpan<byte> header)
    {
        if (header.Length < 3 || header[0] != 0xFF || (header[1] & 0xE0) != 0xE0)
        {
            return false;
        }
        var version = (header[1] >> 3) & 0x03;
        var layer = (header[1] >> 1) & 0x03;
        var bitrateIndex = (header[2] >> 4) & 0x0F;
        var sampleRateIndex = (header[2] >> 2) & 0x03;
        return version != 0x01 && layer != 0 && bitrateIndex is not (0 or 0x0F) && sampleRateIndex != 0x03;
    }

    /// <summary>MPEG-TS: sync byte 0x47 at the start of three consecutive 188-byte packets.</summary>
    private static bool IsTransportStream(ReadOnlySpan<byte> sample) =>
        sample.Length > 2 * TsPacketLength
        && sample[0] == 0x47 && sample[TsPacketLength] == 0x47 && sample[2 * TsPacketLength] == 0x47;

    private static bool IsRawExtension(string? path)
    {
        var ext = Path.GetExtension(path)?.TrimStart('.').ToLowerInvariant();
        return ext is "dng" or "cr2" or "nef" or "arw" or "orf" or "raf" or "rw2";
    }

    private static bool StartsWith(ReadOnlySpan<byte> data, ReadOnlySpan<byte> prefix) =>
        data.Length >= prefix.Length && data[..prefix.Length].SequenceEqual(prefix);

    private static int IndexOf(ReadOnlySpan<byte> data, ReadOnlySpan<byte> needle) => data.IndexOf(needle);

    internal static uint ReadUInt32BigEndian(ReadOnlySpan<byte> span) => BinaryPrimitives.ReadUInt32BigEndian(span);
}
