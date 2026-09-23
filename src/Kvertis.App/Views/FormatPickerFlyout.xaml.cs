using Kvertis.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Kvertis.App.Views;

/// <summary>Output format list shown from a card's format chip. Only formats the machine can produce are listed.</summary>
public sealed partial class FormatPickerFlyout : UserControl
{
    public static readonly DependencyProperty ItemProperty = DependencyProperty.Register(
        nameof(Item), typeof(JobItemViewModel), typeof(FormatPickerFlyout), new PropertyMetadata(null));

    public FormatPickerFlyout()
    {
        InitializeComponent();
    }

    /// <summary>Raised after the user picked a format, so the host can close its flyout.</summary>
    public event EventHandler? Picked;

    public JobItemViewModel? Item
    {
        get => (JobItemViewModel?)GetValue(ItemProperty);
        set => SetValue(ItemProperty, value);
    }

    private void OnFormatClicked(object sender, ItemClickEventArgs e)
    {
        if (Item is { } item && e.ClickedItem is FormatOption option)
        {
            item.SelectedFormat = option;
        }
        Picked?.Invoke(this, EventArgs.Empty);
    }
}
