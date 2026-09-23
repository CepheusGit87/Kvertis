using Kvertis.Engine.Abstractions;
using Kvertis.Engine.Naming;

namespace Kvertis.Queue;

/// <summary>Convenience for the app: detect a file and build a job for it in one step.</summary>
public static class ConversionJobFactory
{
    /// <summary>
    /// Detects <paramref name="path"/> with <paramref name="detector"/> and creates a job whose output
    /// directory follows <paramref name="location"/>. Validation happens later, when the job starts.
    /// </summary>
    public static async Task<ConversionJob> CreateAsync(
        IFormatDetector detector,
        string path,
        ConversionSettings settings,
        OutputLocation location,
        string namePattern = OutputNamePattern.Default,
        int? batchIndex = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(detector);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(location);

        var input = await detector.DetectAsync(path, ct).ConfigureAwait(false);
        var directory = OutputDirectoryResolver.Resolve(input.Path, location);
        return new ConversionJob(input, settings, directory, namePattern, batchIndex);
    }
}
