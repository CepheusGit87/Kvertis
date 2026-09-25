using Kvertis.App.Helpers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Kvertis.App.Views;

/// <summary>
/// Builds a format chip (.fc) in the colour of a kind for lists that are filled in code, where a DataTemplate
/// cannot reach the kind of its tray. Look from <c>KvChipStyle</c>/<c>KvChipTextStyle</c>; only the kind colours
/// and, for the small variant (.fz .fc, .fach-leer .fc), size 9.5 and padding 5/1 are set here.
/// </summary>
public static class FormatChip
{
    public static Border Create(string text, string? brushKey, bool small)
    {
        var label = new TextBlock
        {
            Text = text,
            Style = (Style)Application.Current.Resources["KvChipTextStyle"],
            Foreground = Ui.Brush(brushKey),
        };
        if (small)
        {
            label.FontSize = 9.5;
        }

        var chip = new Border
        {
            Style = (Style)Application.Current.Resources["KvChipStyle"],
            Background = Ui.KindVariant(brushKey, "Tint12"),
            BorderBrush = Ui.KindVariant(brushKey, "Frame40"),
            Child = label,
        };
        if (small)
        {
            chip.Padding = new Thickness(5, 1, 5, 1);
        }

        return chip;
    }
}
