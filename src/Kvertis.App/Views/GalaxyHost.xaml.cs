using System.Numerics;
using Kvertis.App.Rendering;
using Kvertis.App.Scenes;
using Kvertis.App.Services;
using Kvertis.App.ViewModels;
using Kvertis.Engine.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Kvertis.App.Views;

/// <summary>
/// The only place that decides what the top of step 1 shows (ADR-022): the moving Win2D surface, the still
/// picture when Windows asks for reduced motion, or nothing at all in high contrast, where the five kind
/// colours collapse into the text colour and rings would carry no information (ADR-017).
/// </summary>
public sealed partial class GalaxyHost : UserControl
{
    private readonly IMotionSettings _motion;

    private GalaxyCanvas? _canvas;
    private GalaxyStaticView? _still;
    private bool _drawingFailed;
    private bool _suspended = true;
    private bool _paused;
    private bool _built;
    private bool _dragOver;

    // The big bang gimmick: the XAML surface is pulled into the hole (worksheet "Spielereien", draft uiSog).
    private readonly List<SuckedElement> _sucked = [];
    private readonly Random _jitter = new();
    private bool _suctionRunning;

    public GalaxyHost()
    {
        _motion = App.Services.GetRequiredService<IMotionSettings>();
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    /// <summary>Raised when the user clicked an orbit or the hole; null means "back to the overview".</summary>
    public event EventHandler<MediaKind?>? ZoomRequested;

    /// <summary>Raised when the surface appeared or vanished, so the page can give the trays the whole height.</summary>
    public event EventHandler? SurfaceVisibilityChanged;

    /// <summary>The view model of step 1. Set from the page before the host is loaded.</summary>
    public MainViewModel ViewModel { get; set; } = App.Services.GetRequiredService<MainViewModel>();

    /// <summary>False in high contrast: there is no galaxy at all and the trays take the whole area.</summary>
    public bool SurfaceVisible => !_motion.IsHighContrast;

    /// <summary>
    /// The hole of the moving surface in the coordinates of <paramref name="reference"/>, for the transition
    /// overlay (ADR-023). False without a moving surface (still picture, high contrast, not laid out).
    /// </summary>
    public bool TryGetHole(UIElement reference, out Vector2 centre, out float radius)
    {
        ArgumentNullException.ThrowIfNull(reference);
        centre = default;
        radius = 0f;
        if (_canvas is not { ActualWidth: > 0 } canvas)
        {
            return false;
        }

        var scene = ViewModel.Scene;
        var point = canvas.TransformToVisual(reference).TransformPoint(new Windows.Foundation.Point(scene.Layout.Center.X, scene.Layout.Center.Y));
        centre = new Vector2((float)point.X, (float)point.Y);
        radius = scene.HoleRadius;
        return true;
    }

    /// <summary>
    /// Starts the drawing loop (page entered, host loaded). Idempotent: both the page's navigation and the
    /// host's Loaded call it, and only the first builds anything.
    /// </summary>
    public void Resume()
    {
        _suspended = false;
        if (!_built || !SurfaceMatchesSettings())
        {
            Rebuild();
        }
        else
        {
            ApplyPaused();
        }
    }

    /// <summary>
    /// Pauses the loop while another step is shown. The surface itself stays: the page is cached, and Win2D
    /// only stops drawing, it does not need to be rebuilt (Nachtrag zu ADR-022).
    /// </summary>
    public void Suspend()
    {
        _suspended = true;
        ApplyPaused();
    }

    /// <summary>Files hover over the page: the drop card turns mint and says "Loslassen" (.welt.zieht .einwurf).</summary>
    public void SetDragOver(bool over)
    {
        if (over == _dragOver)
        {
            return;
        }

        _dragOver = over;
        VisualStateManager.GoToState(this, over ? "Dragging" : "Idle", useTransitions: false);
    }

    /// <summary>Pauses the loop without destroying anything (window hidden, history pane open).</summary>
    public void SetPaused(bool paused)
    {
        _paused = paused;
        ApplyPaused();
    }

    private void ApplyPaused()
    {
        if (_canvas is not null)
        {
            var paused = _paused || _suspended;
            _canvas.Paused = paused;
            if (paused)
            {
                // No pointer while paused: a charge or whirl must not continue where it was when the page returns.
                ViewModel.Scene.Enqueue(new SetPointer(null));
                StopSuction();
            }
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _motion.Changed -= OnMotionChanged;
        _motion.Changed += OnMotionChanged;
        Resume();
    }

    /// <summary>
    /// The page is cached, so unloading only pauses. The motion setting may change while the page is away;
    /// <see cref="Resume"/> notices that on the way back.
    /// </summary>
    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _motion.Changed -= OnMotionChanged;
        Suspend();
    }

    private void OnMotionChanged(object? sender, EventArgs e)
    {
        Rebuild();
        SurfaceVisibilityChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>What the current settings ask for: 0 nothing, 1 the moving surface, 2 the still picture.</summary>
    private int WantedSurface() =>
        !SurfaceVisible ? 0 : _motion.AnimationsEnabled && !_drawingFailed ? 1 : 2;

    private int CurrentSurface() => _canvas is not null ? 1 : _still is not null ? 2 : 0;

    private bool SurfaceMatchesSettings() => WantedSurface() == CurrentSurface();

    /// <summary>Puts exactly the one control into the surface layer that the current settings ask for.</summary>
    private void Rebuild()
    {
        _built = true;
        DropSurface();
        // "Bahn oder Fach anklicken" only makes sense where orbits are drawn.
        ZoomHintHost.Visibility = SurfaceVisible ? Visibility.Visible : Visibility.Collapsed;
        switch (WantedSurface())
        {
            case 1:
                var canvas = new GalaxyCanvas { Scene = ViewModel.Scene };
                canvas.ZoomRequested += OnCanvasZoomRequested;
                canvas.DrawFailed += OnCanvasDrawFailed;
                canvas.SurfaceEffectChanged += OnSurfaceEffectChanged;
                _canvas = canvas;
                ApplyPaused();
                SurfaceLayer.Children.Add(canvas);
                // The gimmicks (whirl, big bang) exist only on the moving surface: never with reduced motion or high contrast.
                ViewModel.Scene.Enqueue(new SetGimmicksEnabled(true));
                break;
            case 2:
                var still = new GalaxyStaticView { ViewModel = ViewModel };
                _still = still;
                SurfaceLayer.Children.Add(still);
                break;
        }
    }

    private void DropSurface()
    {
        StopSuction();
        ViewModel.Scene.Enqueue(new SetGimmicksEnabled(false));
        if (_canvas is { } canvas)
        {
            canvas.ZoomRequested -= OnCanvasZoomRequested;
            canvas.DrawFailed -= OnCanvasDrawFailed;
            canvas.SurfaceEffectChanged -= OnSurfaceEffectChanged;
            canvas.IsHitTestVisible = false;
            _canvas = null;
            _ = RetireAsync(canvas);
        }

        if (_still is { } still)
        {
            still.ViewModel = null;
            SurfaceLayer.Children.Remove(still);
            _still = null;
        }
    }

    /// <summary>
    /// The old surface stays in the tree until its renderer was handed back on the game loop thread, because
    /// Win2D stops that loop as soon as the control is unloaded.
    /// </summary>
    private async Task RetireAsync(GalaxyCanvas canvas)
    {
        await canvas.DestroyAsync();
        SurfaceLayer.Children.Remove(canvas);
    }

    private void OnCanvasZoomRequested(object? sender, MediaKind? kind) => ZoomRequested?.Invoke(this, kind);

    private void OnCanvasDrawFailed(object? sender, EventArgs e)
    {
        // Once drawing failed the app never tries again in this session; the still picture shows the same files.
        _drawingFailed = true;
        Rebuild();
    }

    // ---- big bang: the XAML surface is swallowed ----------------------------------------------------

    /// <summary>One element pulled into the hole: its resting centre in host coordinates and a fixed spin direction.</summary>
    private sealed record SuckedElement(FrameworkElement Element, Vector2 Rest, float Direction);

    private void OnSurfaceEffectChanged(object? sender, bool active)
    {
        if (active)
        {
            StartSuction();
        }
        else
        {
            StopSuction();
        }
    }

    /// <summary>
    /// Collects the trays of the page and the cards of this host and remembers their resting places before
    /// anything moves. The trays are found in the visual tree: the host never talks to the page directly.
    /// </summary>
    private void StartSuction()
    {
        if (_suctionRunning)
        {
            return;
        }

        _sucked.Clear();
        foreach (var element in SuckedElements())
        {
            if (element.ActualWidth <= 0 || element.ActualHeight <= 0 || element.Visibility != Visibility.Visible)
            {
                continue;
            }

            var half = new Windows.Foundation.Point(element.ActualWidth / 2, element.ActualHeight / 2);
            var centre = element.TransformToVisual(HostRoot).TransformPoint(half);
            element.CenterPoint = new Vector3((float)half.X, (float)half.Y, 0f);
            _sucked.Add(new SuckedElement(element, new Vector2((float)centre.X, (float)centre.Y), _jitter.NextSingle() * 2f - 1f));
        }

        _suctionRunning = true;
        CompositionTarget.Rendering += OnSuctionFrame;
    }

    private IEnumerable<FrameworkElement> SuckedElements()
    {
        yield return Summary;
        yield return EntryCard;
        yield return RejectedCard;
        yield return ZoomHintHost;

        DependencyObject? root = this;
        for (var parent = VisualTreeHelper.GetParent(this); parent is not null; parent = VisualTreeHelper.GetParent(parent))
        {
            root = parent;
            if (parent is Page)
            {
                break;
            }
        }

        foreach (var tray in Descendants<TrayControl>(root))
        {
            yield return tray;
        }
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject? root)
        where T : DependencyObject
    {
        if (root is null)
        {
            yield break;
        }

        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
            {
                yield return match;
                continue;
            }

            foreach (var inner in Descendants<T>(child))
            {
                yield return inner;
            }
        }
    }

    /// <summary>
    /// Draft uiSog per frame: every element slides towards the hole by the suction share, turns up to 50°,
    /// shrinks to at least 4 % and fades; a random jitter shakes it. Pure Composition properties, nothing is
    /// drawn on the canvas.
    /// </summary>
    private void OnSuctionFrame(object? sender, object e)
    {
        var snapshot = ViewModel.Scene.Snapshot;
        if (!snapshot.SurfaceAffected)
        {
            StopSuction();
            return;
        }

        var s = snapshot.SurfaceSuction;
        var j = snapshot.SurfaceJitter;
        var hole = snapshot.HoleCenter;
        var scale = Math.Max(0.04f, 1f - 0.92f * s);
        foreach (var (element, rest, direction) in _sucked)
        {
            var jx = (_jitter.NextSingle() - 0.5f) * j;
            var jy = (_jitter.NextSingle() - 0.5f) * j;
            element.Translation = new Vector3((hole.X - rest.X) * s + jx, (hole.Y - rest.Y) * s + jy, 0f);
            element.Rotation = direction * s * 50f + (_jitter.NextSingle() - 0.5f) * j * 0.6f;
            element.Scale = new Vector3(scale, scale, 1f);
            element.Opacity = Math.Clamp(snapshot.SurfaceOpacity, 0f, 1f);
        }
    }

    /// <summary>Puts every element back exactly where it was; the scene has restored the planets by then.</summary>
    private void StopSuction()
    {
        if (!_suctionRunning)
        {
            return;
        }

        _suctionRunning = false;
        CompositionTarget.Rendering -= OnSuctionFrame;
        foreach (var (element, _, _) in _sucked)
        {
            element.Translation = Vector3.Zero;
            element.Rotation = 0f;
            element.Scale = Vector3.One;
            element.Opacity = 1.0;
        }

        _sucked.Clear();
    }

    /// <summary>Tells the scene where rejected files have to fly to.</summary>
    private void OnRejectedCardSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_canvas is null || sender is not FrameworkElement card)
        {
            return;
        }

        var transform = card.TransformToVisual(SurfaceLayer);
        var centre = transform.TransformPoint(new Windows.Foundation.Point(card.ActualWidth / 2, card.ActualHeight / 2));
        ViewModel.Scene.Enqueue(new SetRejectedAnchor(new Vector2((float)centre.X, (float)centre.Y)));
    }
}
