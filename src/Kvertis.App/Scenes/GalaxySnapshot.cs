using System.Numerics;
using Kvertis.Engine.Abstractions;

namespace Kvertis.App.Scenes;

/// <summary>Where the camera is.</summary>
public enum ZoomState
{
    Overview,
    ZoomingIn,
    Zoomed,
    ZoomingOut,
}

/// <summary>
/// Everything a view needs without touching the drawing thread. Built fresh at the end of every
/// <see cref="GalaxyScene.Update"/> and published as a whole; it holds no reference to a mutable object.
/// </summary>
public sealed record GalaxySnapshot(
    ZoomState Zoom,
    MediaKind? ZoomKind,
    float ZoomProgress,
    int? HoveredOrbit,
    IReadOnlyDictionary<Guid, Vector2> BodyPositions,
    TimeSpan Time)
{
    public static readonly GalaxySnapshot Empty = new(
        ZoomState.Overview,
        null,
        0f,
        null,
        new Dictionary<Guid, Vector2>().AsReadOnly(),
        TimeSpan.Zero);
}

/// <summary>
/// One way from the chosen input format through the hole to a reachable output. Two Bézier halves so the
/// renderer can draw it in one stroke; <see cref="IsRecommended"/> is drawn in mint.
/// </summary>
public sealed record GalaxyPath(
    string FormatId,
    Vector2 From,
    Vector2 ControlIn,
    Vector2 Hole,
    Vector2 ControlOut,
    Vector2 To,
    bool IsRecommended)
{
    /// <summary>The point on the way at 0..1; the first half runs into the hole, the second out of it.</summary>
    public Vector2 PointAt(float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return t < 0.5f
            ? GalaxyMotion.Approach(From, ControlIn, Hole, t * 2f)
            : GalaxyMotion.Approach(Hole, ControlOut, To, (t - 0.5f) * 2f);
    }
}
