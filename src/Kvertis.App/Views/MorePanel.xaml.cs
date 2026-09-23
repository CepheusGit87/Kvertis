using Kvertis.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Kvertis.App.Views;

/// <summary>Advanced options of one card: quality, target size, metadata, name pattern, apply to all.</summary>
public sealed partial class MorePanel : UserControl
{
    public static readonly DependencyProperty ItemProperty = DependencyProperty.Register(
        nameof(Item), typeof(JobItemViewModel), typeof(MorePanel), new PropertyMetadata(null));

    public MorePanel()
    {
        InitializeComponent();
    }

    public JobItemViewModel? Item
    {
        get => (JobItemViewModel?)GetValue(ItemProperty);
        set => SetValue(ItemProperty, value);
    }
}
