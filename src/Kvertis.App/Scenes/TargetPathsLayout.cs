using System.Numerics;

namespace Kvertis.App.Scenes;

/// <summary>
/// Geometry of the middle surface of step 2 for one canvas size: pure functions of width and height, no
/// state (ADR-022). Follows <c>geo()</c> of the draft: the hole sits on the horizontal middle at 42 % of the
/// height, the orbit is at most 200 DIP wide and keeps 36 DIP from the edges.
/// </summary>
public sealed record TargetPathsLayout(float Width, float Height)
{
    /// <summary>Radius of the hole in DIP; the same as the page hole of the transitions, so the flying hole lands 1:1.</summary>
    public const float HoleRadius = 12f;

    /// <summary>Vertical squash of the orbit (draft: ry = rx * 0.42).</summary>
    public const float Flatten = 0.42f;

    /// <summary>Largest horizontal radius of the orbit in DIP.</summary>
    public const float MaxOrbitRadius = 200f;

    /// <summary>Smallest horizontal radius the orbit shrinks to on a narrow surface.</summary>
    public const float MinOrbitRadius = 60f;

    /// <summary>Where the hole is: the middle of the width, 42 % down.</summary>
    public Vector2 Hole { get; } = new(Width / 2f, Height * 0.42f);

    /// <summary>Horizontal radius of the fully opened orbit.</summary>
    public float OrbitRadiusX { get; } = Math.Clamp(Width / 2f - 36f, MinOrbitRadius, MaxOrbitRadius);

    /// <summary>Vertical radius of the fully opened orbit.</summary>
    public float OrbitRadiusY => OrbitRadiusX * Flatten;

    /// <summary>Planet and glow sizes shrink a little on small surfaces: clamp(min(W / 520, H / 420), 0.6, 1).</summary>
    public float Scale { get; } = Math.Clamp(Math.Min(Width / 520f, Height / 420f), 0.6f, 1f);
}
