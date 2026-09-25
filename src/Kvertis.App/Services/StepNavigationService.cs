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
    private readonly ITransitionService _transitions;
    private WorkflowStep? _currentStep;

    public StepNavigationService(INavigationService navigation, ITransitionService transitions)
    {
        _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
        _transitions = transitions ?? throw new ArgumentNullException(nameof(transitions));
        _navigation.Navigated += OnNavigated;
        Update();
    }

    /// <summary>The page that shows <paramref name="step"/>.</summary>
    public static AppPage PageOf(WorkflowStep step) => StepPages[step];

    public WorkflowStep? CurrentStep => _currentStep;

    public event EventHandler? StepChanged;

    public void GoTo(WorkflowStep step)
    {
        if (!StepPages.TryGetValue(step, out var page))
        {
            return;
        }

        // The drawn transitions (ADR-023) navigate themselves; everything else is the plain switch.
        if (_currentStep is { } from && _transitions.TryBegin(from, step))
        {
            return;
        }

        // A step change is not a detour: it leaves no entry in the back stack.
        _navigation.Navigate(page, keepBackStack: false);
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
