using System.Numerics;
using Kvertis.Engine.Abstractions;

namespace Kvertis.App.Services;

/// <summary>What a step page can point the overlay at (worksheet "Übergänge", section "Ankerpunkte").</summary>
public enum TransitionAnchorKind
{
    /// <summary>Step 1: the head of a tray (not the whole tray), one per media kind with files.</summary>
    Tray,

    /// <summary>Step 2: the entry of a kind in the list on the left (later the universe symbol).</summary>
    KindSymbol,

    /// <summary>Step 3: the inbox card on the left.</summary>
    Inbox,

    /// <summary>Step 3: the middle of the swirl surface; its size is the size of the surface.</summary>
    SwirlCentre,

    /// <summary>The black hole of the page: the galaxy centre in step 1, the middle panel in step 2.</summary>
    Hole,

    /// <summary>Step 3: the bag button.</summary>
    Bag,
}

/// <summary>
/// A rectangle in overlay coordinates (DIP), measured by a page. <see cref="MediaKind"/> is null for anchors
/// that are not per media kind. <see cref="HoleRadius"/> only means something for <see cref="TransitionAnchorKind.Hole"/>.
/// </summary>
public sealed record TransitionAnchor(TransitionAnchorKind Kind, MediaKind? MediaKind, Vector2 Centre, Vector2 Size, float HoleRadius = 0f);

/// <summary>Everything one page measured at one moment. Anchors that do not exist are simply missing.</summary>
public sealed record TransitionAnchorSet(WorkflowStep Step, IReadOnlyList<TransitionAnchor> Anchors)
{
    /// <summary>The first anchor of that kind (and media kind, when given); null when the page has none.</summary>
    public TransitionAnchor? Find(TransitionAnchorKind kind, MediaKind? mediaKind = null)
    {
        foreach (var anchor in Anchors)
        {
            if (anchor.Kind == kind && (mediaKind is null || anchor.MediaKind == mediaKind))
            {
                return anchor;
            }
        }

        return null;
    }

    /// <summary>All anchors of that kind in the order the page measured them (for the trays: left to right).</summary>
    public IEnumerable<TransitionAnchor> All(TransitionAnchorKind kind) => Anchors.Where(a => a.Kind == kind);
}
