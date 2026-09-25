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

    /// <summary>
    /// One of the mixed brushes of a kind (KvertisColors.xaml): <c>KindVariant("KvImageBrush", "Tint12")</c> gives
    /// <c>KvImageTint12Brush</c>. An empty variant gives the kind colour itself.
    /// </summary>
    public static Brush KindVariant(string? key, string variant) => Brush(KindKey(key, variant));

    /// <summary>The kind variant when <paramref name="on"/> holds, otherwise the plain brush <paramref name="offKey"/>.</summary>
    public static Brush KindBrushIf(bool on, string? key, string variant, string offKey) =>
        on ? KindVariant(key, variant) : Brush(offKey);

    /// <summary>One of two variants of the same kind.</summary>
    public static Brush KindBrushEither(bool on, string? key, string onVariant, string offVariant) =>
        KindVariant(key, on ? onVariant : offVariant);

    /// <summary>The plain brush <paramref name="onKey"/> when <paramref name="on"/> holds, otherwise a kind variant.</summary>
    public static Brush BrushOrKind(bool on, string onKey, string? key, string offVariant) =>
        on ? Brush(onKey) : KindVariant(key, offVariant);

    /// <summary>One of two plain brushes.</summary>
    public static Brush BrushIf(bool on, string onKey, string offKey) => Brush(on ? onKey : offKey);

    /// <summary>Full opacity when true, <paramref name="off"/> otherwise.</summary>
    public static double OpacityIf(bool value, double off) => value ? 1.0 : off;

    /// <summary>The resource key of a kind variant, see <see cref="KindVariant"/>.</summary>
    public static string KindKey(string? key, string variant)
    {
        if (string.IsNullOrEmpty(key) || !key.EndsWith("Brush", StringComparison.Ordinal))
        {
            return key ?? string.Empty;
        }
        return string.Concat(key.AsSpan(0, key.Length - "Brush".Length), variant, "Brush");
    }
}
