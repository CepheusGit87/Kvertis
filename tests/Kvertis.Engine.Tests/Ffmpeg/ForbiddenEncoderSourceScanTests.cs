using Shouldly;
using Xunit;

namespace Kvertis.Engine.Tests.Ffmpeg;

/// <summary>
/// Legal guard (docs/02-rechtssicherheit.md §2/§3): the names of the GPL/non-free encoders must never
/// appear in shipped source code, so nothing can ever select them, not even as a fallback.
/// </summary>
public class ForbiddenEncoderSourceScanTests
{
    // Assembled from parts so this test file does not trip other literal scans.
    private static readonly string[] Forbidden = ["lib" + "x264", "lib" + "x265", "lib" + "fdk_aac"];

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
