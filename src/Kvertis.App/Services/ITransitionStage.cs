using Kvertis.App.Scenes;
using Kvertis.Engine.Abstractions;

namespace Kvertis.App.Services;

/// <summary>Which of the two pages of a transition an operation is meant for.</summary>
public enum TransitionSide
{
    /// <summary>The page the transition starts on; it leaves the frame as soon as the overlay stands.</summary>
    Source,

    /// <summary>The page the transition ends on; it enters the frame hidden and fades in at the end.</summary>
    Target,
}

/// <summary>
/// The window side of a transition, free of WinUI types so <see cref="TransitionService"/> can be tested
/// without the app host. The window implements it over the frame, the step pages and the overlay control.
/// All members run on the UI thread.
/// </summary>
public interface ITransitionStage
{
    /// <summary>False when the overlay cannot be used right now: drawing failed for good, the window is hidden or the history pane is open.</summary>
    bool CanBegin { get; }

    /// <summary>The colours of the current theme for the scene.</summary>
    ScenePalette Palette { get; }

    /// <summary>Raised on the UI thread when the flights of the running scene ended (the page may show its real elements again).</summary>
    event EventHandler? FlightsEnded;

    /// <summary>Raised on the UI thread when the running scene is finished (hole faded).</summary>
    event EventHandler? Completed;

    /// <summary>Raised on the UI thread after a drawing error; the service then never uses the overlay again.</summary>
    event EventHandler? DrawFailed;

    /// <summary>Measures the page in the frame; null when it is not a step page or not laid out.</summary>
    TransitionAnchorSet? MeasureCurrent();

    /// <summary>
    /// Navigates to the page of <paramref name="step"/> at once and without the Fluent transition. With
    /// <paramref name="hidden"/> the new page starts at opacity 0.
    /// </summary>
    void Navigate(WorkflowStep step, bool hidden);

    /// <summary>Navigates like <see cref="Navigate"/> and fades the new page in (the plain cross-fade of 3→2).</summary>
    void NavigateFaded(WorkflowStep step);

    /// <summary>
    /// Waits for the new page to be laid out (at most two frames) and measures it; null on failure. A
    /// measurement counts only once it holds at least one anchor of <paramref name="required"/>, the kind the
    /// ghosts fly to; before that the containers of a list may simply not exist yet.
    /// </summary>
    Task<TransitionAnchorSet?> MeasureTargetAsync(TransitionAnchorKind required);

    /// <summary>Hides or shows the elements the overlay stands in for, on the source or the target page.</summary>
    void SetFlightVisibility(TransitionSide side, bool visible, IReadOnlySet<MediaKind> kinds);

    /// <summary>Blocks pointer input to the step header and the frame while true.</summary>
    void SetInputLocked(bool locked);

    /// <summary>
    /// Shows the overlay with <paramref name="scene"/> at its first frame and holds it there. Completes once
    /// that frame was drawn, so the old page can leave the frame without a blank frame in between.
    /// </summary>
    Task HoldAsync(TransitionScene scene);

    /// <summary>Replaces the held scene by the real one and lets time run; starts the fade of the target page.</summary>
    void Start(TransitionScene scene);

    /// <summary>Hides the overlay, shows the target page fully. Idempotent.</summary>
    void End();
}
