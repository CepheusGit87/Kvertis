using System.Globalization;
using System.Numerics;
using Kvertis.App.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Shapes;

namespace Kvertis.App.Views;

/// <summary>
/// The red thread above the content: which of the three steps is current, which are done, which are still
/// ahead. It only reads <see cref="IStepNavigationService"/>; it owns no state of its own.
/// </summary>
public sealed partial class StepHeader : UserControl
{
    private readonly IStepNavigationService _steps;
    private readonly IMotionSettings _motion;
    private readonly IWorkflowSession _session;
    private readonly IConversionCoordinator _coordinator;
    private readonly ITransitionService _transitions;
    private readonly ILocalizer _loc;

    // Fill state last applied to the two segments, so only a real change animates (.seg.voll, 0.7 s --sanft).
    private readonly bool?[] _segmentFull = new bool?[2];

    public StepHeader()
    {
        _steps = App.Services.GetRequiredService<IStepNavigationService>();
        _transitions = App.Services.GetRequiredService<ITransitionService>();
        _motion = App.Services.GetRequiredService<IMotionSettings>();
        _session = App.Services.GetRequiredService<IWorkflowSession>();
        _coordinator = App.Services.GetRequiredService<IConversionCoordinator>();
        _loc = App.Services.GetRequiredService<ILocalizer>();
        InitializeComponent();

        // The marks carry the step number (1, 2, 3); formatted, not a text of its own.
        DropNumber.Text = 1.ToString(CultureInfo.CurrentCulture);
        TargetNumber.Text = 2.ToString(CultureInfo.CurrentCulture);
        ConvertNumber.Text = 3.ToString(CultureInfo.CurrentCulture);

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        Update();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Loaded can fire again after the control was unloaded, so unsubscribe first.
        _steps.StepChanged -= OnStepChanged;
        _motion.Changed -= OnMotionChanged;
        _session.Changed -= OnSessionChanged;
        _coordinator.Changed -= OnRoundChanged;
        _transitions.Changed -= OnTransitionChanged;
        _steps.StepChanged += OnStepChanged;
        _motion.Changed += OnMotionChanged;
        _session.Changed += OnSessionChanged;
        _coordinator.Changed += OnRoundChanged;
        _transitions.Changed += OnTransitionChanged;
        Update();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _steps.StepChanged -= OnStepChanged;
        _motion.Changed -= OnMotionChanged;
        _session.Changed -= OnSessionChanged;
        _coordinator.Changed -= OnRoundChanged;
        _transitions.Changed -= OnTransitionChanged;
    }

    private void OnTransitionChanged(object? sender, EventArgs e) => Update();

    private void OnStepChanged(object? sender, EventArgs e) => Update();

    private void OnRoundChanged(object? sender, RoundChangedEventArgs e) => Update();

    private void OnSessionChanged(object? sender, EventArgs e) => Update();

    private void OnMotionChanged(object? sender, EventArgs e) => Update();

    private void OnDropClick(object sender, RoutedEventArgs e) => _steps.GoTo(WorkflowStep.Drop);

    private void OnTargetClick(object sender, RoutedEventArgs e) => _steps.GoTo(WorkflowStep.Target);

    private void OnConvertClick(object sender, RoutedEventArgs e) => _steps.GoTo(WorkflowStep.Convert);

    private void Update()
    {
        var current = _steps.CurrentStep;
        // Outside the main path (settings, history, pro, about, licenses) there is no step to show.
        Visibility = current is null ? Visibility.Collapsed : Visibility.Visible;
        if (current is null)
        {
            return;
        }

        // The state changes are colour and glyph only; with "reduce animations" they are applied without
        // transitions, which is what useTransitions: false does.
        var animate = !_motion.ReducedMotion;
        Apply(WorkflowStep.Drop, current.Value, "Drop", DropButton, DropStatus, animate);
        Apply(WorkflowStep.Target, current.Value, "Target", TargetButton, TargetStatus, animate);
        Apply(WorkflowStep.Convert, current.Value, "Convert", ConvertButton, ConvertStatus, animate);

        // A segment is full once the step after it is reached (the draft's seg-0 from step 2 on, seg-1 from step 3).
        SetSegment(0, DropSegmentFill, current.Value > WorkflowStep.Drop, animate && IsLoaded);
        SetSegment(1, TargetSegmentFill, current.Value > WorkflowStep.Target, animate && IsLoaded);
    }

    private void Apply(WorkflowStep step, WorkflowStep current, string prefix, Button button, TextBlock statusLine, bool animate)
    {
        string state;
        string status;
        if (step < current)
        {
            state = "Done";
            status = _loc.Get("Steps_Status_Done");
        }
        else if (step == current)
        {
            state = "Current";
            status = _loc.Get("Steps_Status_Current");
        }
        else
        {
            state = "Upcoming";
            status = _loc.Get("Steps_Status_Upcoming");
        }
        // While a round is running the way back is closed: whoever wants out presses "Abbrechen" first (ADR-021).
        var locked = IsLocked && step != WorkflowStep.Convert;
        if (locked)
        {
            status = _loc.Get("Steps_Status_Locked");
        }
        VisualStateManager.GoToState(this, prefix + state, animate);
        AutomationProperties.SetItemStatus(button, status);
        // The same words are visible as the small line under the title (.knoten small).
        statusLine.Text = status;
        // During a drawn transition the header takes no clicks and no keys (ADR-023); the state stays as it is.
        button.IsEnabled = !locked && !_transitions.IsTransitioning && (step <= current || IsReachable(step));
    }

    /// <summary>
    /// Fills or empties a segment. The mint rectangle is scaled along x from its left edge on the composition
    /// visual; with reduced motion (or before the first layout) the end value is set at once.
    /// </summary>
    private void SetSegment(int index, Rectangle fill, bool full, bool animate)
    {
        if (_segmentFull[index] == full)
        {
            return;
        }
        var first = _segmentFull[index] is null;
        _segmentFull[index] = full;

        var visual = ElementCompositionPreview.GetElementVisual(fill);
        var target = new Vector3(full ? 1f : 0f, 1f, 1f);
        visual.CenterPoint = Vector3.Zero;
        visual.StopAnimation("Scale");
        if (!animate || first)
        {
            visual.Scale = target;
            return;
        }
        var compositor = visual.Compositor;
        var grow = compositor.CreateVector3KeyFrameAnimation();
        grow.InsertKeyFrame(1f, target, compositor.CreateCubicBezierEasingFunction(new Vector2(0.2f, 0.9f), new Vector2(0.25f, 1f)));
        grow.Duration = TimeSpan.FromMilliseconds(700);
        visual.StartAnimation("Scale", grow);
    }

    private bool IsLocked => _coordinator.State is RoundState.Running or RoundState.Paused;

    /// <summary>
    /// A step ahead of the current one is only reachable once it has something to show: step 2 needs at least
    /// one staged file, step 3 a plan from step 2.
    /// </summary>
    private bool IsReachable(WorkflowStep step) => step switch
    {
        WorkflowStep.Target => _session.Staged.Count > 0,
        WorkflowStep.Convert => _session.Plan is { Items.Count: > 0 },
        _ => true,
    };
}
