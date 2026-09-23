using Kvertis.Engine.Abstractions;
using PdfSharp.Fonts;

namespace Kvertis.Engine.Conversion.Documents;

/// <summary>
/// Fonts for PDF output. Kvertis ships no fonts; it uses fonts installed on the system (see
/// docs/02-rechtssicherheit.md §6). The PDF library needs a resolver on every platform, so this maps
/// two logical families ("sans" for text, "mono" for plain text and code) to font files found in
/// the usual system font folders. Missing bold/italic faces are simulated.
/// </summary>
internal static class DocumentFonts
{
    public const string Sans = "Kvertis Sans";
    public const string Mono = "Kvertis Mono";

    private static readonly object Gate = new();
    private static readonly Lazy<SystemFontResolver> LazyResolver = new(SystemFontResolver.Discover);
    private static bool _registered;

    /// <summary>True when at least one usable font file was found.</summary>
    public static bool IsAvailable => LazyResolver.Value.HasFonts;

    /// <summary>Registers the resolver once. Throws MissingSystemCodec (detail "font") when no font exists.</summary>
    public static void EnsureInitialized(string? filePath = null)
    {
        var resolver = LazyResolver.Value;
        if (!resolver.HasFonts)
        {
            throw new ConversionException(ConversionErrorCode.MissingSystemCodec, filePath, "pdf-fonts", "font");
        }
        lock (Gate)
        {
            if (_registered)
            {
                return;
            }
            if (GlobalFontSettings.FontResolver is null)
            {
                GlobalFontSettings.FontResolver = resolver;
            }
            else if (GlobalFontSettings.FallbackFontResolver is null)
            {
                GlobalFontSettings.FallbackFontResolver = resolver;
            }
            _registered = true;
        }
    }
}

internal sealed class SystemFontResolver : IFontResolver
{
    // Font file names only (no font data is shipped). Free fonts common on Linux first, then the
    // Windows system fonts that docs/02-rechtssicherheit.md allows to be referenced.
    private static readonly string[] SansRegular = ["DejaVuSans.ttf", "LiberationSans-Regular.ttf", "NotoSans-Regular.ttf", "FreeSans.ttf", "segoeui.ttf"];
    private static readonly string[] SansBold = ["DejaVuSans-Bold.ttf", "LiberationSans-Bold.ttf", "NotoSans-Bold.ttf", "FreeSansBold.ttf", "segoeuib.ttf"];
    private static readonly string[] SansItalic = ["DejaVuSans-Oblique.ttf", "LiberationSans-Italic.ttf", "NotoSans-Italic.ttf", "FreeSansOblique.ttf", "segoeuii.ttf"];
    private static readonly string[] SansBoldItalic = ["DejaVuSans-BoldOblique.ttf", "LiberationSans-BoldItalic.ttf", "NotoSans-BoldItalic.ttf", "FreeSansBoldOblique.ttf", "segoeuiz.ttf"];
    private static readonly string[] MonoRegular = ["DejaVuSansMono.ttf", "LiberationMono-Regular.ttf", "NotoSansMono-Regular.ttf", "FreeMono.ttf"];
    private static readonly string[] MonoBold = ["DejaVuSansMono-Bold.ttf", "LiberationMono-Bold.ttf", "NotoSansMono-Bold.ttf", "FreeMonoBold.ttf"];

    private readonly Dictionary<string, byte[]> _cache = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    private sealed record Family(string? Regular, string? Bold, string? Italic, string? BoldItalic);

    private readonly Family _sans;
    private readonly Family _mono;

    private SystemFontResolver(Family sans, Family mono)
    {
        _sans = sans;
        _mono = mono;
    }

    public bool HasFonts => _sans.Regular is not null || _mono.Regular is not null;

    public static SystemFontResolver Discover()
    {
        var index = IndexFontFiles();
        string? Find(string[] names) => names.Select(n => index.GetValueOrDefault(n)).FirstOrDefault(p => p is not null);

        var sans = new Family(Find(SansRegular), Find(SansBold), Find(SansItalic), Find(SansBoldItalic));
        var mono = new Family(Find(MonoRegular), Find(MonoBold), null, null);

        // Last resort: any TrueType file, so text output still works on unusual systems.
        var any = sans.Regular ?? mono.Regular ?? index.Values.FirstOrDefault(p => p.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase));
        if (sans.Regular is null)
        {
            sans = new Family(any, null, null, null);
        }
        if (mono.Regular is null)
        {
            mono = sans;
        }
        return new SystemFontResolver(sans, mono);
    }

    public FontResolverInfo? ResolveTypeface(string familyName, bool isBold, bool isItalic)
    {
        var family = string.Equals(familyName, DocumentFonts.Mono, StringComparison.OrdinalIgnoreCase) ? _mono : _sans;
        if (family.Regular is null)
        {
            return null;
        }
        if (isBold && isItalic && family.BoldItalic is not null)
        {
            return new FontResolverInfo(family.BoldItalic);
        }
        if (isBold && family.Bold is not null)
        {
            return new FontResolverInfo(family.Bold, false, isItalic);
        }
        if (isItalic && family.Italic is not null)
        {
            return new FontResolverInfo(family.Italic, isBold, false);
        }
        return new FontResolverInfo(family.Regular, isBold, isItalic);
    }

    public byte[]? GetFont(string faceName)
    {
        lock (_gate)
        {
            if (!_cache.TryGetValue(faceName, out var data))
            {
                data = File.ReadAllBytes(faceName);
                _cache[faceName] = data;
            }
            return data;
        }
    }

    private static Dictionary<string, string> IndexFontFiles()
    {
        var index = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, MaxRecursionDepth = 6 };
        foreach (var directory in FontDirectories())
        {
            try
            {
                if (!Directory.Exists(directory))
                {
                    continue;
                }
                foreach (var file in Directory.EnumerateFiles(directory, "*.ttf", options))
                {
                    index.TryAdd(Path.GetFileName(file), file);
                }
            }
            catch (IOException)
            {
                // Unreadable folder: skip.
            }
            catch (UnauthorizedAccessException)
            {
                // Unreadable folder: skip.
            }
        }
        return index;
    }

    private static IEnumerable<string> FontDirectories()
    {
        if (OperatingSystem.IsWindows())
        {
            yield return Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
            yield break;
        }
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        yield return "/usr/share/fonts";
        yield return "/usr/local/share/fonts";
        yield return Path.Combine(home, ".local", "share", "fonts");
        yield return Path.Combine(home, ".fonts");
        yield return "/Library/Fonts";
        yield return "/System/Library/Fonts";
    }
}
