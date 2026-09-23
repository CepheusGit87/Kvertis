using Kvertis.Engine.Abstractions;

namespace Kvertis.Engine.Ffmpeg;

/// <summary>
/// Finds ffmpeg/ffprobe: an explicit user path first (LGPL replacement right, see docs/02 §2), then the
/// bundled copy next to the application, then PATH as a development convenience. Never downloads anything.
/// </summary>
public sealed class FfmpegLocator : IFfmpegLocator
{
    private readonly Lazy<(string? Ffmpeg, string? Ffprobe)> _paths;

    public FfmpegLocator(string? userDirectory = null, string? bundledDirectory = null)
    {
        _paths = new Lazy<(string?, string?)>(() => Locate(userDirectory, bundledDirectory));
    }

    public string? FfmpegPath => _paths.Value.Ffmpeg;
    public string? FfprobePath => _paths.Value.Ffprobe;

    private static (string?, string?) Locate(string? userDirectory, string? bundledDirectory)
    {
        var exe = OperatingSystem.IsWindows() ? ".exe" : string.Empty;
        foreach (var dir in Candidates(userDirectory, bundledDirectory))
        {
            var ffmpeg = Path.Combine(dir, "ffmpeg" + exe);
            var ffprobe = Path.Combine(dir, "ffprobe" + exe);
            if (File.Exists(ffmpeg) && File.Exists(ffprobe))
            {
                return (ffmpeg, ffprobe);
            }
        }
        return (null, null);
    }

    private static IEnumerable<string> Candidates(string? userDirectory, string? bundledDirectory)
    {
        if (!string.IsNullOrWhiteSpace(userDirectory))
        {
            yield return userDirectory;
        }
        if (!string.IsNullOrWhiteSpace(bundledDirectory))
        {
            yield return bundledDirectory;
        }
        var baseDir = AppContext.BaseDirectory;
        var arch = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();
        yield return Path.Combine(baseDir, "Tools", "ffmpeg", arch);
        yield return Path.Combine(baseDir, "Tools", "ffmpeg");
        yield return Path.Combine(baseDir, "ffmpeg");
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            yield return dir;
        }
    }
}
