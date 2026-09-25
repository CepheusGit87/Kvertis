using System.Numerics;
using Kvertis.App.Rendering;
using Kvertis.App.Scenes;
using Kvertis.App.Services;
using Kvertis.App.ViewModels;
using Kvertis.Engine.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

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
            _canvas.Paused = _paused || _suspended;
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
        switch (WantedSurface())
        {
            case 1:
                var canvas = new GalaxyCanvas { Scene = ViewModel.Scene };
                canvas.ZoomRequested += OnCanvasZoomRequested;
                canvas.DrawFailed += OnCanvasDrawFailed;
                _canvas = canvas;
                ApplyPaused();
                SurfaceLayer.Children.Add(canvas);
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
        if (_canvas is { } canvas)
        {
            canvas.ZoomRequested -= OnCanvasZoomRequested;
            canvas.DrawFailed -= OnCanvasDrawFailed;
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
