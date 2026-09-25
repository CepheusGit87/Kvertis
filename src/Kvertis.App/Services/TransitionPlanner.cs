using System.Numerics;
using Kvertis.App.Scenes;
using Kvertis.Engine.Abstractions;

namespace Kvertis.App.Services;

/// <summary>
/// Turns two measured anchor sets and the ghost descriptions into a <see cref="TransitionPlan"/> (worksheet
/// "Übergänge"). Pure functions: every ghost flies from the measured source anchor of its kind to the measured
/// target anchor of the same kind, so the order of trays and kinds on the pages never matters here.
/// </summary>
public static class TransitionPlanner
{
    /// <summary>Radius of the hole on the pages that do not draw one (step 2 and the swirl before its surface exists).</summary>
    public const float PageHoleRadius = 12f;

    /// <summary>The overlay pair for a step change, or null for a plain switch.</summary>
    public static TransitionKind? KindOf(WorkflowStep from, WorkflowStep to) => (from, to) switch
    {
        (WorkflowStep.Drop, WorkflowStep.Target) => TransitionKind.WormholeForward,
        (WorkflowStep.Target, WorkflowStep.Drop) => TransitionKind.ArcBack,
        (WorkflowStep.Target, WorkflowStep.Convert) => TransitionKind.SheetsForward,
        _ => null,
    };

    /// <summary>The anchor kind the ghosts start from on the source page.</summary>
    public static TransitionAnchorKind SourceAnchor(TransitionKind kind) =>
        kind == TransitionKind.WormholeForward ? TransitionAnchorKind.Tray : TransitionAnchorKind.KindSymbol;

    /// <summary>
    /// True when the source page offers enough to start: its hole and at least one anchor of a kind that has a
    /// ghost description. Without that there is no transition, only the plain switch.
    /// </summary>
    public static bool HasSource(TransitionKind kind, TransitionAnchorSet source, IReadOnlyList<GhostSpec> kindSpecs, int sheetCount)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(kindSpecs);
        if (source.Find(TransitionAnchorKind.Hole) is null)
        {
            return false;
        }

        var anchors = source.All(SourceAnchor(kind)).Any(a => a.MediaKind is { } m && kindSpecs.Any(s => s.Kind == m));
        return kind == TransitionKind.SheetsForward ? sheetCount > 0 : anchors;
    }

    /// <summary>
    /// The plan of the first frame: every ghost stands on its source anchor, the hole stays where it is. The
    /// overlay shows it while the old page leaves the frame and the new one is measured.
    /// </summary>
    public static TransitionPlan BuildHold(TransitionKind kind, TransitionAnchorSet source, IReadOnlyList<GhostSpec> kindSpecs)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(kindSpecs);
        var hole = source.Find(TransitionAnchorKind.Hole) ?? throw new ArgumentException("The source has no hole anchor.", nameof(source));
        var ghosts = new List<TransitionGhost>();
        foreach (var anchor in source.All(SourceAnchor(kind)))
        {
            if (anchor.MediaKind is not { } media || kindSpecs.FirstOrDefault(s => s.Kind == media) is not { } spec)
            {
                continue;
            }

            var endpoint = new TransitionEndpoint(anchor.Centre, anchor.Size);
            ghosts.Add(new TransitionGhost(spec, endpoint, endpoint));
        }

        var holeEndpoint = new TransitionEndpoint(hole.Centre, Vector2.Zero);
        // ArcBack draws the tray head as shape B; the hold frame only needs shape A, and a wormhole plan with
        // From == To keeps every ghost on its anchor at u = 0 for any kind.
        var holdKind = kind == TransitionKind.SheetsForward ? TransitionKind.SheetsForward : TransitionKind.ArcBack;
        return new TransitionPlan(holdKind, ghosts, holeEndpoint, hole.HoleRadius, holeEndpoint, hole.HoleRadius);
    }

    /// <summary>
    /// The full plan; null when the target page offers nothing to fly to (then the service ends the transition
    /// with the plain end state). Ghost order follows the source anchors, which is the order on the page.
    /// </summary>
    public static TransitionPlan? Build(
        TransitionKind kind,
        TransitionAnchorSet source,
        TransitionAnchorSet target,
        IReadOnlyList<GhostSpec> kindSpecs,
        IReadOnlyList<GhostSpec> sheetSpecs)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(kindSpecs);
        ArgumentNullException.ThrowIfNull(sheetSpecs);

        var holeFrom = source.Find(TransitionAnchorKind.Hole);
        if (holeFrom is null)
        {
            return null;
        }

        return kind switch
        {
            TransitionKind.WormholeForward => BuildPair(kind, source, target, kindSpecs, holeFrom, TransitionAnchorKind.Tray, TransitionAnchorKind.KindSymbol),
            TransitionKind.ArcBack => BuildPair(kind, source, target, kindSpecs, holeFrom, TransitionAnchorKind.KindSymbol, TransitionAnchorKind.Tray),
            _ => BuildSheets(source, target, kindSpecs, sheetSpecs, holeFrom),
        };
    }

    private static TransitionPlan? BuildPair(
        TransitionKind kind,
        TransitionAnchorSet source,
        TransitionAnchorSet target,
        IReadOnlyList<GhostSpec> kindSpecs,
        TransitionAnchor holeFrom,
        TransitionAnchorKind fromKind,
        TransitionAnchorKind toKind)
    {
        var ghosts = new List<TransitionGhost>();
        foreach (var from in source.All(fromKind))
        {
            if (from.MediaKind is not { } media || kindSpecs.FirstOrDefault(s => s.Kind == media) is not { } spec)
            {
                continue;
            }

            if (target.Find(toKind, media) is not { } to)
            {
                continue;
            }

            ghosts.Add(new TransitionGhost(spec, new TransitionEndpoint(from.Centre, from.Size), new TransitionEndpoint(to.Centre, to.Size)));
        }

        if (ghosts.Count == 0)
        {
            return null;
        }

        var holeTo = target.Find(TransitionAnchorKind.Hole);
        var toCentre = holeTo?.Centre ?? holeFrom.Centre;
        var toRadius = holeTo is { HoleRadius: > 0f } ? holeTo.HoleRadius : PageHoleRadius;
        return new TransitionPlan(
            kind,
            ghosts,
            new TransitionEndpoint(holeFrom.Centre, Vector2.Zero),
            holeFrom.HoleRadius,
            new TransitionEndpoint(toCentre, Vector2.Zero),
            toRadius);
    }

    private static TransitionPlan? BuildSheets(
        TransitionAnchorSet source,
        TransitionAnchorSet target,
        IReadOnlyList<GhostSpec> kindSpecs,
        IReadOnlyList<GhostSpec> sheetSpecs,
        TransitionAnchor holeFrom)
    {
        if (sheetSpecs.Count == 0 || target.Find(TransitionAnchorKind.SwirlCentre) is not { } swirl)
        {
            return null;
        }

        var ghosts = new List<TransitionGhost>();
        foreach (var anchor in source.All(TransitionAnchorKind.KindSymbol))
        {
            if (anchor.MediaKind is not { } media || kindSpecs.FirstOrDefault(s => s.Kind == media) is not { } spec)
            {
                continue;
            }

            var endpoint = new TransitionEndpoint(anchor.Centre, anchor.Size);
            ghosts.Add(new TransitionGhost(spec, endpoint, endpoint));
        }

        // The stack places are canvas coordinates of the swirl surface (worksheet "Stapelplatz T_j"); the
        // surface's top-left corner in overlay coordinates moves them into the overlay.
        var origin = swirl.Centre - swirl.Size / 2f;
        var hole = new TransitionEndpoint(holeFrom.Centre, Vector2.Zero);
        for (var j = 0; j < sheetSpecs.Count; j++)
        {
            var place = SwirlScene.StackSlot(j, sheetSpecs.Count, swirl.Size.Y / 2f);
            var to = new TransitionEndpoint(origin + place.Centre, place.Size);
            ghosts.Add(new TransitionGhost(sheetSpecs[j], hole, to));
        }

        return new TransitionPlan(
            TransitionKind.SheetsForward,
            ghosts,
            hole,
            holeFrom.HoleRadius,
            new TransitionEndpoint(swirl.Centre, Vector2.Zero),
            PageHoleRadius);
    }
}
