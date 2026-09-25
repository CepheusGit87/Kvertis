using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Kvertis.App.Helpers;

/// <summary>
/// Small pure functions used from x:Bind function bindings, so XAML needs no value converters.
/// </summary>
public static class Ui
{
    public static Visibility Show(bool value) => value ? Visibility.Visible : Visibility.Collapsed;

    public static Visibility Hide(bool value) => value ? Visibility.Collapsed : Visibility.Visible;

    public static Visibility ShowText(string? value) => string.IsNullOrEmpty(value) ? Visibility.Collapsed : Visibility.Visible;

    public static Visibility ShowAll(bool first, bool second) => first && second ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Visible when <paramref name="show"/> holds and <paramref name="suppress"/> does not.</summary>
    public static Visibility ShowUnless(bool show, bool suppress) => show && !suppress ? Visibility.Visible : Visibility.Collapsed;

    public static bool Not(bool value) => !value;

    /// <summary>Full opacity when true, dimmed when false (used for targets an input cannot reach).</summary>
    public static double DimIf(bool value) => value ? 1.0 : 0.4;

    /// <summary>
    /// Looks up a theme brush by key, so a view model can name a colour ("KvMintBrush") without referencing
    /// a WinUI type. Unknown keys stay transparent instead of throwing.
    /// </summary>
    public static Brush Brush(string? key)
    {
        if (!string.IsNullOrEmpty(key)
            && Application.Current?.Resources.TryGetValue(key, out var value) == true
            && value is Brush brush)
        {
            return brush;
        }
        return new SolidColorBrush(Microsoft.UI.Colors.Transparent);
    }
}
