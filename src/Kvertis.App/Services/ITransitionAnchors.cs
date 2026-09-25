using System.Numerics;
using Kvertis.Engine.Abstractions;
using Microsoft.UI.Xaml;

namespace Kvertis.App.Services;

/// <summary>The one measurement every page uses: the element's box in the coordinates of the overlay.</summary>
public static class TransitionMeasure
{
    /// <summary>The anchor for <paramref name="element"/>, or null when it has no size yet.</summary>
    public static TransitionAnchor? Of(FrameworkElement element, UIElement reference, TransitionAnchorKind kind, MediaKind? mediaKind = null, float holeRadius = 0f)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(reference);
        if (element.ActualWidth <= 0 || element.ActualHeight <= 0 || element.Visibility == Visibility.Collapsed)
        {
            return null;
        }

        var bounds = element.TransformToVisual(reference).TransformBounds(new Windows.Foundation.Rect(0, 0, element.ActualWidth, element.ActualHeight));
        var centre = new Vector2((float)(bounds.X + bounds.Width / 2), (float)(bounds.Y + bounds.Height / 2));
        return new TransitionAnchor(kind, mediaKind, centre, new Vector2((float)bounds.Width, (float)bounds.Height), holeRadius);
    }
}

/// <summary>
/// Implemented by the three step pages (ADR-023). The overlay never copies a XAML element; the page only
/// says where its targets are and hides the real ones while the drawn stand-ins fly. Called on the UI
/// thread only.
/// </summary>
public interface ITransitionAnchors
{
    /// <summary>Measures the current layout relative to <paramref name="reference"/>; null when the page is not laid out yet.</summary>
    TransitionAnchorSet? MeasureAnchors(UIElement reference);

    /// <summary>Hides (false) or shows (true) the elements the overlay is drawing stand-ins for. Must be cheap and idempotent.</summary>
    void SetFlightVisibility(bool visible, IReadOnlySet<MediaKind> kinds);
}
