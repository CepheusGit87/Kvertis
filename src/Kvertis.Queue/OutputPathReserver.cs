using Kvertis.Engine.IO;
using Kvertis.Engine.Naming;

namespace Kvertis.Queue;

/// <summary>
/// Resolves free output paths for starting jobs. Paths handed out stay reserved until the job ends,
/// so two jobs that start at the same moment with the same name get "name.ext" and "name_1.ext"
/// even though neither file exists on disk yet (ADR-007: never overwrite).
/// </summary>
internal sealed class OutputPathReserver
{
    // Case-insensitive on every platform: Windows is the target, and a false "taken" only adds a suffix.
    private readonly HashSet<string> _reserved = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    public string Reserve(string directory, string fileName)
    {
        lock (_gate)
        {
            var path = OutputNamePattern.EnsureUnique(directory, fileName, IsTaken);
            _reserved.Add(path);
            return path;
        }
    }

    public void Release(string? path)
    {
        if (path is null)
        {
            return;
        }
        lock (_gate)
        {
            _reserved.Remove(path);
        }
    }

    private bool IsTaken(string path) =>
        _reserved.Contains(path)
        || File.Exists(path)
        || Directory.Exists(path)
        || File.Exists(path + ConversionOutput.TempSuffix);
}
