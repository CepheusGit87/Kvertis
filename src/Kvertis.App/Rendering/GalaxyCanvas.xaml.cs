using System.Numerics;
using Kvertis.App.Scenes;
using Kvertis.Engine.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Graphics.Canvas.UI;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Kvertis.App.Rendering;

/// <summary>
/// The shell around <see cref="CanvasAnimatedControl"/> (ADR-022). It owns the drawing control and the
/// renderer, but never the scene: the scene belongs to the view model and survives this control being torn
/// down and built again.
/// </summary>
/// <remarks>
/// Thread rule: <c>Update</c> and <c>Draw</c> run on the Win2D game loop thread and are the only place the
/// scene is touched. The UI thread uses <see cref="GalaxyScene.Enqueue"/>, the published snapshot and the pure
/// <see cref="GalaxyScene.OrbitAt"/>, nothing else.
/// </remarks>
public sealed partial class GalaxyCanvas : UserControl, IDisposable
{
    /// <summary>Highest device pixel ratio the surface is drawn at (worksheet: at most 2x).</summary>
    public const double MaxDpiScale = 2.0;

    private static readonly TimeSpan FrameTime = TimeSpan.FromSeconds(1.0 / 60.0);

    /// <summary>How long a retiring control may take to hand back its renderer on the game loop thread.</summary>
    private static readonly TimeSpan RetireTimeout = TimeSpan.FromMilliseconds(500);

    private readonly ILogger<GalaxyCanvas>? _logger;

    private CanvasAnimatedControl? _control;
    private GalaxyRenderer? _renderer;
    private bool _paused;
    private bool _drawFailed;
    private bool _surfaceAffected;

    public GalaxyCanvas()
    {
        InitializeComponent();
        _logger = App.Services.GetService<ILoggerFactory>()?.CreateLogger<GalaxyCanvas>();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        ActualThemeChanged += OnActualThemeChanged;
    }

    /// <summary>Raised on the UI thread when a click landed on an orbit; null means the hole (back to overview).</summary>
    public event EventHandler<MediaKind?>? ZoomRequested;

    /// <summary>Raised on the UI thread after a drawing error; the host then switches to the static view.</summary>
    public event EventHandler? DrawFailed;

    /// <summary>
    /// Raised on the UI thread when the scene starts (true) or stops (false) pulling the XAML surface into the
    /// hole (big bang gimmick). The host then moves trays and cards with Composition properties from the snapshot.
    /// </summary>
    public event EventHandler<bool>? SurfaceEffectChanged;

    /// <summary>The scene to draw. Set once by the host; changing it while running is allowed.</summary>
    public GalaxyScene? Scene { get; set; }

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
    /// Unpauses the loop and builds the drawing control if there is none yet. Idempotent: the cached page may
    /// enter and leave as often as it likes, it always keeps the one control (Nachtrag zu ADR-022).
    /// </summary>
    public void Resume()
    {
        _paused = false;
        EnsureControl();
    }

    /// <summary>
    /// Pauses the loop while another step is shown. The control stays: the page is cached, and taking a Win2D
    /// control out of the tree for good is not possible with Win2D 1.4.0 (Nachtrag zu ADR-022).
    /// </summary>
    public void Suspend() => Paused = true;

    /// <summary>
    /// Tears the surface down for good: only for a change of the motion setting or after a drawing error.
    /// Everyday navigation uses <see cref="Suspend"/>. The renderer is disposed on the game loop thread while
    /// the loop still runs, then the control is paused and leaves its parent. The returned task ends when all
    /// of that is done (or after a short timeout if the loop no longer runs).
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

    /// <summary>Same as <see cref="DestroyAsync"/>: everything the control owns is a Win2D object.</summary>
    public void Dispose() => _ = DestroyAsync();

    /// <summary>Re-reads the theme colours and hands them to the scene.</summary>
    public void PublishPalette()
    {
        if (Scene is { } scene)
        {
            scene.Palette = ScenePaletteReader.Read(this);
        }
    }

    /// <summary>Builds the drawing control once; keeps the current pause state.</summary>
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

        _renderer = new GalaxyRenderer();
        _control = new CanvasAnimatedControl
        {
            TargetElapsedTime = FrameTime,
            IsFixedTimeStep = false,
            ClearColor = Microsoft.UI.Colors.Transparent,
            IsTabStop = false,
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

    private async Task RetireAsync(CanvasAnimatedControl control, GalaxyRenderer? renderer)
    {
        // The renderer's brushes belong to the device of the game loop, so they are disposed there, and
        // before the loop is paused, so the action still gets its turn.
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
                        _logger?.LogDebug(ex, "Disposing the galaxy renderer failed");
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
        // Deviation from the worksheet (Nachtrag zu ADR-022): RemoveFromVisualTree() of Win2D 1.4.0 crashes
        // this Windows App SDK (access violation in Microsoft.Graphics.Canvas.dll, reproduced with an empty
        // Draw). The paused control simply leaves its parent instead; this only happens on a change of the
        // motion setting or after a drawing error, never on navigation.
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

    /// <summary>
    /// The page is cached: leaving it unloads this control, but the drawing control stays for the next visit.
    /// Win2D stops its loop by itself while unloaded.
    /// </summary>
    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (XamlRoot is { } root)
        {
            root.Changed -= OnXamlRootChanged;
        }
    }

    /// <summary>The window moved to a screen with a different scaling: the DPI cap has to follow.</summary>
    private void OnXamlRootChanged(XamlRoot sender, XamlRootChangedEventArgs args) => ApplyDpiCap();

    private void OnActualThemeChanged(FrameworkElement sender, object args)
    {
        PublishPalette();
        // The cached gradient brushes carry the old palette. They are dropped and built again in one go on
        // the game loop thread: releasing alone would leave the renderer without a device and draw nothing.
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
        _control.DpiScale = (float)Math.Clamp(MaxDpiScale / Math.Max(rasterization, 0.1), 0.25, 1.0);
    }

    // ---- game loop ----------------------------------------------------------------------------------

    private void OnCreateResources(CanvasAnimatedControl sender, CanvasCreateResourcesEventArgs args)
    {
        // Runs again after a device loss, so everything the renderer holds is rebuilt from scratch here.
        _renderer?.CreateResources(sender);
    }

    private void OnUpdate(ICanvasAnimatedControl sender, CanvasAnimatedUpdateEventArgs args)
    {
        if (Scene is not { } scene)
        {
            return;
        }

        try
        {
            scene.Update(args.Timing.ElapsedTime);
            var affected = scene.Snapshot.SurfaceAffected;
            if (affected != _surfaceAffected)
            {
                _surfaceAffected = affected;
                DispatcherQueue.TryEnqueue(() => SurfaceEffectChanged?.Invoke(this, affected));
            }
        }
        catch (Exception ex)
        {
            Fail(sender, ex, "Advancing the galaxy failed");
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
            Fail(sender, ex, "Drawing the galaxy failed");
        }
    }

    /// <summary>An error on the game loop thread must never take the app down: the host shows the still picture.</summary>
    private void Fail(ICanvasAnimatedControl sender, Exception exception, string message)
    {
        _logger?.LogWarning(exception, "{Message}; switching to the static view", message);
        _drawFailed = true;
        sender.Paused = true;
        DispatcherQueue.TryEnqueue(() => DrawFailed?.Invoke(this, EventArgs.Empty));
    }

    // ---- input --------------------------------------------------------------------------------------

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(SurfaceHost).Position;
        Scene?.Enqueue(new SetPointer(new Vector2((float)point.X, (float)point.Y)));
    }

    private void OnPointerExited(object sender, PointerRoutedEventArgs e) => Scene?.Enqueue(new SetPointer(null));

    private void OnSurfaceTapped(object sender, TappedRoutedEventArgs e)
    {
        if (Scene is not { } scene)
        {
            return;
        }

        var point = e.GetPosition(SurfaceHost);
        var position = new Vector2((float)point.X, (float)point.Y);

        // A click on the hole always means "back to the overview".
        if (Vector2.Distance(position, scene.Layout.Center) <= scene.HoleRadius + 12f * scene.Layout.Scale)
        {
            ZoomRequested?.Invoke(this, null);
            return;
        }

        if (scene.OrbitAt(position) is { } orbit)
        {
            ZoomRequested?.Invoke(this, GalaxyLayout.KindOf(orbit));
        }
    }

    private void OnSurfaceSizeChanged(object sender, SizeChangedEventArgs e)
    {
        ApplyDpiCap();
        PublishSize();
    }

    private void PublishSize() =>
        Scene?.Enqueue(new Resize((float)SurfaceHost.ActualWidth, (float)SurfaceHost.ActualHeight));
}
