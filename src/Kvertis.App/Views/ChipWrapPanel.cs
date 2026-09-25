using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace Kvertis.App.Views;

/// <summary>
/// Lays its children out left to right and starts a new line when the next one does not fit (the draft's
/// <c>.chips { display: flex; flex-wrap: wrap; gap: 4px }</c>). WinUI has no wrap panel of its own and the app
/// takes no new package for it.
/// </summary>
public sealed partial class ChipWrapPanel : Panel
{
    public static readonly DependencyProperty SpacingProperty = DependencyProperty.Register(
        nameof(Spacing), typeof(double), typeof(ChipWrapPanel), new PropertyMetadata(4.0, OnLayoutPropertyChanged));

    /// <summary>Gap between chips, horizontally and between lines.</summary>
    public double Spacing
    {
        get => (double)GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    private static void OnLayoutPropertyChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) =>
        ((ChipWrapPanel)sender).InvalidateMeasure();

    protected override Size MeasureOverride(Size availableSize)
    {
        var child = new Size(double.PositiveInfinity, double.PositiveInfinity);
        foreach (var element in Children)
        {
            element.Measure(child);
        }

        return Place(availableSize.Width, arrange: false);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        Place(finalSize.Width, arrange: true);
        return finalSize;
    }

    /// <summary>Walks the children once; measures the used size or arranges them.</summary>
    private Size Place(double width, bool arrange)
    {
        var spacing = Spacing;
        double x = 0, y = 0, line = 0, used = 0;
        foreach (var element in Children)
        {
            var size = element.DesiredSize;
            if (x > 0 && x + size.Width > width)
            {
                x = 0;
                y += line + spacing;
                line = 0;
            }

            if (arrange)
            {
                element.Arrange(new Rect(x, y, size.Width, size.Height));
            }

            x += size.Width + spacing;
            used = Math.Max(used, x - spacing);
            line = Math.Max(line, size.Height);
        }

        return new Size(double.IsInfinity(width) ? used : Math.Min(used, width), y + line);
    }
}
