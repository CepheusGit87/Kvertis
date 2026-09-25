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
/// <param name="Gimmick">Where the whirl or big bang timeline is; <see cref="GimmickState.Idle"/> nearly always.</param>
/// <param name="SurfaceSuction">
/// 0..1: how far the XAML surface (trays, cards) is drawn into the hole in the last two seconds of the charge
/// and during the explosion. The host applies it as a Composition translation and scale towards
/// <see cref="HoleCenter"/>; nothing of the XAML is ever drawn on the canvas. May dip slightly below 0 while
/// the surface springs back.
/// </param>
/// <param name="SurfaceOpacity">0..1 opacity of the same elements.</param>
/// <param name="SurfaceJitter">Amplitude in DIP of the random shake of the same elements.</param>
/// <param name="HoleCenter">The hole in canvas coordinates.</param>
public sealed record GalaxySnapshot(
    ZoomState Zoom,
    MediaKind? ZoomKind,
    float ZoomProgress,
    int? HoveredOrbit,
    IReadOnlyDictionary<Guid, Vector2> BodyPositions,
    TimeSpan Time,
    GimmickState Gimmick = GimmickState.Idle,
    float SurfaceSuction = 0f,
    float SurfaceOpacity = 1f,
    float SurfaceJitter = 0f,
    Vector2 HoleCenter = default)
{
    public static readonly GalaxySnapshot Empty = new(
        ZoomState.Overview,
        null,
        0f,
        null,
        new Dictionary<Guid, Vector2>().AsReadOnly(),
        TimeSpan.Zero);

    /// <summary>True while the host has to move the XAML surface for the suction.</summary>
    public bool SurfaceAffected => SurfaceSuction > 0.0005f || SurfaceSuction < -0.0005f || SurfaceJitter > 0.01f || SurfaceOpacity < 0.999f;
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
