using System.Globalization;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace Kvertis.App.Helpers;

/// <summary>
/// The arc of the grade ring (KvGradeRingSliderStyle): a value 0..100 becomes the arc from twelve o'clock
/// clockwise. The parameter is "radius,centre" in pixels, e.g. "42,58". Used inside a control template, where
/// no x:Bind function is available.
/// </summary>
public sealed class RingArcConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var (radius, centre) = Parse(parameter as string);
        var fraction = Math.Clamp(ToDouble(value) / 100.0, 0, 1);
        if (fraction >= 0.9999)
        {
            return new EllipseGeometry { Center = new Point(centre, centre), RadiusX = radius, RadiusY = radius };
        }

        var figure = new PathFigure { StartPoint = new Point(centre, centre - radius), IsClosed = false, IsFilled = false };
        if (fraction > 0)
        {
            var angle = fraction * 2 * Math.PI;
            figure.Segments.Add(new ArcSegment
            {
                Point = new Point(centre + radius * Math.Sin(angle), centre - radius * Math.Cos(angle)),
                Size = new Size(radius, radius),
                SweepDirection = SweepDirection.Clockwise,
                IsLargeArc = fraction > 0.5,
            });
        }
        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        return geometry;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();

    private static double ToDouble(object value) => value switch
    {
        double d => d,
        int i => i,
        _ => 0,
    };

    private static (double Radius, double Centre) Parse(string? parameter)
    {
        var parts = (parameter ?? "42,58").Split(',');
        return (double.Parse(parts[0], CultureInfo.InvariantCulture), double.Parse(parts[1], CultureInfo.InvariantCulture));
    }
}

/// <summary>
/// Places a circle of the grade ring (thumb or its glow) on the end of the arc: a value 0..100 becomes the margin of a
/// top-left aligned element. The parameter is "radius,centre,size" in pixels, e.g. "42,58,24".
/// </summary>
public sealed class RingPointConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var parts = ((parameter as string) ?? "42,58,24").Split(',');
        var radius = double.Parse(parts[0], CultureInfo.InvariantCulture);
        var centre = double.Parse(parts[1], CultureInfo.InvariantCulture);
        var half = double.Parse(parts[2], CultureInfo.InvariantCulture) / 2;
        var angle = Math.Clamp((value is double d ? d : 0) / 100.0, 0, 1) * 2 * Math.PI;
        return new Microsoft.UI.Xaml.Thickness(
            centre + radius * Math.Sin(angle) - half,
            centre - radius * Math.Cos(angle) - half,
            0,
            0);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
