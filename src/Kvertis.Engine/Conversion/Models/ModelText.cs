using System.Globalization;

namespace Kvertis.Engine.Conversion.Models;

/// <summary>Tokenizing and number parsing for the text model formats (STL, OBJ, PLY). Always invariant culture.</summary>
internal static class ModelText
{
    private const string Separators = " \t";

    public static readonly char[] Whitespace = [' ', '\t'];

    public static readonly char[] WhitespaceAndNewLines = [' ', '\t', '\r', '\n'];

    /// <summary>
    /// Splits <paramref name="line"/> at spaces and tabs into <paramref name="parts"/>, dropping empty entries.
    /// Returns the number of tokens; when there are more tokens than slots, the result is parts.Length + 1 so
    /// callers can tell "too many" apart from "exactly enough".
    /// </summary>
    public static int Split(ReadOnlySpan<char> line, Span<Range> parts)
    {
        var count = 0;
        var i = 0;
        while (i < line.Length)
        {
            while (i < line.Length && Separators.Contains(line[i]))
            {
                i++;
            }
            if (i >= line.Length)
            {
                break;
            }
            var start = i;
            while (i < line.Length && !Separators.Contains(line[i]))
            {
                i++;
            }
            if (count == parts.Length)
            {
                return parts.Length + 1;
            }
            parts[count++] = new Range(start, i);
        }
        return count;
    }

    public static bool TryParseFloat(ReadOnlySpan<char> text, out float value) =>
        float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    public static bool TryParseInt(ReadOnlySpan<char> text, out int value) =>
        int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);

    /// <summary>Shortest round-trip text for a float, invariant culture.</summary>
    public static string Format(float value) => value.ToString("R", CultureInfo.InvariantCulture);
}
