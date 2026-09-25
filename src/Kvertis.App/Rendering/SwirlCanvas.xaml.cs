using Kvertis.App.Scenes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Graphics.Canvas.UI;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Kvertis.App.Rendering;

/// <summary>
/// The shell around <see cref="CanvasAnimatedControl"/> for step 3 (ADR-023), built like <see cref="GalaxyCanvas"/>.
/// It owns the drawing control and the renderer, but never the scene: the scene belongs to the view model and
/// survives this control being torn down and built again.
/// </summary>
/// <remarks>
/// Thread rule: <c>Update</c> and <c>Draw</c> run on the Win2D game loop thread and are the only place the
/// scene is touched. The UI thread uses <see cref="SwirlScene.Enqueue"/> and the published snapshot; the two
/// events of this control (shake, report) are raised on the UI thread from what the loop saw in the snapshot.
/// </remarks>
public sealed partial class SwirlCanvas : UserControl, IDisposable
{
    /// <summary>Highest device pixel ratio the surface is drawn at (worksheet: at most 2x).</summary>
    public const double MaxDpiScale = 2.0;

    private static readonly TimeSpan FrameTime = TimeSpan.FromSeconds(1.0 / 60.0);

    /// <summary>How long a retiring control may take to hand back its renderer on the game loop thread.</summary>
    private static readonly TimeSpan RetireTimeout = TimeSpan.FromMilliseconds(500);

    private readonly ILogger<SwirlCanvas>? _logger;

    private CanvasAnimatedControl? _control;
    private SwirlRenderer? _renderer;
    private bool _paused;
    private bool _drawFailed;
    private int _shakesRaised;
    private bool _reportRaised;

#if DEBUG
    // Frame statistics (KVERTIS_GALAXY_STATS=1, Debug builds only): the time between consecutive Draw calls,
    // the mean over the last 60 consecutive draws and the worst frame, written every 5 s to logs/stats.log.
    private static readonly bool StatsEnabled = Environment.GetEnvironmentVariable("KVERTIS_GALAXY_STATS") == "1";
    private static readonly TimeSpan StatsInterval = TimeSpan.FromSeconds(5);
    private readonly double[] _statsWindow = new double[60];
    private long _statsLastDraw;
    private long _statsLastLog;
    private int _statsFrames;
    private double _statsSum;
    private double _statsMax;
#endif

    public SwirlCanvas()
    {
        InitializeComponent();
        _logger = App.Services.GetService<ILoggerFactory>()?.CreateLogger<SwirlCanvas>();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        ActualThemeChanged += OnActualThemeChanged;
    }

    /// <summary>Raised on the UI thread exactly once per finale when the supernova goes off (the page shakes).</summary>
    public event EventHandler? ShakeRequested;

    /// <summary>Raised on the UI thread once per finale when the report may appear (e ≥ 1.2 s).</summary>
    public event EventHandler? ReportReached;

    /// <summary>Raised on the UI thread after a drawing error; the host then switches to the static view.</summary>
    public event EventHandler? DrawFailed;

    /// <summary>The scene to draw. Set once by the host; changing it while running is allowed.</summary>
    public SwirlScene? Scene { get; set; }

    /// <summary>Format of the counter suffix under the white hole ("von {0}"), from the resources.</summary>
    public string CounterSuffixFormat { get; set; } = string.Empty;

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

    /// <summary>Unpauses the loop and builds the drawing control if there is none yet. Idempotent.</summary>
    public void Resume()
    {
        _paused = false;
        EnsureControl();
    }

    /// <summary>Pauses the loop while another step is shown. The control stays (Nachtrag zu ADR-022).</summary>
    public void Suspend() => Paused = true;

    /// <summary>
    /// Tears the surface down for good: only for a change of the motion setting or after a drawing error.
    /// The renderer is disposed on the game loop thread while the loop still runs, then the control is paused
    /// and leaves its parent.
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

        _renderer = new SwirlRenderer { CounterSuffixFormat = CounterSuffixFormat };
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

    private async Task RetireAsync(CanvasAnimatedControl control, SwirlRenderer? renderer)
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
                        _logger?.LogDebug(ex, "Disposing the swirl renderer failed");
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
        // RemoveFromVisualTree() of Win2D 1.4.0 crashes this Windows App SDK (Nachtrag zu ADR-022); the paused
        // control simply leaves its parent instead.
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
        _control.DpiScale = (float)Math.Clamp(MaxDpiScale / Math.Max(rasterization, 0.1), 0.25, 1.0);
    }

    // ---- game loop ----------------------------------------------------------------------------------

    private void OnCreateResources(CanvasAnimatedControl sender, CanvasCreateResourcesEventArgs args)
    {
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
            var snapshot = scene.Snapshot;
            if (snapshot.FinalePhase is null)
            {
                _shakesRaised = 0;
                _reportRaised = false;
                return;
            }

            // The snapshot counts the shakes, so a frame that was skipped cannot lose one.
            if (snapshot.ShakeCount > _shakesRaised)
            {
                _shakesRaised = snapshot.ShakeCount;
                DispatcherQueue.TryEnqueue(() => ShakeRequested?.Invoke(this, EventArgs.Empty));
            }

            if (snapshot.ShowReport && !_reportRaised)
            {
                _reportRaised = true;
                DispatcherQueue.TryEnqueue(() => ReportReached?.Invoke(this, EventArgs.Empty));
            }
        }
        catch (Exception ex)
        {
            Fail(sender, ex, "Advancing the swirl failed");
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
#if DEBUG
            if (StatsEnabled)
            {
                RecordFrame(scene);
            }
#endif
        }
        catch (Exception ex)
        {
            Fail(sender, ex, "Drawing the swirl failed");
        }
    }

#if DEBUG
    private void RecordFrame(SwirlScene scene)
    {
        var now = System.Diagnostics.Stopwatch.GetTimestamp();
        if (_statsLastDraw != 0)
        {
            var ms = (now - _statsLastDraw) * 1000d / System.Diagnostics.Stopwatch.Frequency;
            // A gap over a second is a pause, not a frame.
            if (ms < 1000d)
            {
                _statsWindow[_statsFrames % _statsWindow.Length] = ms;
                _statsFrames++;
                _statsSum += ms;
                _statsMax = Math.Max(_statsMax, ms);
            }
        }

        _statsLastDraw = now;
        if (_statsLastLog == 0)
        {
            _statsLastLog = now;
            return;
        }

        if (TimeSpan.FromSeconds((now - _statsLastLog) / (double)System.Diagnostics.Stopwatch.Frequency) < StatsInterval || _statsFrames == 0)
        {
            return;
        }

        var window = Math.Min(_statsFrames, _statsWindow.Length);
        var windowSum = 0d;
        for (var i = 0; i < window; i++)
        {
            windowSum += _statsWindow[i];
        }

        var line = string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            "{0:yyyy-MM-dd HH:mm:ss.fff} swirl frames={1} ms/frame mean={2:F2} last60={3:F2} max={4:F2} particles={5} finale={6}",
            DateTime.Now,
            _statsFrames,
            _statsSum / _statsFrames,
            windowSum / window,
            _statsMax,
            scene.ParticleCount,
            scene.Snapshot.FinalePhase?.ToString() ?? "-");
        try
        {
            Directory.CreateDirectory(Services.AppPaths.LogsFolder);
            File.AppendAllText(Path.Combine(Services.AppPaths.LogsFolder, "stats.log"), line + Environment.NewLine);
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Writing the frame statistics failed");
        }

        _statsLastLog = now;
        _statsFrames = 0;
        _statsSum = 0d;
        _statsMax = 0d;
    }
#endif

    /// <summary>An error on the game loop thread must never take the app down: the host shows the XAML view.</summary>
    private void Fail(ICanvasAnimatedControl sender, Exception exception, string message)
    {
        _logger?.LogWarning(exception, "{Message}; switching to the static view", message);
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
        Scene?.Enqueue(new SwirlResize((float)SurfaceHost.ActualWidth, (float)SurfaceHost.ActualHeight));
}
