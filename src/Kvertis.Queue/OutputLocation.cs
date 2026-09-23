namespace Kvertis.Queue;

/// <summary>Where converted files go, relative to each input file or as a fixed folder.</summary>
public abstract record OutputLocation
{
    private OutputLocation()
    {
    }

    /// <summary>Default sub folder name for <see cref="InSubFolder"/>.</summary>
    public const string DefaultSubFolderName = "Kvertis";

    /// <summary>Next to the input file.</summary>
    public static OutputLocation SameFolder { get; } = new SameFolderLocation();

    /// <summary>In a sub folder of the input file's folder.</summary>
    public static OutputLocation SubFolder(string name = DefaultSubFolderName) => new SubFolderLocation(name);

    /// <summary>A fixed folder chosen by the user.</summary>
    public static OutputLocation Custom(string path) => new CustomLocation(path);

    public sealed record SameFolderLocation : OutputLocation;

    public sealed record SubFolderLocation(string Name) : OutputLocation;

    public sealed record CustomLocation(string Path) : OutputLocation;
}

/// <summary>Turns an <see cref="OutputLocation"/> into an absolute directory for one input file.</summary>
public static class OutputDirectoryResolver
{
    /// <summary>Resolves the output directory. Does not create it; the queue creates it when the job starts.</summary>
    /// <exception cref="ArgumentException">Sub folder name is empty, rooted, contains separators, ".." or invalid characters.</exception>
    public static string Resolve(string inputPath, OutputLocation option)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);
        ArgumentNullException.ThrowIfNull(option);

        var inputDirectory = Path.GetDirectoryName(Path.GetFullPath(inputPath))
                             ?? throw new ArgumentException("Input path has no directory.", nameof(inputPath));

        return option switch
        {
            OutputLocation.SameFolderLocation => inputDirectory,
            OutputLocation.SubFolderLocation sub => Path.Combine(inputDirectory, ValidateFolderName(sub.Name)),
            OutputLocation.CustomLocation custom when !string.IsNullOrWhiteSpace(custom.Path) => Path.GetFullPath(custom.Path),
            OutputLocation.CustomLocation => throw new ArgumentException("Custom output folder is empty.", nameof(option)),
            _ => throw new ArgumentOutOfRangeException(nameof(option), option, "Unknown output location."),
        };
    }

    private static string ValidateFolderName(string name)
    {
        // Windows drops trailing dots and spaces, so "..." would silently become the parent folder.
        var trimmed = (name ?? string.Empty).Trim().TrimEnd('.', ' ');
        if (trimmed.Length == 0
            || trimmed.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || trimmed.Contains('/', StringComparison.Ordinal)
            || trimmed.Contains('\\', StringComparison.Ordinal))
        {
            throw new ArgumentException($"Invalid sub folder name '{name}'.", nameof(name));
        }
        return trimmed;
    }
}
