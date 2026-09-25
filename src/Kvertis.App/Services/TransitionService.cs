using Kvertis.App.Scenes;
using Kvertis.Engine.Abstractions;
using Kvertis.Engine.Formats;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kvertis.App.Services;

/// <summary>
/// <see cref="ITransitionService"/> over an <see cref="ITransitionStage"/> (ADR-023). Free of WinUI: the
/// decision, the ghost descriptions and the order of the steps live here and are tested without the app host;
/// the window hands in the stage, which measures, navigates and draws.
/// </summary>
/// <remarks>
/// One transition at a time. The flow (worksheet "Ablauf eines Übergangs"): measure the source, hide its
/// elements, hold the overlay at the first frame, navigate at once with the new page hidden, measure the
/// target after its layout, start the scene, restore the target's elements when the flights ended, hide the
/// overlay when the hole faded. Anything that goes wrong on the way ends in the same end state as
/// <see cref="Finish"/>: new page shown, overlay hidden.
/// </remarks>
public sealed class TransitionService : ITransitionService
{
    private readonly IMotionSettings _motion;
    private readonly IWorkflowSession _session;
    private readonly ILocalizer _loc;
    private readonly FormatRegistry _registry;
    private readonly Func<Random> _random;
    private readonly ILogger<TransitionService> _logger;

    private ITransitionStage? _stage;
    private Run? _active;
    private bool _overlayBroken;

    public TransitionService(
        IMotionSettings motion,
        IWorkflowSession session,
        ILocalizer loc,
        FormatRegistry registry,
        ILogger<TransitionService>? logger = null,
        Func<Random>? random = null)
    {
        _motion = motion ?? throw new ArgumentNullException(nameof(motion));
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _loc = loc ?? throw new ArgumentNullException(nameof(loc));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _logger = logger ?? NullLogger<TransitionService>.Instance;
        _random = random ?? (() => Random.Shared);
        _motion.Changed += OnMotionChanged;
    }

    public bool IsTransitioning => _active is not null;

    public event EventHandler? Changed;

    /// <summary>Hands in the window side. Called once by the window after its content exists.</summary>
    public void Attach(ITransitionStage stage)
    {
        ArgumentNullException.ThrowIfNull(stage);
        if (_stage is { } old)
        {
            old.FlightsEnded -= OnFlightsEnded;
            old.Completed -= OnCompleted;
            old.DrawFailed -= OnDrawFailed;
        }

        _stage = stage;
        stage.FlightsEnded += OnFlightsEnded;
        stage.Completed += OnCompleted;
        stage.DrawFailed += OnDrawFailed;
    }

    public bool TryBegin(WorkflowStep from, WorkflowStep to)
    {
        // Whoever navigates while a flight runs gets the end state first (sofortFertig in the design).
        Finish();

        if (_stage is not { } stage || from == to)
        {
            return false;
        }

        var kind = TransitionPlanner.KindOf(from, to);
        if (kind is null)
        {
            // 3→2 and every other step change without a flight: a plain cross-fade, no overlay. With reduced
            // motion the caller switches without any animation.
            if (_motion.ReducedMotion)
            {
                return false;
            }

            stage.NavigateFaded(to);
            return true;
        }

        if (_motion.ReducedMotion || _overlayBroken || _session.Staged.Count == 0 || !stage.CanBegin)
        {
            return false;
        }

        var source = stage.MeasureCurrent();
        if (source is null || source.Step != from)
        {
            return false;
        }

        var kindSpecs = BuildKindSpecs(kind.Value);
        var sheetSpecs = kind == TransitionKind.SheetsForward ? BuildSheetSpecs() : [];
        if (!TransitionPlanner.HasSource(kind.Value, source, kindSpecs, sheetSpecs.Count))
        {
            return false;
        }

        var kinds = source.All(TransitionPlanner.SourceAnchor(kind.Value))
            .Where(a => a.MediaKind is { } m && kindSpecs.Any(s => s.Kind == m))
            .Select(a => a.MediaKind!.Value)
            .ToHashSet();
        var run = new Run(kind.Value, to, source, kindSpecs, sheetSpecs, kinds);
        _active = run;
        Changed?.Invoke(this, EventArgs.Empty);
        stage.SetInputLocked(true);
        stage.SetFlightVisibility(TransitionSide.Source, false, kinds);
        _ = RunAsync(stage, run);
        return true;
    }

    public void Finish() => Complete(_active);

    private async Task RunAsync(ITransitionStage stage, Run run)
    {
        try
        {
            var hold = new TransitionScene(TransitionPlanner.BuildHold(run.Kind, run.Source, run.KindSpecs), stage.Palette, _random());
            await stage.HoldAsync(hold);
            if (_active != run)
            {
                return;
            }

            // Navigate now, in parallel to the flight: the target anchors only exist once the page is in the tree.
            run.Navigated = true;
            stage.Navigate(run.To, hidden: true);
            stage.SetFlightVisibility(TransitionSide.Target, false, run.Kinds);
            var target = await stage.MeasureTargetAsync(TargetAnchor(run.Kind));
            if (_active != run)
            {
                return;
            }

            var plan = target is null ? null : TransitionPlanner.Build(run.Kind, run.Source, target, run.KindSpecs, run.SheetSpecs);
            if (plan is null)
            {
                Complete(run);
                return;
            }

            run.Scene = new TransitionScene(plan, stage.Palette, _random());
            stage.Start(run.Scene);
        }
        catch (Exception ex)
        {
            // The end state is always safe; the user only sees the plain switch.
            _logger.LogWarning(ex, "Transition {Kind} to {Step} failed; showing the page without a flight", run.Kind, run.To);
            Complete(run);
        }
    }

    /// <summary>The anchor kind the ghosts fly to; a target measurement without it is not ready yet.</summary>
    private static TransitionAnchorKind TargetAnchor(TransitionKind kind) => kind switch
    {
        TransitionKind.WormholeForward => TransitionAnchorKind.KindSymbol,
        TransitionKind.ArcBack => TransitionAnchorKind.Tray,
        _ => TransitionAnchorKind.SwirlCentre,
    };

    /// <summary>Ends <paramref name="run"/> if it is still the active one: page shown, overlay hidden, input free.</summary>
    private void Complete(Run? run)
    {
        if (run is null || _active != run)
        {
            return;
        }

        _active = null;
        if (_stage is { } stage)
        {
            // Finished during the hold, before the page swap: the click must still reach its step.
            if (!run.Navigated)
            {
                run.Navigated = true;
                stage.Navigate(run.To, hidden: false);
            }

            stage.End();
            stage.SetFlightVisibility(TransitionSide.Source, true, run.Kinds);
            stage.SetFlightVisibility(TransitionSide.Target, true, run.Kinds);
            stage.SetInputLocked(false);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void OnFlightsEnded(object? sender, EventArgs e)
    {
        // The ghosts are gone from this moment on; the page shows its real elements while the hole still fades.
        if (_active is { } run && _stage is { } stage)
        {
            stage.SetFlightVisibility(TransitionSide.Target, true, run.Kinds);
        }
    }

    private void OnCompleted(object? sender, EventArgs e) => Complete(_active);

    private void OnDrawFailed(object? sender, EventArgs e)
    {
        _overlayBroken = true;
        Complete(_active);
    }

    private void OnMotionChanged(object? sender, EventArgs e) => Finish();

    /// <summary>One ghost description per media kind with staged files: the kind name and the count, as the tray head shows them.</summary>
    private List<GhostSpec> BuildKindSpecs(TransitionKind kind)
    {
        var shape = kind == TransitionKind.WormholeForward ? GhostShape.TrayHeader : GhostShape.KindSymbol;
        var specs = new List<GhostSpec>();
        foreach (var group in _session.Staged.Where(f => f.IsUsable).GroupBy(f => f.Input.Kind))
        {
            var count = group.Count().ToString(System.Globalization.CultureInfo.CurrentCulture);
            specs.Add(new GhostSpec(shape, group.Key, _loc.Get("Kind_" + group.Key + "_Name"), count, null, null));
        }

        return specs;
    }

    /// <summary>One sheet per file of the plan, in plan order: kind, target format and file name.</summary>
    private List<GhostSpec> BuildSheetSpecs()
    {
        var specs = new List<GhostSpec>();
        if (_session.Plan is not { } plan)
        {
            return specs;
        }

        foreach (var item in plan.Items)
        {
            var kind = item.Input.Kind;
            specs.Add(new GhostSpec(
                GhostShape.Sheet,
                kind,
                _loc.Get("Kind_" + kind + "_Name"),
                null,
                TargetPlanner.Label(_registry, item.Settings.Output),
                Path.GetFileName(item.Input.Path)));
        }

        return specs;
    }

    private sealed class Run(
        TransitionKind kind,
        WorkflowStep to,
        TransitionAnchorSet source,
        IReadOnlyList<GhostSpec> kindSpecs,
        IReadOnlyList<GhostSpec> sheetSpecs,
        IReadOnlySet<MediaKind> kinds)
    {
        public TransitionKind Kind { get; } = kind;

        public WorkflowStep To { get; } = to;

        public TransitionAnchorSet Source { get; } = source;

        public IReadOnlyList<GhostSpec> KindSpecs { get; } = kindSpecs;

        public IReadOnlyList<GhostSpec> SheetSpecs { get; } = sheetSpecs;

        public IReadOnlySet<MediaKind> Kinds { get; } = kinds;

        public TransitionScene? Scene { get; set; }

        /// <summary>True once the frame shows <see cref="To"/>; a run that ends before that still has to navigate.</summary>
        public bool Navigated { get; set; }
    }
}
