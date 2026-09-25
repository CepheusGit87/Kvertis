namespace Kvertis.App.Services;

/// <summary>
/// Decides whether a step change gets the drawn overlay (wormhole 1→2, arc 2→1, sheets 2→3) or a plain page
/// switch, and runs the overlay when it does (ADR-023). <see cref="StepNavigationService"/> asks it first.
/// </summary>
public interface ITransitionService
{
    /// <summary>True from the start of a flight until its end state; navigation commands and the galaxy check it.</summary>
    bool IsTransitioning { get; }

    /// <summary>Raised on the UI thread whenever <see cref="IsTransitioning"/> changed.</summary>
    event EventHandler? Changed;

    /// <summary>
    /// Starts the transition from <paramref name="from"/> to <paramref name="to"/> if there is one. Returns true
    /// when the service navigated itself (overlay or cross-fade); false means the caller navigates as usual.
    /// A running transition is finished first.
    /// </summary>
    bool TryBegin(WorkflowStep from, WorkflowStep to);

    /// <summary>Jumps to the end state at once: the new page is shown, the overlay is hidden. Safe to call any time.</summary>
    void Finish();
}
