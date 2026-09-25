using System.Collections.Specialized;
using System.ComponentModel;
using Kvertis.App.Helpers;
using Kvertis.App.Services;
using Kvertis.App.ViewModels.Target;
using Kvertis.Engine.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;

namespace Kvertis.App.Views;

/// <summary>
/// Step 2 "Ziel". The page only shows what <see cref="TargetPageViewModel"/> computes. The code-behind does three
/// view-only jobs: it switches the column layout by window width, it lights up the chosen kind in the list of
/// universes (and lets its orbit open once), and it lays out the colour track and the marks of the size bar, whose
/// positions depend on the width of the bar. In high contrast the colour segments disappear and only text carries the
/// meaning (docs/06-design.md).
/// </summary>
public sealed partial class TargetPage : Page, ITransitionAnchors
{
    // Column widths of the draft at >= 1060 px and the 85 % below (docs/entwuerfe/abgleich-mischentwurf.md 1.1).
    private const double WideLeft = 250;
    private const double WideRight = 300;
    private const double NarrowFactor = 0.85;
    private const double FullLayoutWidth = 1060;
    private const double StackedLayoutWidth = 900;

    // .k-b: the chosen universe; the opening of its orbit (.k-auf, 0.9 s) and the colour of the symbol (0.7 s).
    private const double OrbitBackOpacity = 0.3;
    private const double OrbitFrontOpacity = 0.65;
    private const double HaloOpacity = 0.08;
    private static readonly TimeSpan OrbitOpening = TimeSpan.FromMilliseconds(900);
    private static readonly TimeSpan IconFade = TimeSpan.FromMilliseconds(700);

    private readonly IMotionSettings _motion;
    private TuningPanelViewModel? _tuning;
    private bool? _stacked;

    public TargetPage()
    {
        ViewModel = App.Services.GetRequiredService<TargetPageViewModel>();
        _motion = App.Services.GetRequiredService<IMotionSettings>();
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        SizeChanged += OnPageSizeChanged;
        ActualThemeChanged += OnActualThemeChanged;
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
        ViewModel.PropertyChanged -= OnViewModelChanged;
        ViewModel.PropertyChanged += OnViewModelChanged;
        AttachTuning(ViewModel.SelectedKind?.Tuning);
        ApplyLayout(ActualWidth);
        ApplyContrast();
        UpdateKindVisuals(animate: false);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _motion.Changed -= OnMotionChanged;
        ViewModel.PropertyChanged -= OnViewModelChanged;
        AttachTuning(null);
    }

    private void OnMotionChanged(object? sender, EventArgs e) => ApplyContrast();

    private void OnActualThemeChanged(FrameworkElement sender, object args) => LayoutSizeBar();

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TargetPageViewModel.SelectedKind))
        {
            AttachTuning(ViewModel.SelectedKind?.Tuning);
        }
    }

    private void ApplyContrast()
    {
        LayoutSizeBar();
    }

    // ---- Layout by width ------------------------------------------------------------------------------------

    private void OnPageSizeChanged(object sender, SizeChangedEventArgs e) => ApplyLayout(e.NewSize.Width);

    /// <summary>
    /// &gt;= 1060: the draft's columns 1:1. 900-1059: the side columns shrink to 85 %. Below 900 the columns stack
    /// and the whole body scrolls. Code-behind, not VisualState setters on ColumnDefinition (see docs/06-design.md).
    /// </summary>
    private void ApplyLayout(double width)
    {
        if (width <= 0)
        {
            return;
        }

        var stacked = width < StackedLayoutWidth;
        var factor = width >= FullLayoutWidth ? 1.0 : NarrowFactor;
        if (!stacked)
        {
            LeftColumn.Width = new GridLength(WideLeft * factor);
            RightColumn.Width = new GridLength(WideRight * factor);
        }
        if (_stacked == stacked)
        {
            return;
        }
        _stacked = stacked;

        if (stacked)
        {
            RailGap.Width = new GridLength(16);
            RailColumn.Width = new GridLength(1, GridUnitType.Star);
            LeftGap.Width = new GridLength(0);
            LeftColumn.Width = new GridLength(0);
            MiddleColumn.Width = new GridLength(0);
            RightColumn.Width = new GridLength(0);
            Row0.Height = GridLength.Auto;
            Place(KindsList, 0);
            Place(LeftPanel, 1);
            Place(MiddlePanel, 2);
            Place(RightPanel, 3);
            KindsList.HorizontalAlignment = HorizontalAlignment.Center;
            BodyScroller.VerticalScrollMode = ScrollMode.Enabled;
            BodyScroller.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        }
        else
        {
            RailGap.Width = new GridLength(10);
            RailColumn.Width = new GridLength(124);
            LeftGap.Width = new GridLength(12);
            MiddleColumn.Width = new GridLength(1, GridUnitType.Star);
            Row0.Height = new GridLength(1, GridUnitType.Star);
            Grid.SetColumn(KindsList, 1);
            Grid.SetColumn(LeftPanel, 3);
            Grid.SetColumn(MiddlePanel, 4);
            Grid.SetColumn(RightPanel, 5);
            foreach (var element in new FrameworkElement[] { KindsList, LeftPanel, MiddlePanel, RightPanel })
            {
                Grid.SetRow(element, 0);
            }
            KindsList.HorizontalAlignment = HorizontalAlignment.Stretch;
            BodyScroller.VerticalScrollMode = ScrollMode.Disabled;
            BodyScroller.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
        }
    }

    private static void Place(FrameworkElement element, int row)
    {
        Grid.SetColumn(element, 1);
        Grid.SetRow(element, row);
    }

    // ---- Kinds as universes ---------------------------------------------------------------------------------

    private void OnKindsSelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateKindVisuals(animate: true);

    private void OnKindContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (!args.InRecycleQueue && args.ItemContainer is ListViewItem container)
        {
            ApplyKindVisual(container, container.IsSelected, animate: false);
        }
    }

    private void UpdateKindVisuals(bool animate)
    {
        foreach (var group in ViewModel.Kinds)
        {
            if (KindsList.ContainerFromItem(group) is ListViewItem container)
            {
                var selected = ReferenceEquals(group, KindsList.SelectedItem);
                // Only the newly chosen universe opens its orbit; the others just turn grey.
                ApplyKindVisual(container, selected, animate && selected && container.Tag is not true);
                container.Tag = selected;
            }
        }
    }

    /// <summary>
    /// Chosen: card, orbit, halo and symbol in the kind colour, name in ink. Otherwise the grey layers. With
    /// <paramref name="animate"/> the coloured orbit grows from 55 % and fades in like the draft's .k-auf; nothing
    /// keeps moving afterwards, and with animations off it simply appears.
    /// </summary>
    private void ApplyKindVisual(ListViewItem container, bool selected, bool animate)
    {
        if (container.ContentTemplateRoot is not FrameworkElement root)
        {
            return;
        }

        SetOpacity(root, "KindCard", selected ? 1 : 0);
        SetOpacity(root, "KindHalo", selected ? HaloOpacity : 0);
        SetOpacity(root, "KindNameText", selected ? 1 : 0.6);
        SetOpacity(root, "OrbitBackGrey", selected ? 0 : 0.3);
        SetOpacity(root, "OrbitFrontGrey", selected ? 0 : 0.55);
        SetOpacity(root, "KindIconGrey", selected ? 0 : 0.85);

        var back = root.FindName("OrbitBackColor") as UIElement;
        var front = root.FindName("OrbitFrontColor") as UIElement;
        var icon = root.FindName("KindIconColor") as UIElement;
        if (back is null || front is null || icon is null)
        {
            return;
        }

        if (!selected || !animate || !_motion.AnimationsEnabled)
        {
            back.Opacity = selected ? OrbitBackOpacity : 0;
            front.Opacity = selected ? OrbitFrontOpacity : 0;
            icon.Opacity = selected ? 1 : 0;
            ResetScale(back);
            ResetScale(front);
            return;
        }

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var story = new Storyboard();
        AddOpening(story, back, OrbitBackOpacity, ease);
        AddOpening(story, front, OrbitFrontOpacity, ease);
        var fade = new DoubleAnimation { From = 0, To = 1, Duration = IconFade, EasingFunction = ease };
        Storyboard.SetTarget(fade, icon);
        Storyboard.SetTargetProperty(fade, "Opacity");
        story.Children.Add(fade);
        story.Begin();
    }

    private static void AddOpening(Storyboard story, UIElement orbit, double opacity, EasingFunctionBase ease)
    {
        var fade = new DoubleAnimation { From = 0, To = opacity, Duration = OrbitOpening, EasingFunction = ease };
        Storyboard.SetTarget(fade, orbit);
        Storyboard.SetTargetProperty(fade, "Opacity");
        story.Children.Add(fade);
        if (orbit.RenderTransform is ScaleTransform scale)
        {
            foreach (var axis in new[] { "ScaleX", "ScaleY" })
            {
                var grow = new DoubleAnimation { From = 0.55, To = 1, Duration = OrbitOpening, EasingFunction = ease };
                Storyboard.SetTarget(grow, scale);
                Storyboard.SetTargetProperty(grow, axis);
                story.Children.Add(grow);
            }
        }
    }

    private static void ResetScale(UIElement element)
    {
        if (element.RenderTransform is ScaleTransform scale)
        {
            scale.ScaleX = 1;
            scale.ScaleY = 1;
        }
    }

    private static void SetOpacity(FrameworkElement root, string name, double opacity)
    {
        if (root.FindName(name) is UIElement element)
        {
            element.Opacity = opacity;
        }
    }

    // ---- Size bar: colour track and marks -------------------------------------------------------------------

    private void AttachTuning(TuningPanelViewModel? tuning)
    {
        if (ReferenceEquals(_tuning, tuning))
        {
            LayoutSizeBar();
            return;
        }
        if (_tuning is not null)
        {
            _tuning.SizeBar.Segments.CollectionChanged -= OnSizeBarContentChanged;
            _tuning.SizeBar.Marks.CollectionChanged -= OnSizeBarContentChanged;
            _tuning.PropertyChanged -= OnTuningChanged;
        }
        _tuning = tuning;
        if (_tuning is not null)
        {
            _tuning.SizeBar.Segments.CollectionChanged += OnSizeBarContentChanged;
            _tuning.SizeBar.Marks.CollectionChanged += OnSizeBarContentChanged;
            _tuning.PropertyChanged += OnTuningChanged;
        }
        LayoutSizeBar();
    }

    private void OnSizeBarContentChanged(object? sender, NotifyCollectionChangedEventArgs e) => LayoutSizeBar();

    private void OnTuningChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(TuningPanelViewModel.HasPrevious) or nameof(TuningPanelViewModel.PreviousBarLeft)
            or nameof(TuningPanelViewModel.SupportsGrade))
        {
            LayoutSizeBar();
        }
    }

    private void OnSizeBarSizeChanged(object sender, SizeChangedEventArgs e) => LayoutSizeBar();

    /// <summary>
    /// The 24 segments become one horizontal gradient with hard stops (a rounded border clips its own background,
    /// not its children), at 75 % like the draft. The marks stand at their share of the bar width with a 1 px tick
    /// through the track; the "damals" mark sits under the track.
    /// </summary>
    private void LayoutSizeBar()
    {
        if (!IsLoaded)
        {
            return;
        }

        var bar = _tuning?.SizeBar;
        var width = SizeBarHost.ActualWidth;

        if (_motion.IsHighContrast || bar is null || bar.Segments.Count == 0)
        {
            SizeTrack.Background = Ui.Brush("KvDeepBrush");
            SizeTrack.Opacity = 1;
        }
        else
        {
            var brush = new LinearGradientBrush { StartPoint = new Windows.Foundation.Point(0, 0.5), EndPoint = new Windows.Foundation.Point(1, 0.5) };
            var count = bar.Segments.Count;
            for (var i = 0; i < count; i++)
            {
                var color = Ui.Brush(bar.Segments[i].BrushKey) is SolidColorBrush solid ? solid.Color : Microsoft.UI.Colors.Transparent;
                brush.GradientStops.Add(new GradientStop { Color = color, Offset = (double)i / count });
                brush.GradientStops.Add(new GradientStop { Color = color, Offset = (double)(i + 1) / count });
            }
            SizeTrack.Background = brush;
            SizeTrack.Opacity = 0.75;
        }

        // Remove the old marks, keep the "damals" mark.
        for (var i = SizeMarks.Children.Count - 1; i >= 0; i--)
        {
            if (!ReferenceEquals(SizeMarks.Children[i], PreviousBarMark))
            {
                SizeMarks.Children.RemoveAt(i);
            }
        }

        if (bar is null || width <= 0)
        {
            PreviousBarMark.Visibility = Visibility.Collapsed;
            return;
        }

        var tickBrush = Ui.Brush("KvMutedBrush");
        foreach (var mark in bar.Marks)
        {
            var x = Math.Round(mark.Position / bar.Maximum * width);
            var label = new TextBlock
            {
                Text = mark.Label,
                Style = (Style)Application.Current.Resources["KvMonoTextStyle"],
                FontSize = 9.5,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            };
            label.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(label, Math.Clamp(x - label.DesiredSize.Width / 2, -6, Math.Max(-6, width + 6 - label.DesiredSize.Width)));
            Canvas.SetTop(label, 0);
            var tick = new Rectangle { Width = 1, Height = 18, Fill = tickBrush };
            Canvas.SetLeft(tick, x);
            Canvas.SetTop(tick, 13);
            SizeMarks.Children.Add(tick);
            SizeMarks.Children.Add(label);
        }

        if (_tuning is { HasPrevious: true } tuning)
        {
            PreviousBarMark.Visibility = Visibility.Visible;
            PreviousBarMark.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
            var x = tuning.PreviousBarPosition / bar.Maximum * width;
            Canvas.SetLeft(PreviousBarMark, Math.Clamp(x - PreviousBarMark.DesiredSize.Width / 2, 0, Math.Max(0, width - PreviousBarMark.DesiredSize.Width)));
        }
        else
        {
            PreviousBarMark.Visibility = Visibility.Collapsed;
        }
    }

    // ---- Transition anchors (ADR-023) -----------------------------------------------------------------------

    /// <summary>
    /// One anchor per kind, on the core of its universe in the list, and the hole in the middle of the paths
    /// panel, radius 12, until the drawing layer of step 2 exists.
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
            if (KindsList.ContainerFromItem(group) is ListViewItem container)
            {
                var symbol = (container.ContentTemplateRoot as FrameworkElement)?.FindName("KindCore") as FrameworkElement ?? container;
                if (TransitionMeasure.Of(symbol, reference, TransitionAnchorKind.KindSymbol, group.Kind) is { } anchor)
                {
                    anchors.Add(anchor);
                }
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
