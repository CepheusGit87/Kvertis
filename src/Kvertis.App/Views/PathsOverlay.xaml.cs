using System.Numerics;
using Kvertis.App.Scenes;
using Kvertis.App.Services;
using Kvertis.App.ViewModels.Drop;
using Kvertis.Engine.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Kvertis.App.Views;

/// <summary>
/// The readable half of the zoom (ADR-022): the two format lists, the title and "Übersicht". The ways between
/// them are drawn by the galaxy, which only needs to know where the rows sit; this control reports that.
/// </summary>
public sealed partial class PathsOverlay : UserControl, IDisposable
{
    /// <summary>How long layout has to be quiet before new anchors are reported.</summary>
    private static readonly TimeSpan AnchorDelay = TimeSpan.FromMilliseconds(50);

    /// <summary>How often the automatic change of the left selection is checked.</summary>
    private static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(250);

    private readonly IMotionSettings _motion;
    private readonly IUiDispatcher _ui;
    private readonly DispatcherTimer _timer = new();

    private Debouncer? _anchors;
    private bool _listeningToLayout;

    public PathsOverlay()
    {
        _motion = App.Services.GetRequiredService<IMotionSettings>();
        _ui = App.Services.GetRequiredService<IUiDispatcher>();
        InitializeComponent();
        _timer.Interval = TickInterval;
        _timer.Tick += OnTimerTick;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    /// <summary>Raised by "Übersicht", by Escape and by the same tray head that opened the zoom.</summary>
    public event EventHandler? BackRequested;

    /// <summary>The lists to show. Set by the page before the overlay becomes visible.</summary>
    public PathsViewModel Paths { get; set; } = null!;

    /// <summary>The scene that draws the ways.</summary>
    public GalaxyScene? Scene { get; set; }

    /// <summary>The element the anchor coordinates are measured against: the drawing surface.</summary>
    public FrameworkElement? AnchorRoot { get; set; }

    /// <summary>The page tells the overlay that the zoom opened or closed, so it can hook up its layout watch.</summary>
    public void NotifyZoomChanged()
    {
        UpdateLayoutWatch();
        ReportAnchors();
    }

    /// <summary>Stops the automation timer and the debouncer; the same work OnUnloaded does.</summary>
    public void Dispose()
    {
        _timer.Stop();
        StopLayoutWatch();
    }

    /// <summary>
    /// The page is cached, so the overlay is loaded and unloaded again and again. Everything that is torn
    /// down on unload is therefore built here, not in the constructor.
    /// </summary>
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _anchors ??= new Debouncer(ReportAnchors, _ui.Post, AnchorDelay);
        // No automatic change with reduced motion: the lists simply stand still (worksheet, "Zoom-Automatik").
        if (_motion.AnimationsEnabled)
        {
            _timer.Start();
        }

        UpdateLayoutWatch();
        ReportAnchors();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _timer.Stop();
        StopLayoutWatch();
    }

    private void OnTimerTick(object? sender, object e)
    {
        UpdateLayoutWatch();
        if (Paths?.Tick() == true)
        {
            ReportAnchors();
        }
    }

    /// <summary>Layout events only cost anything while the zoom is open, so they are hooked up only then.</summary>
    private void UpdateLayoutWatch()
    {
        var wanted = IsLoaded && Paths is { Kind: not MediaKind.Unknown };
        if (wanted == _listeningToLayout)
        {
            return;
        }

        _listeningToLayout = wanted;
        if (wanted)
        {
            LayoutUpdated += OnLayoutUpdated;
        }
        else
        {
            LayoutUpdated -= OnLayoutUpdated;
        }
    }

    private void StopLayoutWatch()
    {
        if (_listeningToLayout)
        {
            LayoutUpdated -= OnLayoutUpdated;
            _listeningToLayout = false;
        }

        _anchors?.Dispose();
        _anchors = null;
    }

    private void OnLayoutUpdated(object? sender, object e) => _anchors?.Request();

    /// <summary>
    /// The rows slide in from their side, 35 ms apart (.seite-l/-r > *: 14 px, 0.4 s fade, 0.5 s slide); with
    /// reduced motion they simply appear. Composition translation does not move the layout slot, so the
    /// anchors of the ways stay where the rows end up.
    /// </summary>
    private void OnRowLoaded(object sender, RoutedEventArgs e)
    {
        if (!_motion.AnimationsEnabled || sender is not FrameworkElement row)
        {
            return;
        }

        var left = row.DataContext is PathFormatViewModel;
        var list = left ? InputList : OutputList;
        var index = list.IndexFromContainer(list.ContainerFromItem(row.DataContext));
        var delay = TimeSpan.FromMilliseconds(35 * (Math.Max(index, 0) + 1));

        var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(row);
        var compositor = visual.Compositor;
        var ease = compositor.CreateCubicBezierEasingFunction(new Vector2(0.2f, 0.9f), new Vector2(0.25f, 1f));

        var fade = compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(0f, 0f);
        fade.InsertKeyFrame(1f, 1f);
        fade.Duration = TimeSpan.FromMilliseconds(400);
        fade.DelayTime = delay;
        fade.DelayBehavior = Microsoft.UI.Composition.AnimationDelayBehavior.SetInitialValueBeforeDelay;
        visual.StartAnimation("Opacity", fade);

        Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.SetIsTranslationEnabled(row, true);
        var slide = compositor.CreateVector3KeyFrameAnimation();
        slide.InsertKeyFrame(0f, new Vector3(left ? -14f : 14f, 0f, 0f));
        slide.InsertKeyFrame(1f, Vector3.Zero, ease);
        slide.Duration = TimeSpan.FromMilliseconds(500);
        slide.DelayTime = delay;
        slide.DelayBehavior = Microsoft.UI.Composition.AnimationDelayBehavior.SetInitialValueBeforeDelay;
        visual.StartAnimation("Translation", slide);
    }

    private void OnBackClick(object sender, RoutedEventArgs e) => BackRequested?.Invoke(this, EventArgs.Empty);

    private void OnInputChecked(object sender, RoutedEventArgs e)
    {
        // The row comes from the template's data context; x:Bind to Tag is not allowed inside a DataTemplate.
        if (sender is FrameworkElement { DataContext: PathFormatViewModel row } && Paths is { } paths && !ReferenceEquals(paths.SelectedInput, row))
        {
            paths.Choose(row, byClick: true);
            ReportAnchors();
        }
    }

    /// <summary>
    /// Hands the scene the canvas coordinates of every row's inner edge, so the ways start and end exactly
    /// where the lists do.
    /// </summary>
    private void ReportAnchors()
    {
        if (Scene is not { } scene || Paths is not { } paths || AnchorRoot is not { } root || !IsLoaded)
        {
            return;
        }

        var left = Collect(InputList, paths.Inputs, root, rightEdge: true);
        var right = Collect(OutputList, paths.Outputs, root, rightEdge: false);
        scene.Enqueue(new SetPathAnchors(left, right, paths.SelectedInput?.Id.Id, paths.Reachable, paths.RecommendedId));
    }

    private static List<PathAnchor> Collect<T>(
        ItemsControl list,
        IReadOnlyList<T> rows,
        FrameworkElement root,
        bool rightEdge)
        where T : PathRowBase
    {
        var anchors = new List<PathAnchor>(rows.Count);
        for (var i = 0; i < rows.Count; i++)
        {
            if (list.ContainerFromIndex(i) is not FrameworkElement container || container.ActualHeight <= 0)
            {
                continue;
            }

            var transform = container.TransformToVisual(root);
            var point = transform.TransformPoint(new Windows.Foundation.Point(
                rightEdge ? container.ActualWidth : 0,
                container.ActualHeight / 2));
            anchors.Add(new PathAnchor(rows[i].Id.Id, new Vector2((float)point.X, (float)point.Y)));
        }

        return anchors;
    }
}
