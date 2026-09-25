using Kvertis.App.Services;
using Microsoft.UI.Windowing;
using Windows.Graphics;

namespace Kvertis.App.Helpers;

/// <summary>
/// Restores the stored position, size and maximized state of the main window.
/// A stored placement is only used when it still lands on a display that exists right now,
/// so unplugging a monitor cannot open the window out of reach.
/// </summary>
internal static class WindowPlacementHelper
{
    /// <summary>How much of the window has to stay inside the work area, in physical pixels.</summary>
    private const int MinVisible = 200;

    /// <summary>
    /// Moves <paramref name="window"/> to <paramref name="placement"/> if that is still reachable.
    /// Returns false when the caller should fall back to the default size.
    /// </summary>
    public static bool TryApply(AppWindow window, WindowPlacement? placement, int minWidth, int minHeight)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (placement is null)
        {
            return false;
        }

        var rect = new RectInt32(
            placement.X,
            placement.Y,
            Math.Max(placement.Width, minWidth),
            Math.Max(placement.Height, minHeight));
        if (!IsReachable(rect))
        {
            return false;
        }

        window.MoveAndResize(rect);
        if (placement.IsMaximized && window.Presenter is OverlappedPresenter presenter)
        {
            presenter.Maximize();
        }
        return true;
    }

    /// <summary>True when the rectangle meets a display whose work area shows enough of it.</summary>
    public static bool IsReachable(RectInt32 rect)
    {
        if (rect.Width < MinVisible || rect.Height < MinVisible)
        {
            return false;
        }

        // DisplayAreaFallback.None returns null when no monitor intersects the rectangle at all.
        var display = DisplayArea.GetFromRect(rect, DisplayAreaFallback.None);
        if (display is null)
        {
            return false;
        }

        var work = display.WorkArea;
        var overlapWidth = Math.Min(rect.X + rect.Width, work.X + work.Width) - Math.Max(rect.X, work.X);
        var overlapHeight = Math.Min(rect.Y + rect.Height, work.Y + work.Height) - Math.Max(rect.Y, work.Y);
        return overlapWidth >= MinVisible && overlapHeight >= MinVisible;
    }

    /// <summary>
    /// Builds the value to store. While the window is maximized or minimized its own bounds are useless,
    /// so the caller passes the last known restored bounds together with the maximized flag.
    /// The window itself is not touched, which keeps this usable while the window is already closing.
    /// </summary>
    public static WindowPlacement? Capture(RectInt32 restoredBounds, bool isMaximized)
    {
        if (restoredBounds.Width <= 0 || restoredBounds.Height <= 0)
        {
            return null;
        }

        return new WindowPlacement(
            restoredBounds.X,
            restoredBounds.Y,
            restoredBounds.Width,
            restoredBounds.Height,
            isMaximized);
    }
}
