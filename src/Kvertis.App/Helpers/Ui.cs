using Microsoft.UI.Xaml;

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

    public static bool Not(bool value) => !value;
}
