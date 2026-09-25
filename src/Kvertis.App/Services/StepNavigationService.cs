namespace Kvertis.App.Services;

/// <summary>The three steps of the main path (design/ENTSCHEIDUNGEN.md, "Grundaufbau").</summary>
public enum WorkflowStep
{
    Drop = 0,
    Target,
    Convert,
}

/// <summary>
/// Which of the three steps the user is on. Pages outside the main path (settings, history, pro, about,
/// licenses) have no step; <see cref="CurrentStep"/> is null there and the step header hides itself.
/// </summary>
public interface IStepNavigationService
{
    WorkflowStep? CurrentStep { get; }

    event EventHandler? StepChanged;

    void GoTo(WorkflowStep step);
}

/// <summary>
/// <see cref="IStepNavigationService"/> on top of <see cref="INavigationService"/>: it maps the three step
/// pages to <see cref="WorkflowStep"/> and back, so there is only one place that knows the order.
/// </summary>
public sealed class StepNavigationService : IStepNavigationService
{
    private static readonly Dictionary<WorkflowStep, AppPage> StepPages = new()
    {
        [WorkflowStep.Drop] = AppPage.Main,
        [WorkflowStep.Target] = AppPage.Target,
        [WorkflowStep.Convert] = AppPage.Convert,
    };

    private readonly INavigationService _navigation;
    private WorkflowStep? _currentStep;

    public StepNavigationService(INavigationService navigation)
    {
        _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
        _navigation.Navigated += OnNavigated;
        Update();
    }

    public WorkflowStep? CurrentStep => _currentStep;

    public event EventHandler? StepChanged;

    public void GoTo(WorkflowStep step)
    {
        if (StepPages.TryGetValue(step, out var page))
        {
            // A step change is not a detour: it leaves no entry in the back stack.
            _navigation.Navigate(page, keepBackStack: false);
        }
    }

    private void OnNavigated(object? sender, EventArgs e) => Update();

    private void Update()
    {
        WorkflowStep? step = null;
        foreach (var pair in StepPages)
        {
            if (pair.Value == _navigation.CurrentPage)
            {
                step = pair.Key;
                break;
            }
        }
        if (step == _currentStep)
        {
            return;
        }
        _currentStep = step;
        StepChanged?.Invoke(this, EventArgs.Empty);
    }
}
