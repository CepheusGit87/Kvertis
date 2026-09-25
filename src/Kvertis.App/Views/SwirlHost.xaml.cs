using Kvertis.App.Rendering;
using Kvertis.App.Services;
using Kvertis.App.ViewModels.Convert;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Kvertis.App.Views;

/// <summary>
/// The only place that decides what the middle of step 3 shows (ADR-023): the moving Win2D surface with
/// swirl, white hole and finale; with reduced motion the static ring after the round; in high contrast
/// nothing but the cards (the kind colours collapse, ADR-017). Built like <see cref="GalaxyHost"/>.
/// </summary>
public sealed partial class SwirlHost : UserControl
{
    private readonly IMotionSettings _motion;
    private readonly ILocalizer _loc;

    private SwirlCanvas? _canvas;
    private FinaleStaticView? _still;
    private bool _drawingFailed;
    private bool _suspended = true;
    private bool _paused;
    private bool _built;

    public SwirlHost()
    {
        _motion = App.Services.GetRequiredService<IMotionSettings>();
        _loc = App.Services.GetRequiredService<ILocalizer>();
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    /// <summary>Raised on the UI thread when the supernova goes off: the page runs the XAML shake.</summary>
    public event EventHandler? ShakeRequested;

    /// <summary>Raised after the host switched between moving surface, static view and nothing (see <see cref="IsAnimated"/>).</summary>
    public event EventHandler? SurfaceModeChanged;

    /// <summary>The view model of step 3. Set from the page before the host is loaded.</summary>
    public ConvertPageViewModel ViewModel { get; set; } = App.Services.GetRequiredService<ConvertPageViewModel>();

    /// <summary>The layer the page measures anchors against (the surface, without the cards).</summary>
    public UIElement Surface => SurfaceLayer;

    /// <summary>The report card, for the page's entrance animation.</summary>
    public UIElement ReportCardElement => ReportCard;

    /// <summary>True while the moving surface is shown (the page then hides the inbox during the finale).</summary>
    public bool IsAnimated => _canvas is not null;

    /// <summary>
    /// True while something else (a transition overlay) draws; the swirl loop pauses meanwhile so only one
    /// <c>CanvasAnimatedControl</c> runs at a time (worksheet "Leistung"). Set by the transition service.
    /// </summary>
    public bool IsPaused
    {
        get => _paused;
        set => SetPaused(value);
    }

    /// <summary>Starts the drawing loop (page entered, host loaded). Idempotent.</summary>
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

    /// <summary>Pauses the loop while another step is shown; the surface stays (Nachtrag zu ADR-022).</summary>
    public void Suspend()
    {
        _suspended = true;
        ApplyPaused();
    }

    /// <summary>Pauses the loop without destroying anything (window hidden, overlay running).</summary>
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

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _motion.Changed -= OnMotionChanged;
        Suspend();
    }

    private void OnMotionChanged(object? sender, EventArgs e) => Rebuild();

    /// <summary>What the current settings ask for: 0 nothing, 1 the moving surface, 2 the static ring.</summary>
    private int WantedSurface() =>
        _motion.IsHighContrast ? 0 : _motion.AnimationsEnabled && !_drawingFailed ? 1 : 2;

    private int CurrentSurface() => _canvas is not null ? 1 : _still is not null ? 2 : 0;

    private bool SurfaceMatchesSettings() => WantedSurface() == CurrentSurface();

    /// <summary>Puts exactly the one control into the surface layer that the current settings ask for.</summary>
    public void Rebuild()
    {
        _built = true;
        DropSurface();
        var wanted = WantedSurface();
        switch (wanted)
        {
            case 1:
                var canvas = new SwirlCanvas
                {
                    Scene = ViewModel.Scene,
                    CounterSuffixFormat = _loc.Get("Swirl_Counter_Of"),
                };
                canvas.ShakeRequested += OnCanvasShakeRequested;
                canvas.ReportReached += OnCanvasReportReached;
                canvas.DrawFailed += OnCanvasDrawFailed;
                _canvas = canvas;
                ApplyPaused();
                SurfaceLayer.Children.Add(canvas);
                break;
            case 2:
                var still = new FinaleStaticView { ViewModel = ViewModel };
                _still = still;
                SurfaceLayer.Children.Add(still);
                break;
        }

        // With the moving surface the running files show only in the swirl and in the list (draft); without it
        // the progress cards and the hint stand in the middle of the stage.
        StatusStack.Visibility = wanted == 1 ? Visibility.Collapsed : Visibility.Visible;
        ViewModel.AnimatedFinale = wanted == 1;
        SurfaceModeChanged?.Invoke(this, EventArgs.Empty);
    }

    private void DropSurface()
    {
        if (_canvas is { } canvas)
        {
            canvas.ShakeRequested -= OnCanvasShakeRequested;
            canvas.ReportReached -= OnCanvasReportReached;
            canvas.DrawFailed -= OnCanvasDrawFailed;
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

    private async Task RetireAsync(SwirlCanvas canvas)
    {
        await canvas.DestroyAsync();
        SurfaceLayer.Children.Remove(canvas);
    }

    private void OnCanvasShakeRequested(object? sender, EventArgs e) => ShakeRequested?.Invoke(this, EventArgs.Empty);

    private void OnCanvasReportReached(object? sender, EventArgs e) => ViewModel.OnFinaleReportReached();

    private void OnCanvasDrawFailed(object? sender, EventArgs e)
    {
        // Once drawing failed the app never tries again in this session; the cards carry the same information.
        _drawingFailed = true;
        Rebuild();
    }
}
