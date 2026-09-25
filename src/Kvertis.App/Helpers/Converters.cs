using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace Kvertis.App.Helpers;

/// <summary>
/// True becomes visible. Only used where a classic binding is needed (a data context that can be null);
/// everywhere else x:Bind calls the functions in <see cref="Ui"/> instead of a converter.
/// </summary>
public sealed class ShowConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        value is Visibility.Visible;
}

/// <summary>Turns a theme resource key ("KvMintBrush") from a view model into the brush itself.</summary>
public sealed class BrushKeyConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        Ui.Brush(value as string);

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
