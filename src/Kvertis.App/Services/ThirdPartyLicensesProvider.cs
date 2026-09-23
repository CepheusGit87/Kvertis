namespace Kvertis.App.Services;

/// <summary>One third-party component with its license text.</summary>
public sealed record ThirdPartyLicense(string Name, string Text);

/// <summary>
/// Reads the license texts packaged under ThirdParty/&lt;Component&gt;/ (copied from the repository's third_party/
/// folder by the project file). Local files only.
/// </summary>
public sealed class ThirdPartyLicensesProvider
{
    private readonly string _root;

    public ThirdPartyLicensesProvider()
        : this(AppPaths.ThirdPartyFolder)
    {
    }

    public ThirdPartyLicensesProvider(string root)
    {
        _root = root;
    }

    public async Task<IReadOnlyList<ThirdPartyLicense>> LoadAsync(CancellationToken ct = default)
    {
        if (!Directory.Exists(_root))
        {
            return [];
        }

        var result = new List<ThirdPartyLicense>();
        foreach (var directory in Directory.EnumerateDirectories(_root).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
        {
            var parts = new List<string>();
            foreach (var file in Directory.EnumerateFiles(directory).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    parts.Add(await File.ReadAllTextAsync(file, ct).ConfigureAwait(false));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Skip unreadable files; the remaining texts are still shown.
                }
            }
            if (parts.Count > 0)
            {
                result.Add(new ThirdPartyLicense(Path.GetFileName(directory), string.Join(Environment.NewLine + Environment.NewLine, parts)));
            }
        }
        return result;
    }
}
