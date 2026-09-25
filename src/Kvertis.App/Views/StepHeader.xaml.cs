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
    private readonly ILocalizer _loc;

    public StepHeader()
    {
        _steps = App.Services.GetRequiredService<IStepNavigationService>();
        _motion = App.Services.GetRequiredService<IMotionSettings>();
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
        _steps.StepChanged += OnStepChanged;
        _motion.Changed += OnMotionChanged;
        Update();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _steps.StepChanged -= OnStepChanged;
        _motion.Changed -= OnMotionChanged;
    }

    private void OnStepChanged(object? sender, EventArgs e) => Update();

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
        VisualStateManager.GoToState(this, prefix + state, animate);
        AutomationProperties.SetItemStatus(button, status);
        // Only going back is allowed for now; a step that was never run must not be reachable, otherwise a
        // screen reader would report it as done afterwards. Going forward comes with the conditions per step.
        button.IsEnabled = step <= current;
    }
}
