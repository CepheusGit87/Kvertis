using System.Numerics;
using Kvertis.App.Helpers;
using Kvertis.App.Services;
using Kvertis.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace Kvertis.App.Views;

/// <summary>Side panel with the recent conversions, grouped by day on a timeline (worksheet 3.6).</summary>
public sealed partial class HistoryPanel : UserControl
{
    public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(
        nameof(ViewModel), typeof(HistoryViewModel), typeof(HistoryPanel), new PropertyMetadata(null, OnViewModelChanged));

    // One shared shadow for all cards (1.5: cards cast ThemeShadow at depth 8, never in high contrast).
    private readonly ThemeShadow _cardShadow = new();
    private readonly IMotionSettings _motion;

    public HistoryPanel()
    {
        InitializeComponent();
        _motion = App.Services.GetRequiredService<IMotionSettings>();
    }

    public HistoryViewModel? ViewModel
    {
        get => (HistoryViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    private static void OnViewModelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var panel = (HistoryPanel)d;
        ((CollectionViewSource)panel.Resources["DayGroups"]).Source = (e.NewValue as HistoryViewModel)?.Groups;
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel is { } vm)
        {
            vm.IsOpen = false;
        }
    }

    private void OnCardLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Border card)
        {
            return;
        }
        if (_motion.IsHighContrast)
        {
            card.Shadow = null;
            card.Translation = Vector3.Zero;
            return;
        }
        card.Shadow = _cardShadow;
        card.Translation = new Vector3(0, 0, 8);
    }

    // .rk:hover: the border turns line-stark; leaving restores the style's theme brush.
    private void OnCardPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Border card)
        {
            card.BorderBrush = Ui.Brush("KvLineStrongBrush");
        }
    }

    private void OnCardPointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Border card)
        {
            card.ClearValue(Border.BorderBrushProperty);
        }
    }
}
