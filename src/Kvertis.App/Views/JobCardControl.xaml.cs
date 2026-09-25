using Kvertis.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Kvertis.App.Views;

/// <summary>One file card of step 1 (docs/06-design.md, "Job-Karte"). Pure presentation; the entrance
/// animation is played by the list that hosts it.</summary>
public sealed partial class JobCardControl : UserControl
{
    public static readonly DependencyProperty ItemProperty = DependencyProperty.Register(
        nameof(Item), typeof(JobItemViewModel), typeof(JobCardControl), new PropertyMetadata(null));

    public JobCardControl()
    {
        InitializeComponent();
    }

    public JobItemViewModel? Item
    {
        get => (JobItemViewModel?)GetValue(ItemProperty);
        set => SetValue(ItemProperty, value);
    }
}
