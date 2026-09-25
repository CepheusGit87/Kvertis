using Kvertis.App.Services;
using Kvertis.App.ViewModels.Target;
using Kvertis.Engine.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Kvertis.App.Views;

/// <summary>
/// Step 2 "Ziel". The page only shows what <see cref="TargetPageViewModel"/> computes; the colour zones of
/// the size bar disappear in high contrast, where only text carries the meaning (docs/06-design.md).
/// </summary>
public sealed partial class TargetPage : Page, ITransitionAnchors
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

    // ---- Transition anchors (ADR-023) ---------------------------------------------------------------

    /// <summary>
    /// One anchor per entry of the kind list (later the universe symbol) and the hole in the middle of the
    /// paths panel, radius 12, until the drawing layer of step 2 exists.
    /// </summary>
    public TransitionAnchorSet? MeasureAnchors(UIElement reference)
    {
        if (!IsLoaded)
        {
            return null;
        }

        var anchors = new List<TransitionAnchor>();
        foreach (var group in ViewModel.Kinds)
        {
            if (KindsList.ContainerFromItem(group) is FrameworkElement container
                && TransitionMeasure.Of(container, reference, TransitionAnchorKind.KindSymbol, group.Kind) is { } anchor)
            {
                anchors.Add(anchor);
            }
        }

        if (TransitionMeasure.Of(MiddlePanel, reference, TransitionAnchorKind.Hole, holeRadius: TransitionPlanner.PageHoleRadius) is { } hole)
        {
            anchors.Add(hole);
        }

        return new TransitionAnchorSet(WorkflowStep.Target, anchors);
    }

    /// <summary>The list entries of the kinds in flight vanish while the overlay draws their stand-ins.</summary>
    public void SetFlightVisibility(bool visible, IReadOnlySet<MediaKind> kinds)
    {
        ArgumentNullException.ThrowIfNull(kinds);
        foreach (var group in ViewModel.Kinds)
        {
            if (kinds.Contains(group.Kind) && KindsList.ContainerFromItem(group) is UIElement container)
            {
                container.Opacity = visible ? 1 : 0;
            }
        }
    }
}
