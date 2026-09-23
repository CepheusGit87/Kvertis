using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Kvertis.Engine.Naming;

/// <summary>
/// Builds output file names from a user pattern such as "{name}_{date}_{format}".
/// Supported tokens: {name} (input name without extension), {date} (yyyy-MM-dd), {time} (HH-mm-ss),
/// {format} (target extension), {n} (batch index, 1-based). Unknown tokens are kept verbatim.
/// Never overwrites: when the target exists, "_1", "_2", ... is appended.
/// </summary>
public static partial class OutputNamePattern
{
    public const string Default = "{name}";
    public const string DefaultWithDateAndFormat = "{name}_{date}_{format}";

    private static readonly char[] InvalidChars = Path.GetInvalidFileNameChars();

    public static string Render(string pattern, string inputPath, string targetExtension, DateTimeOffset now, int? batchIndex = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetExtension);
        pattern = string.IsNullOrWhiteSpace(pattern) ? Default : pattern;

        var ext = targetExtension.TrimStart('.');
        var name = Path.GetFileNameWithoutExtension(inputPath);
        var rendered = TokenRegex().Replace(pattern, m => m.Groups[1].Value.ToLowerInvariant() switch
        {
            "name" => name,
            "date" => now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            "time" => now.ToString("HH-mm-ss", CultureInfo.InvariantCulture),
            "format" => ext,
            "n" => batchIndex?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            _ => m.Value,
        });

        rendered = Sanitize(rendered);
        if (rendered.Length == 0)
        {
            rendered = Sanitize(name);
        }
        return rendered + "." + ext;
    }

    /// <summary>Returns a path inside <paramref name="directory"/> that does not exist yet.</summary>
    public static string EnsureUnique(string directory, string fileName, Func<string, bool>? exists = null)
    {
        exists ??= File.Exists;
        var candidate = Path.Combine(directory, fileName);
        if (!exists(candidate))
        {
            return candidate;
        }
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var ext = Path.GetExtension(fileName);
        for (var i = 1; i < 10_000; i++)
        {
            candidate = Path.Combine(directory, $"{stem}_{i}{ext}");
            if (!exists(candidate))
            {
                return candidate;
            }
        }
        throw new IOException($"Could not find a free file name for '{fileName}' in '{directory}'.");
    }

    private static string Sanitize(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            sb.Append(Array.IndexOf(InvalidChars, c) >= 0 ? '_' : c);
        }
        // Windows forbids trailing dots and spaces in file names.
        var result = sb.ToString().TrimEnd('.', ' ');
        return IsReservedDeviceName(result) ? "_" + result : result;
    }

    /// <summary>
    /// Windows device names (CON, PRN, AUX, NUL, COM1-9, LPT1-9) are reserved in any case and even with an
    /// extension ("nul.txt", "Com1.backup"). The part before the first dot decides.
    /// </summary>
    internal static bool IsReservedDeviceName(string stem)
    {
        var dot = stem.IndexOf('.', StringComparison.Ordinal);
        var head = (dot >= 0 ? stem[..dot] : stem).TrimEnd(' ').ToUpperInvariant();
        return head switch
        {
            "CON" or "PRN" or "AUX" or "NUL" => true,
            _ => head.Length == 4 && (head.StartsWith("COM", StringComparison.Ordinal) || head.StartsWith("LPT", StringComparison.Ordinal))
                 && head[3] is >= '1' and <= '9',
        };
    }

    [GeneratedRegex(@"\{(\w+)\}")]
    private static partial Regex TokenRegex();
}
