using Shouldly;
using Xunit;

namespace Kvertis.Engine.Tests.Ffmpeg;

/// <summary>
/// Legal guard (docs/02-rechtssicherheit.md §2/§3): the names of the GPL/non-free encoders must never
/// appear in shipped source code, so nothing can ever select them, not even as a fallback. The names of the
/// patent-encumbered decoders (ADR-015, list of tools/ffmpeg/check-build.sh) must never appear as plain string
/// literals (<c>"h264"</c>); code that needs them assembles them from parts in one place.
/// </summary>
public class ForbiddenEncoderSourceScanTests
{
    // Assembled from parts so this test file does not trip other literal scans.
    private static readonly string[] Forbidden = ["lib" + "x264", "lib" + "x265", "lib" + "fdk_aac"];

    // Same list as tools/ffmpeg/check-build.sh, assembled from parts for the same reason.
    private static readonly string[] ForbiddenDecoderLiterals =
    [
        "h26" + "4", "he" + "vc", "aa" + "c", "aa" + "c_fixed", "aa" + "c_latm", "mpeg" + "4",
        "msmpeg4" + "v1", "msmpeg4" + "v2", "msmpeg4" + "v3", "wm" + "v1", "wm" + "v2", "wm" + "v3", "vc" + "1",
        "wma" + "v1", "wma" + "v2", "wma" + "pro", "wma" + "lossless", "wma" + "voice", "h26" + "3", "pro" + "res",
        "dnx" + "hd", "ea" + "c3", "dc" + "a", "true" + "hd", "amr" + "nb", "amr" + "wb",
        "h26" + "4_qsv", "he" + "vc_qsv", "h26" + "4_cuvid", "he" + "vc_cuvid",
    ];

    // Literals that are not codec selectors: the HEIF brand "hevc" in the ISO-BMFF brand check and the ".aac"
    // file extension of the M4A format. File (relative to src/) and literal.
    private static readonly (string File, string Literal)[] AllowedDecoderLiterals =
    [
        ("Kvertis.Engine/Formats/MagicBytes.cs", "he" + "vc"),
        ("Kvertis.Engine/Formats/FormatRegistry.cs", "aa" + "c"),
    ];

    private static readonly string[] Extensions = [".cs", ".csproj", ".props", ".targets", ".json", ".xaml", ".resw", ".xml", ".ps1", ".cmd", ".bat"];

    [Fact]
    public void SourceTreeDoesNotMentionForbiddenEncoders()
    {
        var src = Path.Combine(FindRepositoryRoot(), "src");
        Directory.Exists(Path.Combine(src, "Kvertis.Engine")).ShouldBeTrue();

        var hits = new List<string>();
        var scanned = 0;
        foreach (var file in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(src, file);
            var parts = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (parts.Contains("bin") || parts.Contains("obj") || !Extensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }
            scanned++;
            var text = File.ReadAllText(file);
            hits.AddRange(Forbidden.Where(f => text.Contains(f, StringComparison.OrdinalIgnoreCase)).Select(f => $"{relative}: {f}"));
        }

        scanned.ShouldBeGreaterThan(10);
        hits.ShouldBeEmpty();
    }

    [Fact]
    public void SourceTreeHasNoEncumberedDecoderLiterals()
    {
        var src = Path.Combine(FindRepositoryRoot(), "src");
        var hits = new List<string>();
        foreach (var file in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(src, file).Replace(Path.DirectorySeparatorChar, '/');
            var parts = relative.Split('/');
            if (parts.Contains("bin") || parts.Contains("obj") || !Extensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }
            var text = File.ReadAllText(file);
            foreach (var name in ForbiddenDecoderLiterals)
            {
                if (text.Contains('"' + name + '"', StringComparison.OrdinalIgnoreCase)
                    && !AllowedDecoderLiterals.Contains((relative, name)))
                {
                    hits.Add($"{relative}: \"{name}\"");
                }
            }
        }

        hits.ShouldBeEmpty();
    }

    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Kvertis.sln")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? throw new InvalidOperationException("Kvertis.sln not found above " + AppContext.BaseDirectory);
    }
}
