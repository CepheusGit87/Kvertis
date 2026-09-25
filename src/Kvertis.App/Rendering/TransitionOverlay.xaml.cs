using Kvertis.App.Scenes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Graphics.Canvas.UI;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Kvertis.App.Rendering;

/// <summary>
/// The shell around the overlay's <see cref="CanvasAnimatedControl"/> (ADR-023). One control for the life of
/// the window: built on the first use (or by <see cref="Prepare"/> at start), paused and transparent while
/// idle, running only during a transition. <c>RemoveFromVisualTree</c> is never called (Nachtrag zu ADR-022).
/// </summary>
/// <remarks>
/// Thread rule as in <see cref="GalaxyCanvas"/>: <c>Update</c> and <c>Draw</c> run on the game loop thread and
/// are the only place the scene is advanced; the UI thread hands scenes over through fields written under a
/// lock and reads nothing but the events raised back on the dispatcher.
/// </remarks>
public sealed partial class TransitionOverlay : UserControl, IDisposable
{
    /// <summary>Highest device pixel ratio the surface is drawn at (worksheet: at most 2x).</summary>
    public const double MaxDpiScale = 2.0;

    private static readonly TimeSpan FrameTime = TimeSpan.FromSeconds(1.0 / 60.0);

    /// <summary>How long <see cref="HoldAsync"/> waits for the first frame before it goes on without it.</summary>
    private static readonly TimeSpan HoldTimeout = TimeSpan.FromMilliseconds(120);

    private readonly ILogger<TransitionOverlay>? _logger;
    private readonly object _gate = new();

    private CanvasAnimatedControl? _control;
    private TransitionRenderer? _renderer;
    private TransitionScene? _scene;
    private bool _running;
    private bool _flightsEndedRaised;
    private bool _completedRaised;
    private bool _drawFailed;
    private TaskCompletionSource? _firstFrame;

    public TransitionOverlay()
    {
        InitializeComponent();
        _logger = App.Services.GetService<ILoggerFactory>()?.CreateLogger<TransitionOverlay>();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        ActualThemeChanged += OnActualThemeChanged;
    }

    /// <summary>Raised on the UI thread once the flights of the current scene ended (ghosts gone, hole fading).</summary>
    public event EventHandler? FlightsEnded;

    /// <summary>Raised on the UI thread once the current scene is finished.</summary>
    public event EventHandler? Completed;

    /// <summary>Raised on the UI thread after a drawing error; the overlay stays unusable afterwards.</summary>
    public event EventHandler? DrawFailed;

    /// <summary>True until a drawing error happened.</summary>
    public bool IsUsable => !_drawFailed;

    /// <summary>The theme colours as the scene needs them, including the ghost card tokens.</summary>
    public ScenePalette ReadPalette()
    {
        var palette = ScenePaletteReader.Read(this);
        return palette with
        {
            Panel = ReadToken("Panel") ?? palette.Panel,
            Line = ReadToken("Line") ?? palette.Line,
        };
    }

    /// <summary>Builds the drawing control ahead of the first transition, so the device exists when it is needed.</summary>
    public void Prepare() => EnsureControl();

    /// <summary>
    /// Shows <paramref name="scene"/> at its first frame without advancing time. The task completes once that
    /// frame was drawn (or after a short timeout), so the caller can swap the pages underneath.
    /// </summary>
    public Task HoldAsync(TransitionScene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        if (_drawFailed)
        {
            return Task.CompletedTask;
        }

        EnsureControl();
        var first = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            _scene = scene;
            _running = false;
            _flightsEndedRaised = false;
            _completedRaised = false;
            _firstFrame = first;
        }

        Opacity = 1;
        if (_control is { } control)
        {
            control.Paused = false;
            control.Invalidate();
        }

        // The continuation lands on the UI thread again through the dispatcher in OnDraw; the timeout keeps
        // a lost device from stalling the transition.
        return Task.WhenAny(first.Task, Task.Delay(HoldTimeout));
    }

    /// <summary>Replaces the held scene by <paramref name="scene"/> and lets time run.</summary>
    public void Start(TransitionScene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        if (_drawFailed)
        {
            return;
        }

        EnsureControl();
        lock (_gate)
        {
            _scene = scene;
            _running = true;
            _flightsEndedRaised = false;
            _completedRaised = false;
        }

        Opacity = 1;
        if (_control is { } control)
        {
            control.Paused = false;
        }
    }

    /// <summary>Drops the scene, draws one empty frame and pauses. Idempotent.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _scene = null;
            _running = false;
            _firstFrame?.TrySetResult();
            _firstFrame = null;
        }

        Opacity = 0;
        if (_control is { } control)
        {
            // A paused CanvasAnimatedControl still draws once on Invalidate: that frame clears the surface.
            control.Paused = true;
            control.Invalidate();
        }
    }

    public void Dispose()
    {
        var control = _control;
        _control = null;
        var renderer = _renderer;
        _renderer = null;
        if (control is null)
        {
            return;
        }

        control.CreateResources -= OnCreateResources;
        control.Update -= OnUpdate;
        control.Draw -= OnDraw;
        _ = RetireAsync(control, renderer);
    }

    private void EnsureControl()
    {
        if (_drawFailed || _control is not null)
        {
            return;
        }

        _renderer = new TransitionRenderer();
        _control = new CanvasAnimatedControl
        {
            TargetElapsedTime = FrameTime,
            IsFixedTimeStep = false,
            ClearColor = Microsoft.UI.Colors.Transparent,
            IsTabStop = false,
            IsHitTestVisible = false,
            Paused = true,
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAccessibilityView(
            _control, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        _control.CreateResources += OnCreateResources;
        _control.Update += OnUpdate;
        _control.Draw += OnDraw;
        ApplyDpiCap();
        SurfaceHost.Children.Add(_control);
    }

    private async Task RetireAsync(CanvasAnimatedControl control, TransitionRenderer? renderer)
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
                        _logger?.LogDebug(ex, "Disposing the transition renderer failed");
                    }
                }).AsTask();
                await Task.WhenAny(disposed, Task.Delay(TimeSpan.FromMilliseconds(500)));
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "The game loop did not take the transition renderer back");
            }
        }

        control.Paused = true;
        SurfaceHost.Children.Remove(control);
    }

    private SceneColor? ReadToken(string token)
    {
        if (Resources.TryGetValue(ScenePaletteReader.KeyPrefix + token, out var value) && value is SolidColorBrush brush)
        {
            return new SceneColor(brush.Color.R, brush.Color.G, brush.Color.B);
        }

        return null;
    }

    // ---- lifetime -----------------------------------------------------------------------------------

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
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
        // The service ends a running transition on a theme change; the brushes are rebuilt for the next one.
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
        }
    }

    private void OnSurfaceSizeChanged(object sender, SizeChangedEventArgs e) => ApplyDpiCap();

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

    private void OnCreateResources(CanvasAnimatedControl sender, CanvasCreateResourcesEventArgs args) =>
        _renderer?.CreateResources(sender);

    private void OnUpdate(ICanvasAnimatedControl sender, CanvasAnimatedUpdateEventArgs args)
    {
        TransitionScene? scene;
        bool running;
        lock (_gate)
        {
            scene = _scene;
            running = _running;
        }

        if (scene is null || !running)
        {
            return;
        }

        try
        {
            scene.Update(args.Timing.ElapsedTime);
        }
        catch (Exception ex)
        {
            Fail(sender, ex, "Advancing the transition failed");
            return;
        }

        var flightsEnded = scene.Time.TotalSeconds >= scene.FlightsEnd + TransitionScene.CleanUpDelay;
        var completed = scene.IsFinished;
        bool raiseFlights = false, raiseCompleted = false;
        lock (_gate)
        {
            if (_scene != scene)
            {
                return;
            }

            if (flightsEnded && !_flightsEndedRaised)
            {
                _flightsEndedRaised = true;
                raiseFlights = true;
            }

            if (completed && !_completedRaised)
            {
                _completedRaised = true;
                _running = false;
                raiseCompleted = true;
            }
        }

        if (raiseFlights)
        {
            DispatcherQueue.TryEnqueue(() => FlightsEnded?.Invoke(this, EventArgs.Empty));
        }

        if (raiseCompleted)
        {
            DispatcherQueue.TryEnqueue(() => Completed?.Invoke(this, EventArgs.Empty));
        }
    }

    private void OnDraw(ICanvasAnimatedControl sender, CanvasAnimatedDrawEventArgs args)
    {
        TransitionScene? scene;
        TaskCompletionSource? first;
        lock (_gate)
        {
            scene = _scene;
            first = _firstFrame;
            _firstFrame = null;
        }

        if (scene is not null && _renderer is { } renderer)
        {
            try
            {
                renderer.Draw(args.DrawingSession, scene);
            }
            catch (Exception ex)
            {
                Fail(sender, ex, "Drawing the transition failed");
            }
        }

        first?.TrySetResult();
    }

    /// <summary>An error on the game loop thread must never take the app down: the service falls back to plain switches.</summary>
    private void Fail(ICanvasAnimatedControl sender, Exception exception, string message)
    {
        _logger?.LogWarning(exception, "{Message}; transitions are plain page switches from now on", message);
        _drawFailed = true;
        sender.Paused = true;
        lock (_gate)
        {
            _scene = null;
            _running = false;
            _firstFrame?.TrySetResult();
            _firstFrame = null;
        }

        DispatcherQueue.TryEnqueue(() =>
        {
            Opacity = 0;
            DrawFailed?.Invoke(this, EventArgs.Empty);
        });
    }
}
