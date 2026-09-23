using ImageMagick;
using Kvertis.Engine.Abstractions;
using Kvertis.Engine.Formats;

namespace Kvertis.Engine.Conversion.Images;

/// <summary>
/// Shared Magick.NET glue for the image converter and the image prober: format mapping, read settings
/// and exception translation. Every read passes an explicit coder so Magick.NET never picks a coder by
/// sniffing the content on its own (in particular never its HEIC delegate, see ADR-006).
/// </summary>
internal static class MagickSupport
{
    private static readonly Dictionary<FormatId, MagickFormat> ReadFormats = new()
    {
        [FormatRegistry.Jpg] = MagickFormat.Jpeg,
        [FormatRegistry.Png] = MagickFormat.Png,
        [FormatRegistry.WebP] = MagickFormat.WebP,
        [FormatRegistry.Gif] = MagickFormat.Gif,
        [FormatRegistry.Bmp] = MagickFormat.Bmp,
        [FormatRegistry.Tiff] = MagickFormat.Tiff,
        [FormatRegistry.Avif] = MagickFormat.Avif,
        [FormatRegistry.Svg] = MagickFormat.Svg,
        [FormatRegistry.Ico] = MagickFormat.Ico,
        [FormatRegistry.Psd] = MagickFormat.Psd,
    };

    private static readonly Dictionary<FormatId, MagickFormat> WriteFormats = new()
    {
        [FormatRegistry.Jpg] = MagickFormat.Jpeg,
        [FormatRegistry.Png] = MagickFormat.Png,
        [FormatRegistry.WebP] = MagickFormat.WebP,
        [FormatRegistry.Gif] = MagickFormat.Gif,
        [FormatRegistry.Bmp] = MagickFormat.Bmp,
        [FormatRegistry.Tiff] = MagickFormat.Tiff,
        [FormatRegistry.Ico] = MagickFormat.Ico,
    };

    /// <summary>
    /// Coder used to read a detected format. Returns null for formats Magick.NET must never read
    /// (HEIC goes through IHeicDecoder) or does not know.
    /// </summary>
    public static MagickFormat? ReadFormatFor(FormatId format, string? path = null)
    {
        if (format == FormatRegistry.Raw)
        {
            return RawFormatFor(path);
        }
        return ReadFormats.TryGetValue(format, out var coder) ? coder : null;
    }

    /// <summary>Coder used to write an output format. Null when the image converter does not write it.</summary>
    public static MagickFormat? WriteFormatFor(FormatId format) =>
        WriteFormats.TryGetValue(format, out var coder) ? coder : null;

    public static MagickReadSettings ReadSettings(FormatId format, string path, bool firstFrameOnly)
    {
        MagickSecurity.EnsureInitialized();
        var coder = ReadFormatFor(format, path)
                    ?? throw new ConversionException(ConversionErrorCode.UnsupportedFormat, path, "read", $"no image reader for '{format}'");
        var settings = new MagickReadSettings { Format = coder };
        if (firstFrameOnly)
        {
            settings.FrameIndex = 0;
            settings.FrameCount = 1;
        }
        return settings;
    }

    /// <summary>Translates a Magick.NET failure into the engine's error codes.</summary>
    public static ConversionException Translate(Exception ex, string? path, string step) => ex switch
    {
        ConversionException ce => ce,
        MagickCorruptImageErrorException or MagickMissingDelegateErrorException or MagickCoderErrorException
            => new ConversionException(ConversionErrorCode.CorruptFile, path, step, ex.Message, ex),
        _ => ConversionException.From(ex, path, step),
    };

    private static MagickFormat RawFormatFor(string? path)
    {
        var ext = Path.GetExtension(path)?.TrimStart('.').ToLowerInvariant();
        return ext switch
        {
            "cr2" => MagickFormat.Cr2,
            "cr3" => MagickFormat.Cr3,
            "nef" => MagickFormat.Nef,
            "arw" => MagickFormat.Arw,
            "orf" => MagickFormat.Orf,
            "raf" => MagickFormat.Raf,
            "rw2" => MagickFormat.Rw2,
            _ => MagickFormat.Dng,
        };
    }
}
