using Kvertis.Engine.Abstractions;

namespace Kvertis.Engine.IO;

/// <summary>
/// Atomic output handling (ADR-007): converters write to "&lt;target&gt;.kvertis-tmp" and commit by rename.
/// Also checks free disk space before work starts and cleans up leftovers.
/// </summary>
public sealed class ConversionOutput : IDisposable
{
    public const string TempSuffix = ".kvertis-tmp";

    private readonly bool _allowOverwrite;
    private bool _committed;

    public string FinalPath { get; }
    public string TempPath { get; }

    private ConversionOutput(string finalPath, bool allowOverwrite)
    {
        FinalPath = finalPath;
        TempPath = finalPath + TempSuffix;
        _allowOverwrite = allowOverwrite;
    }

    /// <summary>Prepares the target: directory exists, target is free, temp leftover removed, enough space.</summary>
    public static ConversionOutput Begin(string finalPath, long expectedBytes, bool allowOverwrite = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(finalPath);
        var directory = Path.GetDirectoryName(Path.GetFullPath(finalPath))
                        ?? throw new ConversionException(ConversionErrorCode.OutputNotWritable, finalPath, "prepare");
        try
        {
            Directory.CreateDirectory(directory);
        }
        catch (Exception ex)
        {
            throw new ConversionException(ConversionErrorCode.OutputNotWritable, finalPath, "prepare", ex.Message, ex);
        }

        if (File.Exists(finalPath) && !allowOverwrite)
        {
            throw new ConversionException(ConversionErrorCode.OutputExists, finalPath, "prepare");
        }

        var free = FreeBytes(directory);
        if (free is { } f && expectedBytes > 0 && f < expectedBytes + 32L * 1024 * 1024)
        {
            throw new ConversionException(ConversionErrorCode.InsufficientDiskSpace, finalPath, "prepare", $"free={f}, needed={expectedBytes}");
        }

        var output = new ConversionOutput(finalPath, allowOverwrite);
        TryDelete(output.TempPath);
        return output;
    }

    /// <summary>
    /// Renames the temp file onto the final path. Throws if the converter produced nothing, and with
    /// OutputExists when the target appeared during the conversion and <c>allowOverwrite</c> was not given
    /// to <see cref="Begin"/> (ADR-007: never overwrite without consent).
    /// </summary>
    public long Commit()
    {
        var info = new FileInfo(TempPath);
        if (!info.Exists || info.Length == 0)
        {
            TryDelete(TempPath);
            throw new ConversionException(ConversionErrorCode.ToolFailed, FinalPath, "commit", "no output produced");
        }
        if (!_allowOverwrite && File.Exists(FinalPath))
        {
            TryDelete(TempPath);
            throw new ConversionException(ConversionErrorCode.OutputExists, FinalPath, "commit", "target appeared during conversion");
        }
        try
        {
            File.Move(TempPath, FinalPath, overwrite: _allowOverwrite);
        }
        catch (IOException ex) when (!_allowOverwrite && File.Exists(FinalPath))
        {
            // Lost the race: the target was created between the check and the move.
            TryDelete(TempPath);
            throw new ConversionException(ConversionErrorCode.OutputExists, FinalPath, "commit", ex.Message, ex);
        }
        catch (Exception ex)
        {
            TryDelete(TempPath);
            throw ConversionException.From(ex, FinalPath, "commit");
        }
        _committed = true;
        return info.Length;
    }

    public void Dispose()
    {
        if (!_committed)
        {
            TryDelete(TempPath);
        }
    }

    /// <summary>Deletes stale temp files in a folder, e.g. after a crash. Best effort.</summary>
    public static int CleanupLeftovers(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return 0;
        }
        var count = 0;
        // Matches "<name>.kvertis-tmp" and helper files such as ".kvertis-heic-<guid>.png".
        foreach (var file in Directory.EnumerateFiles(directory, "*.kvertis-*"))
        {
            if (TryDelete(file))
            {
                count++;
            }
        }
        return count;
    }

    public static long? FreeBytes(string directory)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(directory));
            if (string.IsNullOrEmpty(root))
            {
                return null;
            }
            return new DriveInfo(root).AvailableFreeSpace;
        }
        catch (Exception)
        {
            return null; // Network paths and odd mounts: skip the check rather than block the user.
        }
    }

    private static bool TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
                return true;
            }
        }
        catch (Exception)
        {
            // Locked or already gone.
        }
        return false;
    }
}
