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
    /// <summary>Bytes needed to classify anything we know. Read at most this much.</summary>
    public const int HeaderLength = 64;

    public static FormatId? Detect(ReadOnlySpan<byte> header, string? path = null)
    {
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
        if (StartsWith(header, "ID3"u8) || (header[0] == 0xFF && (header[1] & 0xE0) == 0xE0 && (header[1] & 0x06) != 0))
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
        if (header[0] == 0x47 && header.Length >= 8 && (header.Length < 189 || header[188] == 0x47))
        {
            return FormatRegistry.Ts;
        }
        if (header.Length >= 12 && header.Slice(4, 4).SequenceEqual("ftyp"u8))
        {
            return DetectIsoBmff(header.Slice(8), path);
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

    private static FormatId? DetectIsoBmff(ReadOnlySpan<byte> afterFtyp, string? path)
    {
        // Major brand, then compatible brands. Scan all four-char codes we have.
        var brands = Encoding.ASCII.GetString(afterFtyp);
        if (brands.Contains("heic", StringComparison.Ordinal) || brands.Contains("heix", StringComparison.Ordinal) ||
            brands.Contains("hevc", StringComparison.Ordinal) || brands.Contains("mif1", StringComparison.Ordinal) ||
            brands.Contains("msf1", StringComparison.Ordinal) || brands.Contains("heim", StringComparison.Ordinal))
        {
            return brands.Contains("avif", StringComparison.Ordinal) ? FormatRegistry.Avif : FormatRegistry.Heic;
        }
        if (brands.Contains("avif", StringComparison.Ordinal) || brands.Contains("avis", StringComparison.Ordinal))
        {
            return FormatRegistry.Avif;
        }
        if (brands.Contains("qt  ", StringComparison.Ordinal))
        {
            return FormatRegistry.Mov;
        }
        if (brands.Contains("3gp", StringComparison.Ordinal) || brands.Contains("3g2", StringComparison.Ordinal))
        {
            return FormatRegistry.ThreeGp;
        }
        if (brands.Contains("M4A ", StringComparison.Ordinal) || brands.Contains("M4B ", StringComparison.Ordinal))
        {
            return FormatRegistry.M4a;
        }
        if (brands.Contains("crx ", StringComparison.Ordinal))
        {
            return FormatRegistry.Raw; // CR3
        }
        // isom, mp41, mp42, avc1, dash, iso2 ... A .m4a with a generic brand is audio-only; ffprobe sorts that out later.
        return string.Equals(Path.GetExtension(path), ".m4a", StringComparison.OrdinalIgnoreCase) ? FormatRegistry.M4a : FormatRegistry.Mp4;
    }

    /// <summary>
    /// For ZIP-based files (OOXML): opens the archive and looks at [Content_Types].xml and the top-level folder.
    /// Returns null for a ZIP that is not an Office document.
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
