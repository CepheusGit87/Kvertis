using System.Numerics;
using Kvertis.App.Scenes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Graphics.Canvas.UI;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Kvertis.App.Rendering;

/// <summary>
/// The shell around <see cref="CanvasAnimatedControl"/> for the middle of step 2 (ADR-022, same pattern as
/// <see cref="GalaxyCanvas"/>). It owns the drawing control and the renderer, never the scene: the scene
/// belongs to the view model and survives this control being torn down and built again.
/// </summary>
/// <remarks>
/// Thread rule: <c>Update</c> and <c>Draw</c> run on the Win2D game loop thread and are the only place the
/// scene is touched. The UI thread uses <see cref="TargetPathsScene.Enqueue"/> and reads the pure
/// <see cref="TargetPathsLayout"/> for the hole anchor, nothing else.
/// </remarks>
public sealed partial class TargetPathsCanvas : UserControl, IDisposable
{
    private static readonly TimeSpan FrameTime = TimeSpan.FromSeconds(1.0 / 60.0);

    /// <summary>How long a retiring control may take to hand back its renderer on the game loop thread.</summary>
    private static readonly TimeSpan RetireTimeout = TimeSpan.FromMilliseconds(500);

    private readonly ILogger<TargetPathsCanvas>? _logger;

    private CanvasAnimatedControl? _control;
    private TargetPathsRenderer? _renderer;
    private bool _paused;
    private bool _drawFailed;

    public TargetPathsCanvas()
    {
        InitializeComponent();
        _logger = App.Services.GetService<ILoggerFactory>()?.CreateLogger<TargetPathsCanvas>();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        ActualThemeChanged += OnActualThemeChanged;
    }

    /// <summary>Raised on the UI thread after a drawing error; the page then shows the plain list.</summary>
    public event EventHandler? DrawFailed;

    /// <summary>The scene to draw. Set once by the page; changing it while running is allowed.</summary>
    public TargetPathsScene? Scene { get; set; }

    /// <summary>While true neither <c>Update</c> nor <c>Draw</c> runs and the last frame stays on screen.</summary>
    public bool Paused
    {
        get => _paused;
        set
        {
            _paused = value;
            if (_control is not null)
            {
                _control.Paused = value;
            }
        }
    }

    /// <summary>
    /// The hole in the coordinates of <paramref name="reference"/>, from the pure layout of the current size,
    /// so it is right even while the loop is paused during a transition (the scene applies its resize only
    /// in <c>Update</c>). False before the surface has a size.
    /// </summary>
    public bool TryGetHole(UIElement reference, out Vector2 centre, out float radius)
    {
        ArgumentNullException.ThrowIfNull(reference);
        centre = default;
        radius = 0f;
        if (SurfaceHost.ActualWidth <= 0 || SurfaceHost.ActualHeight <= 0)
        {
            return false;
        }

        var layout = new TargetPathsLayout((float)SurfaceHost.ActualWidth, (float)SurfaceHost.ActualHeight);
        var point = SurfaceHost.TransformToVisual(reference).TransformPoint(new Windows.Foundation.Point(layout.Hole.X, layout.Hole.Y));
        centre = new Vector2((float)point.X, (float)point.Y);
        radius = TargetPathsLayout.HoleRadius;
        return true;
    }

    /// <summary>Unpauses the loop and builds the drawing control if there is none yet. Idempotent.</summary>
    public void Resume()
    {
        _paused = false;
        EnsureControl();
    }

    /// <summary>Pauses the loop while another step is shown; the control stays (Nachtrag zu ADR-022).</summary>
    public void Suspend() => Paused = true;

    /// <summary>
    /// Tears the surface down for good: only for a change of the motion setting or after a drawing error.
    /// The renderer is disposed on the game loop thread while the loop still runs, then the control is paused
    /// and leaves its parent without <c>RemoveFromVisualTree</c> (Nachtrag zu ADR-022).
    /// </summary>
    public Task DestroyAsync()
    {
        var control = _control;
        if (control is null)
        {
            return Task.CompletedTask;
        }

        _control = null;
        control.CreateResources -= OnCreateResources;
        control.Update -= OnUpdate;
        control.Draw -= OnDraw;
        var renderer = _renderer;
        _renderer = null;
        return RetireAsync(control, renderer);
    }

    public void Dispose() => _ = DestroyAsync();

    /// <summary>Re-reads the theme colours and hands them to the scene.</summary>
    public void PublishPalette()
    {
        if (Scene is { } scene)
        {
            scene.Palette = ScenePaletteReader.Read(this);
        }
    }

    private void EnsureControl()
    {
        if (_drawFailed)
        {
            return;
        }

        if (_control is { } existing)
        {
            existing.Paused = _paused;
            return;
        }

        _renderer = new TargetPathsRenderer();
        _control = new CanvasAnimatedControl
        {
            TargetElapsedTime = FrameTime,
            IsFixedTimeStep = false,
            ClearColor = Microsoft.UI.Colors.Transparent,
            IsTabStop = false,
            IsHitTestVisible = false,
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAccessibilityView(
            _control, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        _control.CreateResources += OnCreateResources;
        _control.Update += OnUpdate;
        _control.Draw += OnDraw;
        ApplyDpiCap();
        SurfaceHost.Children.Add(_control);
        _control.Paused = _paused;
        PublishSize();
        PublishPalette();
    }

    private async Task RetireAsync(CanvasAnimatedControl control, TargetPathsRenderer? renderer)
    {
        if (renderer is not null)
        {
            try
            {
                var disposed = control.RunOnGameLoopThreadAsync(() =>
                {
                    try
                    {
                        renderer.Dispose();
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogDebug(ex, "Disposing the target paths renderer failed");
                    }
                }).AsTask();
                await Task.WhenAny(disposed, Task.Delay(RetireTimeout));
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "The game loop did not take the renderer back");
            }
        }

        control.Paused = true;
        SurfaceHost.Children.Remove(control);
    }

    // ---- lifetime -----------------------------------------------------------------------------------

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        EnsureControl();
        if (XamlRoot is { } root)
        {
            root.Changed -= OnXamlRootChanged;
            root.Changed += OnXamlRootChanged;
        }

        ApplyDpiCap();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (XamlRoot is { } root)
        {
            root.Changed -= OnXamlRootChanged;
        }
    }

    private void OnXamlRootChanged(XamlRoot sender, XamlRootChangedEventArgs args) => ApplyDpiCap();

    private void OnActualThemeChanged(FrameworkElement sender, object args)
    {
        PublishPalette();
        if (_control is { } control)
        {
            _ = control.RunOnGameLoopThreadAsync(() =>
            {
                if (_renderer is not { } renderer)
                {
                    return;
                }
                renderer.ReleaseBrushes();
                renderer.CreateResources(control);
            });
            control.Invalidate();
        }
    }

    private void ApplyDpiCap()
    {
        if (_control is null)
        {
            return;
        }

        var rasterization = XamlRoot?.RasterizationScale ?? 1.0;
        _control.DpiScale = (float)Math.Clamp(GalaxyCanvas.MaxDpiScale / Math.Max(rasterization, 0.1), 0.25, 1.0);
    }

    // ---- game loop ----------------------------------------------------------------------------------

    private void OnCreateResources(CanvasAnimatedControl sender, CanvasCreateResourcesEventArgs args) =>
        _renderer?.CreateResources(sender);

    private void OnUpdate(ICanvasAnimatedControl sender, CanvasAnimatedUpdateEventArgs args)
    {
        if (Scene is not { } scene)
        {
            return;
        }

        try
        {
            scene.Update(args.Timing.ElapsedTime);
        }
        catch (Exception ex)
        {
            Fail(sender, ex, "Advancing the target paths failed");
        }
    }

    private void OnDraw(ICanvasAnimatedControl sender, CanvasAnimatedDrawEventArgs args)
    {
        if (Scene is not { } scene || _renderer is not { } renderer)
        {
            return;
        }

        try
        {
            renderer.Draw(args.DrawingSession, scene);
        }
        catch (Exception ex)
        {
            Fail(sender, ex, "Drawing the target paths failed");
        }
    }

    private void Fail(ICanvasAnimatedControl sender, Exception exception, string message)
    {
        _logger?.LogWarning(exception, "{Message}; switching to the plain list", message);
        _drawFailed = true;
        sender.Paused = true;
        DispatcherQueue.TryEnqueue(() => DrawFailed?.Invoke(this, EventArgs.Empty));
    }

    private void OnSurfaceSizeChanged(object sender, SizeChangedEventArgs e)
    {
        ApplyDpiCap();
        PublishSize();
    }

    private void PublishSize() =>
        Scene?.Enqueue(new ResizeTargetPaths((float)SurfaceHost.ActualWidth, (float)SurfaceHost.ActualHeight));
}
