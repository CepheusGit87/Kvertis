using System.Globalization;
using Kvertis.App.Services;

namespace Kvertis.App.Helpers;

/// <summary>Human-friendly sizes and durations. Units and patterns come from the resources.</summary>
public static class Formatting
{
    private const double Kilo = 1024d;

    public static string Bytes(ILocalizer loc, long bytes)
    {
        ArgumentNullException.ThrowIfNull(loc);
        var culture = CultureInfo.CurrentCulture;
        if (bytes < Kilo)
        {
            return loc.Format("Format_Size_Bytes", bytes.ToString("N0", culture));
        }
        if (bytes < Kilo * Kilo)
        {
            return loc.Format("Format_Size_KB", (bytes / Kilo).ToString("N0", culture));
        }
        if (bytes < Kilo * Kilo * Kilo)
        {
            return loc.Format("Format_Size_MB", (bytes / (Kilo * Kilo)).ToString("N1", culture));
        }
        return loc.Format("Format_Size_GB", (bytes / (Kilo * Kilo * Kilo)).ToString("N2", culture));
    }

    public static string Duration(ILocalizer loc, TimeSpan duration)
    {
        ArgumentNullException.ThrowIfNull(loc);
        if (duration < TimeSpan.FromSeconds(1))
        {
            return loc.Get("Format_Duration_UnderSecond");
        }
        if (duration < TimeSpan.FromMinutes(1))
        {
            return loc.Format("Format_Duration_Seconds", (int)Math.Ceiling(duration.TotalSeconds));
        }
        if (duration < TimeSpan.FromHours(1))
        {
            return loc.Format("Format_Duration_Minutes", (int)duration.TotalMinutes, duration.Seconds);
        }
        return loc.Format("Format_Duration_Hours", (int)duration.TotalHours, duration.Minutes);
    }

    /// <summary>Media length such as 3:25 or 1:02:03 (numbers only, no words).</summary>
    public static string Clock(TimeSpan duration) =>
        duration.TotalHours >= 1
            ? duration.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
            : duration.ToString(@"m\:ss", CultureInfo.InvariantCulture);

    /// <summary>Signed percentage change from <paramref name="before"/> to <paramref name="after"/>, e.g. "-87 %" (with a real minus sign).</summary>
    public static string Change(ILocalizer loc, long before, long after)
    {
        ArgumentNullException.ThrowIfNull(loc);
        if (before <= 0)
        {
            return string.Empty;
        }
        var percent = (int)Math.Round((after - before) * 100d / before);
        var number = percent < 0
            ? "\u2212" + Math.Abs(percent).ToString(CultureInfo.CurrentCulture)
            : "+" + percent.ToString(CultureInfo.CurrentCulture);
        return loc.Format("Format_Change_Percent", number);
    }
}
