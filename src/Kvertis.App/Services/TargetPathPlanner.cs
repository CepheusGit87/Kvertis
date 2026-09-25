using Kvertis.Engine.Formats;
using Kvertis.Engine.Naming;
using Kvertis.Queue;

namespace Kvertis.App.Services;

/// <summary>
/// Where one planned file will land, computed the same way <c>JobRunner</c> does it at start
/// (ADR-007: numbered, never overwritten).
/// </summary>
/// <param name="Item">The plan entry this preview belongs to.</param>
/// <param name="Location">Effective location: the file's own one, otherwise the shared one.</param>
/// <param name="IsOwnLocation">True when the user gave this file its own target ("Ändern").</param>
/// <param name="Directory">Absolute output directory, null when <paramref name="NeedsFolder"/> is true.</param>
/// <param name="FileName">Rendered file name including the extension, null when a folder is still missing.</param>
/// <param name="FullPath">Directory and file name, numbered against disk and the other previews of this round.</param>
/// <param name="NeedsFolder">The input is temporary (clipboard) or the folder is not usable: the user must pick one.</param>
/// <param name="BatchIndex">1-based index for the {n} token; null when the plan holds a single file.</param>
public sealed record TargetPathPreview(
    PlannedConversion Item,
    OutputLocation Location,
    bool IsOwnLocation,
    string? Directory,
    string? FileName,
    string? FullPath,
    bool NeedsFolder,
    int? BatchIndex);

/// <summary>
/// Target path preview for step 3 (ADR-021). Pure and free of any WinUI type, so it is tested from
/// <c>tests/Kvertis.App.Tests</c>. The final path still comes from the queue
/// (<see cref="JobChangeKind.Details"/>); this is the same arithmetic done one moment earlier.
/// </summary>
public static class TargetPathPlanner
{
    /// <summary>The file's own target if it has one, otherwise the target that holds for all files.</summary>
    public static OutputLocation EffectiveLocation(
        PlannedConversion item,
        OutputLocation shared,
        IReadOnlyDictionary<string, OutputLocation>? own)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(shared);
        return own is not null && own.TryGetValue(item.Input.Path, out var mine) && mine is not null ? mine : shared;
    }

    /// <summary>
    /// True when the input lives under <paramref name="temporaryRoot"/> (clipboard images) and the location is
    /// relative to it. "Next to the original" would drop the result into the app's cache, so step 3 asks for a
    /// folder before it lets the user start.
    /// </summary>
    public static bool NeedsFolder(PlannedConversion item, OutputLocation location, string? temporaryRoot)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(location);
        if (location is OutputLocation.CustomLocation custom)
        {
            return string.IsNullOrWhiteSpace(custom.Path);
        }
        if (string.IsNullOrWhiteSpace(temporaryRoot))
        {
            return false;
        }
        var directory = SafeDirectoryOf(item.Input.Path);
        return directory is not null && IsUnder(directory, temporaryRoot);
    }

    /// <summary>
    /// One preview per plan item, in plan order. The batch index is 1..n only when the plan has more than one
    /// item. Numbering ("_1", "_2") considers <paramref name="exists"/> (disk) and the other previews of this
    /// call, exactly like the queue's output path reserver does at start.
    /// </summary>
    public static IReadOnlyList<TargetPathPreview> PreviewAll(
        TargetPlan plan,
        OutputLocation shared,
        IReadOnlyDictionary<string, OutputLocation>? own,
        FormatRegistry registry,
        DateTimeOffset now,
        Func<string, bool> exists,
        string? temporaryRoot)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(shared);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(exists);

        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var previews = new List<TargetPathPreview>(plan.Items.Count);
        var multiple = plan.Items.Count > 1;

        for (var i = 0; i < plan.Items.Count; i++)
        {
            var item = plan.Items[i];
            var location = EffectiveLocation(item, shared, own);
            var isOwn = own is not null && own.ContainsKey(item.Input.Path);
            int? batchIndex = multiple ? i + 1 : null;

            if (NeedsFolder(item, location, temporaryRoot))
            {
                previews.Add(new TargetPathPreview(item, location, isOwn, null, null, null, true, batchIndex));
                continue;
            }

            string directory;
            try
            {
                directory = OutputDirectoryResolver.Resolve(item.Input.Path, location);
            }
            catch (ArgumentException)
            {
                // Empty custom folder or an unusable sub folder name: the user has to choose one.
                previews.Add(new TargetPathPreview(item, location, isOwn, null, null, null, true, batchIndex));
                continue;
            }

            var fileName = OutputNamePattern.Render(
                item.NamePattern, item.Input.Path, registry.ExtensionFor(item.Settings.Output), now, batchIndex);
            var fullPath = OutputNamePattern.EnsureUnique(directory, fileName, p => taken.Contains(p) || exists(p));
            taken.Add(fullPath);
            previews.Add(new TargetPathPreview(
                item, location, isOwn, directory, Path.GetFileName(fullPath), fullPath, false, batchIndex));
        }

        return previews;
    }

    private static string? SafeDirectoryOf(string path)
    {
        try
        {
            return Path.GetDirectoryName(Path.GetFullPath(path));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private static bool IsUnder(string directory, string root)
    {
        string full;
        try
        {
            full = Path.GetFullPath(root);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
        var candidate = directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        full = full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return candidate.Equals(full, StringComparison.OrdinalIgnoreCase)
               || candidate.StartsWith(full + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}
