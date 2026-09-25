using Kvertis.App.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

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
    private readonly ILocalizer _loc;

    public StepHeader()
    {
        _steps = App.Services.GetRequiredService<IStepNavigationService>();
        _motion = App.Services.GetRequiredService<IMotionSettings>();
        _session = App.Services.GetRequiredService<IWorkflowSession>();
        _coordinator = App.Services.GetRequiredService<IConversionCoordinator>();
        _loc = App.Services.GetRequiredService<ILocalizer>();
        InitializeComponent();

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
        _steps.StepChanged += OnStepChanged;
        _motion.Changed += OnMotionChanged;
        _session.Changed += OnSessionChanged;
        _coordinator.Changed += OnRoundChanged;
        Update();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _steps.StepChanged -= OnStepChanged;
        _motion.Changed -= OnMotionChanged;
        _session.Changed -= OnSessionChanged;
        _coordinator.Changed -= OnRoundChanged;
    }

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
        Apply(WorkflowStep.Drop, current.Value, "Drop", DropButton, animate);
        Apply(WorkflowStep.Target, current.Value, "Target", TargetButton, animate);
        Apply(WorkflowStep.Convert, current.Value, "Convert", ConvertButton, animate);
    }

    private void Apply(WorkflowStep step, WorkflowStep current, string prefix, Button button, bool animate)
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
        button.IsEnabled = !locked && (step <= current || IsReachable(step));
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
