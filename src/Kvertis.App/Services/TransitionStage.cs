using Kvertis.App.Rendering;
using Kvertis.App.Scenes;
using Kvertis.App.ViewModels;
using Kvertis.Engine.Abstractions;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;

namespace Kvertis.App.Services;

/// <summary>
/// <see cref="ITransitionStage"/> over the main window: the frame with its step pages, the step header and
/// the <see cref="TransitionOverlay"/> (ADR-023). Built by the window, handed to <see cref="TransitionService"/>.
/// </summary>
public sealed class TransitionStage : ITransitionStage
{
    /// <summary>The cross-fade of a plain step change (3→2).</summary>
    private static readonly TimeSpan CrossFade = TimeSpan.FromMilliseconds(240);

    /// <summary>How long one layout pass may take before the target is measured anyway.</summary>
    private static readonly TimeSpan LayoutTimeout = TimeSpan.FromMilliseconds(40);

    private readonly Frame _frame;
    private readonly TransitionOverlay _overlay;
    private readonly UIElement _header;
    private readonly FrameNavigationService _navigation;
    private readonly HistoryViewModel _history;

    private ITransitionAnchors? _sourcePage;
    private FrameworkElement? _targetPage;
    private Storyboard? _fade;
    private bool _windowVisible = true;

    public TransitionStage(Frame frame, TransitionOverlay overlay, UIElement header, FrameNavigationService navigation, HistoryViewModel history)
    {
        _frame = frame ?? throw new ArgumentNullException(nameof(frame));
        _overlay = overlay ?? throw new ArgumentNullException(nameof(overlay));
        _header = header ?? throw new ArgumentNullException(nameof(header));
        _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
        _history = history ?? throw new ArgumentNullException(nameof(history));
        _overlay.FlightsEnded += (_, _) => FlightsEnded?.Invoke(this, EventArgs.Empty);
        _overlay.Completed += (_, _) => Completed?.Invoke(this, EventArgs.Empty);
        _overlay.DrawFailed += (_, _) => DrawFailed?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? FlightsEnded;

    public event EventHandler? Completed;

    public event EventHandler? DrawFailed;

    public bool CanBegin => _overlay.IsUsable && _windowVisible && !_history.IsOpen;

    public ScenePalette Palette => _overlay.ReadPalette();

    /// <summary>The window tells the stage when it is hidden; a hidden window gets no transitions.</summary>
    public void SetWindowVisible(bool visible) => _windowVisible = visible;

    public TransitionAnchorSet? MeasureCurrent()
    {
        _sourcePage = _frame.Content as ITransitionAnchors;
        return _sourcePage?.MeasureAnchors(_overlay);
    }

    public void Navigate(WorkflowStep step, bool hidden)
    {
        _navigation.Navigate(StepNavigationService.PageOf(step), keepBackStack: false, suppressTransition: true);
        _targetPage = _frame.Content as FrameworkElement;
        if (hidden && _targetPage is { } page)
        {
            page.Opacity = 0;
        }
    }

    public void NavigateFaded(WorkflowStep step)
    {
        End();
        Navigate(step, hidden: true);
        if (_targetPage is { } page)
        {
            RunFade(page, TimeSpan.Zero, CrossFade);
        }
    }

    public async Task<TransitionAnchorSet?> MeasureTargetAsync(TransitionAnchorKind required)
    {
        if (_targetPage is not { } page)
        {
            return null;
        }

        for (var attempt = 0; attempt < 2; attempt++)
        {
            await NextLayoutAsync(page);
            if (!ReferenceEquals(_frame.Content, page))
            {
                return null;
            }

            // The hole alone is not enough: the list containers of step 2 appear one layout pass later.
            var anchors = (page as ITransitionAnchors)?.MeasureAnchors(_overlay);
            if (anchors is not null && anchors.Find(required) is not null)
            {
                return anchors;
            }
        }

        return null;
    }

    public void SetFlightVisibility(TransitionSide side, bool visible, IReadOnlySet<MediaKind> kinds)
    {
        var page = side == TransitionSide.Source ? _sourcePage : _targetPage as ITransitionAnchors;
        page?.SetFlightVisibility(visible, kinds);
    }

    public void SetInputLocked(bool locked)
    {
        // Pointer and keyboard: a disabled frame takes no focus and no keys while its page is invisible anyway.
        _frame.IsHitTestVisible = !locked;
        _frame.IsEnabled = !locked;
        _header.IsHitTestVisible = !locked;
    }

    public Task HoldAsync(TransitionScene scene) => _overlay.HoldAsync(scene);

    public void Start(TransitionScene scene)
    {
        _overlay.Start(scene);
        if (_targetPage is { } page)
        {
            var lead = Math.Max(0f, scene.FlightsEnd - TransitionScene.PageFadeLead);
            RunFade(page, TimeSpan.FromSeconds(lead), TimeSpan.FromSeconds(TransitionScene.PageFadeLength));
        }
    }

    public void End()
    {
        _overlay.Clear();
        StopFade();
        if (_targetPage is { } page)
        {
            page.Opacity = 1;
        }
    }

    /// <summary>Opacity 0 → 1 on <paramref name="page"/>, starting after <paramref name="delay"/>. Independent animation, no layout.</summary>
    private void RunFade(FrameworkElement page, TimeSpan delay, TimeSpan length)
    {
        StopFade();
        var animation = new DoubleAnimation
        {
            From = 0,
            To = 1,
            BeginTime = delay,
            Duration = new Duration(length),
            EnableDependentAnimation = false,
        };
        Storyboard.SetTarget(animation, page);
        Storyboard.SetTargetProperty(animation, "Opacity");
        var storyboard = new Storyboard();
        storyboard.Children.Add(animation);
        storyboard.Completed += (_, _) =>
        {
            if (_fade == storyboard)
            {
                _fade = null;
                storyboard.Stop();
                page.Opacity = 1;
            }
        };
        _fade = storyboard;
        storyboard.Begin();
    }

    private void StopFade()
    {
        if (_fade is { } fade)
        {
            _fade = null;
            fade.Stop();
        }
    }

    /// <summary>Completes after the page's next layout pass (Loaded first if it is not in the tree yet), or after a short timeout.</summary>
    private static Task NextLayoutAsync(FrameworkElement page)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var queue = page.DispatcherQueue;
        DispatcherQueueTimer? timer = null;

        void Finish()
        {
            page.Loaded -= OnLoaded;
            page.LayoutUpdated -= OnLayoutUpdated;
            timer?.Stop();
            done.TrySetResult();
        }

        void OnLoaded(object sender, RoutedEventArgs e)
        {
            // Loaded comes after the first layout; one more pass is enough for the containers of a list.
            page.Loaded -= OnLoaded;
            page.LayoutUpdated += OnLayoutUpdated;
        }

        void OnLayoutUpdated(object? sender, object e) => Finish();

        if (page.IsLoaded)
        {
            page.LayoutUpdated += OnLayoutUpdated;
        }
        else
        {
            page.Loaded += OnLoaded;
        }

        timer = queue.CreateTimer();
        timer.Interval = LayoutTimeout;
        timer.IsRepeating = false;
        timer.Tick += (_, _) => Finish();
        timer.Start();
        return done.Task;
    }
}
