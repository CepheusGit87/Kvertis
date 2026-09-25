using Kvertis.App.Services;
using Kvertis.App.ViewModels.Target;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Kvertis.App.Views;

/// <summary>
/// Step 2 "Ziel". The page only shows what <see cref="TargetPageViewModel"/> computes; the colour zones of
/// the size bar disappear in high contrast, where only text carries the meaning (docs/06-design.md).
/// </summary>
public sealed partial class TargetPage : Page
{
    private readonly IMotionSettings _motion;

    public TargetPage()
    {
        ViewModel = App.Services.GetRequiredService<TargetPageViewModel>();
        _motion = App.Services.GetRequiredService<IMotionSettings>();
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public TargetPageViewModel ViewModel { get; }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel.Load();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        // The page is cached, so the groups and their timers would otherwise stay alive on the other steps.
        ViewModel.Unload();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _motion.Changed -= OnMotionChanged;
        _motion.Changed += OnMotionChanged;
        ApplyContrast();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e) => _motion.Changed -= OnMotionChanged;

    private void OnMotionChanged(object? sender, EventArgs e) => ApplyContrast();

    private void ApplyContrast() =>
        SizeSegments.Visibility = _motion.IsHighContrast ? Visibility.Collapsed : Visibility.Visible;
}
