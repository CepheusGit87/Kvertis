using System.Numerics;

namespace Kvertis.App.Scenes;

/// <summary>
/// An easing of one keyframe section: linear, a CSS <c>cubic-bezier</c> or one of the scene curves. A small
/// value type so a keyframe track is plain data and can be compared in tests.
/// </summary>
public readonly record struct SceneEasing
{
    private enum Form
    {
        Linear,
        Bezier,
        EaseInOut,
        EaseOutCubic,
    }

    private readonly Form _form;

    private SceneEasing(Form form, float x1 = 0f, float y1 = 0f, float x2 = 1f, float y2 = 1f)
    {
        _form = form;
        X1 = x1;
        Y1 = y1;
        X2 = x2;
        Y2 = y2;
    }

    public float X1 { get; }

    public float Y1 { get; }

    public float X2 { get; }

    public float Y2 { get; }

    /// <summary>The Web Animations default between two keyframes.</summary>
    public static SceneEasing Linear { get; } = new(Form.Linear);

    /// <summary><see cref="SceneMotion.Ease"/>: cubic in and out.</summary>
    public static SceneEasing Ease { get; } = new(Form.EaseInOut);

    /// <summary><see cref="SceneMotion.EaseOut"/>: 1 - (1 - t)³.</summary>
    public static SceneEasing EaseOutCubic { get; } = new(Form.EaseOutCubic);

    /// <summary>CSS <c>ease-out</c> = cubic-bezier(0, 0, .58, 1).</summary>
    public static SceneEasing EaseOutCss { get; } = Bezier(0f, 0f, 0.58f, 1f);

    /// <summary>cubic-bezier(.6, 0, .9, .5): the pull into the hole (wormhole, stations 0 and 0.30).</summary>
    public static SceneEasing Suck { get; } = Bezier(0.6f, 0f, 0.9f, 0.5f);

    /// <summary>cubic-bezier(.3, 1.5, .5, 1): the overshoot when a symbol jumps out of the hole.</summary>
    public static SceneEasing Overshoot { get; } = Bezier(0.3f, 1.5f, 0.5f, 1f);

    /// <summary>cubic-bezier(.45, .05, .3, 1): first half of the arc back.</summary>
    public static SceneEasing ArcRise { get; } = Bezier(0.45f, 0.05f, 0.3f, 1f);

    /// <summary>cubic-bezier(.3, .1, .25, 1): second half of the arc back and the sheets' landing.</summary>
    public static SceneEasing ArcLand { get; } = Bezier(0.3f, 0.1f, 0.25f, 1f);

    /// <summary>cubic-bezier(.3, 0, .3, 1): a sheet rising out of the hole.</summary>
    public static SceneEasing SheetRise { get; } = Bezier(0.3f, 0f, 0.3f, 1f);

    /// <summary>cubic-bezier(.3, 1.2, .5, 1).</summary>
    public static SceneEasing Spring { get; } = Bezier(0.3f, 1.2f, 0.5f, 1f);

    /// <summary>cubic-bezier(.3, .7, .4, 1): the shake of the step header.</summary>
    public static SceneEasing Shake { get; } = Bezier(0.3f, 0.7f, 0.4f, 1f);

    /// <summary>A CSS cubic-bezier(x1, y1, x2, y2).</summary>
    public static SceneEasing Bezier(float x1, float y1, float x2, float y2) => new(Form.Bezier, x1, y1, x2, y2);

    /// <summary>The eased share for a linear share <paramref name="t"/> (clamped to 0..1).</summary>
    public float Apply(float t) => _form switch
    {
        Form.Bezier => SceneMotion.CubicBezier(X1, Y1, X2, Y2, t),
        Form.EaseInOut => SceneMotion.Ease(t),
        Form.EaseOutCubic => SceneMotion.EaseOut(t),
        _ => Math.Clamp(t, 0f, 1f),
    };
}

/// <summary>
/// One station of a ghost track (worksheet "Übergänge", keyframe evaluation): values at <see cref="Offset"/>
/// and the easing used from here to the next station.
/// </summary>
public readonly record struct GhostKeyframe(
    float Offset,
    Vector2 Position,
    float Width,
    float ScaleX,
    float ScaleY,
    float Rotation,
    float AlphaA,
    float AlphaB,
    SceneEasing EasingToNext);

/// <summary>
/// Curves, the CSS cubic-bezier solver and the keyframe evaluation of the transitions, swirl and finale as pure
/// functions (ADR-022, ADR-023). Complements <see cref="GalaxyMotion"/>.
/// </summary>
public static class SceneMotion
{
    /// <summary>Cubic in and out: t &lt; 0.5 ? 4t³ : 1 - (-2t + 2)³ / 2.</summary>
    public static float Ease(float t) => GalaxyMotion.Ease(t);

    /// <summary>1 - (1 - t)³ (the design's <c>aus3</c>).</summary>
    public static float EaseOut(float t) => GalaxyMotion.EaseOut(t);

    /// <summary>Smoothstep t²(3 - 2t).</summary>
    public static float Smooth(float t) => GalaxyMotion.Smooth(t);

    /// <summary>1 + 2.2(t - 1)³ + 1.2(t - 1)².</summary>
    public static float EaseBack(float t) => GalaxyMotion.EaseBack(t);

    /// <summary>clamp((f - a) / (b - a)) to 0..1; the time windows of the finale.</summary>
    public static float Phase(float f, float a, float b)
    {
        if (b <= a)
        {
            return f >= b ? 1f : 0f;
        }

        return Math.Clamp((f - a) / (b - a), 0f, 1f);
    }

    /// <summary>
    /// CSS <c>cubic-bezier(x1, y1, x2, y2)</c> at time share <paramref name="t"/>: solves x(s) = t by Newton
    /// (eight steps) with bisection as fallback and returns y(s). y may leave 0..1 (overshoot).
    /// </summary>
    public static float CubicBezier(float x1, float y1, float x2, float y2, float t)
    {
        if (t <= 0f)
        {
            return 0f;
        }

        if (t >= 1f)
        {
            return 1f;
        }

        const double tolerance = 1e-7;
        double ax = x1, bx = x2, target = t;
        var s = target;
        for (var i = 0; i < 8; i++)
        {
            var error = BezierAt(ax, bx, s) - target;
            var slope = BezierSlope(ax, bx, s);
            if (Math.Abs(error) < tolerance || Math.Abs(slope) < 1e-6)
            {
                break;
            }

            s -= error / slope;
            if (s < 0d || s > 1d)
            {
                break;
            }
        }

        if (s < 0d || s > 1d || Math.Abs(BezierAt(ax, bx, s) - target) >= tolerance)
        {
            double lo = 0d, hi = 1d;
            s = target;
            for (var i = 0; i < 60; i++)
            {
                if (BezierAt(ax, bx, s) < target)
                {
                    lo = s;
                }
                else
                {
                    hi = s;
                }

                s = (lo + hi) * 0.5d;
            }
        }

        return (float)BezierAt(y1, y2, s);
    }

    /// <summary>
    /// The state of a ghost at share <paramref name="u"/> of its track: within a section every field is
    /// interpolated linearly with the section's eased share (Web Animations: easing per keyframe).
    /// </summary>
    public static GhostState Evaluate(GhostSpec spec, IReadOnlyList<GhostKeyframe> track, float u)
    {
        ArgumentNullException.ThrowIfNull(track);
        if (track.Count == 0)
        {
            throw new ArgumentException("A track needs at least one station.", nameof(track));
        }

        var first = track[0];
        if (track.Count == 1 || u <= first.Offset)
        {
            return StateOf(spec, first);
        }

        for (var i = 0; i < track.Count - 1; i++)
        {
            var a = track[i];
            var b = track[i + 1];
            if (u <= b.Offset)
            {
                var span = b.Offset - a.Offset;
                var s = span <= 0f ? 1f : a.EasingToNext.Apply((u - a.Offset) / span);
                return new GhostState(
                    spec,
                    Vector2.Lerp(a.Position, b.Position, s),
                    float.Lerp(a.Width, b.Width, s),
                    float.Lerp(a.ScaleX, b.ScaleX, s),
                    float.Lerp(a.ScaleY, b.ScaleY, s),
                    float.Lerp(a.Rotation, b.Rotation, s),
                    float.Lerp(a.AlphaA, b.AlphaA, s),
                    float.Lerp(a.AlphaB, b.AlphaB, s));
            }
        }

        return StateOf(spec, track[^1]);
    }

    private static GhostState StateOf(GhostSpec spec, in GhostKeyframe k) =>
        new(spec, k.Position, k.Width, k.ScaleX, k.ScaleY, k.Rotation, k.AlphaA, k.AlphaB);

    // B(s) of a cubic Bézier with P0 = 0 and P3 = 1.
    private static double BezierAt(double p1, double p2, double s)
    {
        var u = 1d - s;
        return (3d * u * u * s * p1) + (3d * u * s * s * p2) + (s * s * s);
    }

    private static double BezierSlope(double p1, double p2, double s)
    {
        var u = 1d - s;
        return (3d * u * u * p1) + (6d * u * s * (p2 - p1)) + (3d * s * s * (1d - p2));
    }
}
