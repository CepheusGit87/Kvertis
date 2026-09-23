using Kvertis.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Kvertis.App.Views;

/// <summary>Side panel with the recent conversions.</summary>
public sealed partial class HistoryPanel : UserControl
{
    public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(
        nameof(ViewModel), typeof(HistoryViewModel), typeof(HistoryPanel), new PropertyMetadata(null));

    public HistoryPanel()
    {
        InitializeComponent();
    }

    public HistoryViewModel? ViewModel
    {
        get => (HistoryViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { } vm)
        {
            vm.IsOpen = false;
        }
    }
}
