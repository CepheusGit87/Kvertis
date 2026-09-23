using System.Globalization;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace Kvertis.Engine.Conversion.Documents;

/// <summary>
/// Reads worksheets row by row. Shared strings are resolved, numbers are formatted as displayed
/// where cheap (dates, times, percent, fixed decimals); formulas yield their cached values.
/// </summary>
internal sealed class XlsxReader
{
    private enum ValueKind
    {
        General,
        Integer,
        TwoDecimals,
        Percent,
        Date,
        Time,
        DateTime,
    }

    private readonly WorkbookPart _workbook;
    private readonly List<string> _sharedStrings;
    private readonly List<ValueKind> _styleKinds;
    private readonly bool _date1904;

    public sealed record SheetEntry(string Name, WorksheetPart Part);

    public XlsxReader(SpreadsheetDocument document, CancellationToken ct)
    {
        _workbook = document.WorkbookPart ?? throw new InvalidDataException("workbook part missing");
        _date1904 = _workbook.Workbook?.WorkbookProperties?.Date1904?.Value == true;
        _sharedStrings = ReadSharedStrings(_workbook.SharedStringTablePart, ct);
        _styleKinds = ReadStyleKinds(_workbook.WorkbookStylesPart);
    }

    /// <summary>Worksheets in workbook order. Chart sheets and dialog sheets are skipped.</summary>
    public IReadOnlyList<SheetEntry> Sheets()
    {
        var result = new List<SheetEntry>();
        var sheets = _workbook.Workbook?.Sheets?.Elements<Sheet>() ?? [];
        foreach (var sheet in sheets)
        {
            var id = sheet.Id?.Value;
            if (id is null)
            {
                continue;
            }
            if (_workbook.TryGetPartById(id, out var part) && part is WorksheetPart ws)
            {
                result.Add(new SheetEntry(sheet.Name?.Value ?? $"Sheet{result.Count + 1}", ws));
            }
        }
        return result;
    }

    /// <summary>
    /// Rows as cell texts, all padded to the same width. Gaps in row numbering become empty rows so
    /// positions stay as in the sheet. Uses two streaming passes: one to find the width, one to read.
    /// </summary>
    public IEnumerable<string[]> ReadRows(WorksheetPart part, CancellationToken ct)
    {
        var width = 0;
        foreach (var element in OpenXmlStreaming.Elements(part, ct, typeof(Row)))
        {
            var row = (Row)element;
            var col = 0;
            foreach (var cell in row.Elements<Cell>())
            {
                col = ColumnIndex(cell.CellReference?.Value) ?? col;
                col++;
                if (!string.IsNullOrEmpty(CellText(cell)))
                {
                    width = Math.Max(width, col);
                }
            }
        }
        if (width == 0)
        {
            yield break;
        }

        uint expectedRow = 1;
        foreach (var element in OpenXmlStreaming.Elements(part, ct, typeof(Row)))
        {
            var row = (Row)element;
            var index = row.RowIndex?.Value ?? expectedRow;
            while (expectedRow < index)
            {
                var empty = new string[width];
                Array.Fill(empty, string.Empty);
                yield return empty;
                expectedRow++;
            }
            var values = new string[width];
            Array.Fill(values, string.Empty);
            var col = 0;
            foreach (var cell in row.Elements<Cell>())
            {
                col = ColumnIndex(cell.CellReference?.Value) ?? col;
                if (col < width)
                {
                    values[col] = CellText(cell);
                }
                col++;
            }
            yield return values;
            expectedRow = index + 1;
        }
    }

    /// <summary>Drops trailing rows that are completely empty (formatted but unused cells).</summary>
    public static IEnumerable<string[]> TrimTrailingEmpty(IEnumerable<string[]> rows)
    {
        var pending = new List<string[]>();
        foreach (var row in rows)
        {
            if (row.All(string.IsNullOrEmpty))
            {
                pending.Add(row);
                continue;
            }
            foreach (var p in pending)
            {
                yield return p;
            }
            pending.Clear();
            yield return row;
        }
    }

    private string CellText(Cell cell)
    {
        var type = cell.DataType?.Value;
        if (type == CellValues.InlineString)
        {
            return cell.InlineString is null ? string.Empty : ItemText(cell.InlineString);
        }
        var raw = cell.CellValue?.Text;
        if (string.IsNullOrEmpty(raw))
        {
            return string.Empty;
        }
        if (type == CellValues.SharedString)
        {
            return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) && i >= 0 && i < _sharedStrings.Count
                ? _sharedStrings[i]
                : string.Empty;
        }
        if (type == CellValues.Boolean)
        {
            return raw == "1" ? "TRUE" : "FALSE";
        }
        if (type == CellValues.String || type == CellValues.Error || type == CellValues.Date)
        {
            return raw;
        }
        if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            return raw;
        }
        var styleIndex = (int)(cell.StyleIndex?.Value ?? 0);
        var kind = styleIndex < _styleKinds.Count ? _styleKinds[styleIndex] : ValueKind.General;
        return FormatNumber(number, kind, _date1904);
    }

    private static string FormatNumber(double value, ValueKind kind, bool date1904)
    {
        switch (kind)
        {
            case ValueKind.Integer:
                return Math.Round(value, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture);
            case ValueKind.TwoDecimals:
                return value.ToString("0.00", CultureInfo.InvariantCulture);
            case ValueKind.Percent:
                return (value * 100).ToString("G15", CultureInfo.InvariantCulture) + "%";
            case ValueKind.Date or ValueKind.Time or ValueKind.DateTime:
                var serial = date1904 ? value + 1462 : value;
                if (serial is < -657435 or > 2958465)
                {
                    break;
                }
                var date = DateTime.FromOADate(serial);
                var hasTime = date.TimeOfDay != TimeSpan.Zero;
                return kind switch
                {
                    ValueKind.Time => date.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
                    ValueKind.DateTime => date.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                    _ => date.ToString(hasTime ? "yyyy-MM-dd HH:mm:ss" : "yyyy-MM-dd", CultureInfo.InvariantCulture),
                };
        }
        return value.ToString("G15", CultureInfo.InvariantCulture);
    }

    /// <summary>"B7" → 1. Returns null when there is no reference.</summary>
    internal static int? ColumnIndex(string? reference)
    {
        if (string.IsNullOrEmpty(reference))
        {
            return null;
        }
        var col = 0;
        foreach (var c in reference)
        {
            if (c is >= 'A' and <= 'Z')
            {
                col = col * 26 + (c - 'A' + 1);
            }
            else if (c is >= 'a' and <= 'z')
            {
                col = col * 26 + (c - 'a' + 1);
            }
            else
            {
                break;
            }
            if (col > 16384)
            {
                return null;
            }
        }
        return col == 0 ? null : col - 1;
    }

    private static List<string> ReadSharedStrings(SharedStringTablePart? part, CancellationToken ct)
    {
        var list = new List<string>();
        if (part is null)
        {
            return list;
        }
        foreach (var element in OpenXmlStreaming.Elements(part, ct, typeof(SharedStringItem)))
        {
            list.Add(ItemText(element));
        }
        return list;
    }

    /// <summary>Text of a shared string item or inline string: plain text or rich text runs, no phonetic hints.</summary>
    private static string ItemText(OpenXmlElement item)
    {
        var sb = new StringBuilder();
        foreach (var child in item.ChildElements)
        {
            switch (child)
            {
                case Text t:
                    sb.Append(t.Text);
                    break;
                case Run r when r.Text is not null:
                    sb.Append(r.Text.Text);
                    break;
            }
        }
        return sb.ToString();
    }

    private static List<ValueKind> ReadStyleKinds(WorkbookStylesPart? part)
    {
        var kinds = new List<ValueKind>();
        var stylesheet = part?.Stylesheet;
        if (stylesheet?.CellFormats is null)
        {
            return kinds;
        }
        var custom = new Dictionary<uint, string>();
        foreach (var nf in stylesheet.NumberingFormats?.Elements<NumberingFormat>() ?? [])
        {
            if (nf.NumberFormatId?.Value is uint id && nf.FormatCode?.Value is { } code)
            {
                custom[id] = code;
            }
        }
        foreach (var format in stylesheet.CellFormats.Elements<CellFormat>())
        {
            var id = format.NumberFormatId?.Value ?? 0;
            kinds.Add(custom.TryGetValue(id, out var code) ? Classify(code) : Builtin(id));
        }
        return kinds;
    }

    private static ValueKind Builtin(uint id) => id switch
    {
        1 or 3 => ValueKind.Integer,
        2 or 4 => ValueKind.TwoDecimals,
        9 or 10 => ValueKind.Percent,
        14 or 15 or 16 or 17 or (>= 27 and <= 31) or (>= 34 and <= 36) or (>= 50 and <= 58) => ValueKind.Date,
        18 or 19 or 20 or 21 or 45 or 46 or 47 or 32 or 33 => ValueKind.Time,
        22 => ValueKind.DateTime,
        _ => ValueKind.General,
    };

    /// <summary>Rough classification of a custom number format code.</summary>
    private static ValueKind Classify(string code)
    {
        // Only the first section (positive numbers) matters; quoted text, escapes and colors are ignored.
        var sb = new StringBuilder();
        var inQuote = false;
        for (var i = 0; i < code.Length; i++)
        {
            var c = code[i];
            if (c == '"')
            {
                inQuote = !inQuote;
                continue;
            }
            if (inQuote)
            {
                continue;
            }
            if (c == '\\' || c == '_' || c == '*')
            {
                i++;
                continue;
            }
            if (c == ';')
            {
                break;
            }
            if (c == '[')
            {
                var close = code.IndexOf(']', i);
                if (close < 0)
                {
                    break;
                }
                var inner = code[(i + 1)..close].ToLowerInvariant();
                if (inner is "h" or "hh" or "m" or "mm" or "s" or "ss")
                {
                    sb.Append(inner); // Elapsed time.
                }
                i = close;
                continue;
            }
            sb.Append(char.ToLowerInvariant(c));
        }
        var s = sb.ToString();
        if (s.Contains('%', StringComparison.Ordinal))
        {
            return ValueKind.Percent;
        }
        var hasDate = s.Contains('d', StringComparison.Ordinal) || s.Contains('y', StringComparison.Ordinal);
        var hasTime = s.Contains('h', StringComparison.Ordinal) || s.Contains('s', StringComparison.Ordinal);
        if (hasDate && hasTime)
        {
            return ValueKind.DateTime;
        }
        if (hasDate || (s.Contains('m', StringComparison.Ordinal) && !hasTime))
        {
            return ValueKind.Date;
        }
        if (hasTime)
        {
            return ValueKind.Time;
        }
        return ValueKind.General;
    }
}
