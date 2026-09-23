using Shouldly;
using Xunit;

namespace Kvertis.Engine.Tests.Images;

/// <summary>
/// Legal guard (decision 2026-09-23, zero patent exposure): the ImageMagick wrapper was removed because its
/// native library embeds HEVC/H.264 decoders. Neither its namespace nor its packages may come back in src/
/// or in the central package list.
/// </summary>
public class NoImageMagickSourceScanTests
{
    // Assembled from parts so this file does not trip its own or other literal scans.
    private static readonly string[] Forbidden = ["using " + "ImageMagick", "Image" + "Magick.", "Magick" + ".NET"];

    private static readonly string[] Extensions = [".cs", ".csproj", ".props", ".targets"];

    [Fact]
    public void Source_tree_does_not_reference_the_removed_image_library()
    {
        var root = FindRepositoryRoot();
        var src = Path.Combine(root, "src");
        var hits = new List<string>();
        var scanned = 0;
        var files = Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories)
            .Append(Path.Combine(root, "Directory.Packages.props"));
        foreach (var file in files)
        {
            var relative = Path.GetRelativePath(root, file);
            var parts = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (parts.Contains("bin") || parts.Contains("obj") || !Extensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }
            scanned++;
            var text = File.ReadAllText(file);
            hits.AddRange(Forbidden.Where(f => text.Contains(f, StringComparison.Ordinal)).Select(f => $"{relative}: {f}"));
        }

        scanned.ShouldBeGreaterThan(10);
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
