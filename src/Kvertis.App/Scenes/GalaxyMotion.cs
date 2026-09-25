using System.Numerics;

namespace Kvertis.App.Scenes;

/// <summary>
/// The curves and the approach path of the galaxy as pure functions, so tests can ask them at fixed points
/// in time (ADR-022). Every smoothing has the frame rate independent form <c>1 - exp(-dt * rate)</c>.
/// </summary>
public static class GalaxyMotion
{
    /// <summary>Cubic in and out: t &lt; 0.5 ? 4t³ : 1 - (-2t + 2)³ / 2.</summary>
    public static float Ease(float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return t < 0.5f ? 4f * t * t * t : 1f - MathF.Pow(-2f * t + 2f, 3f) / 2f;
    }

    /// <summary>Smoothstep: t²(3 - 2t).</summary>
    public static float Smooth(float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    /// <summary>1 - (1 - t)³.</summary>
    public static float EaseOut(float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        var u = 1f - t;
        return 1f - u * u * u;
    }

    /// <summary>Overshooting spring used for the tray counter: 1 + 2.2(t - 1)³ + 1.2(t - 1)².</summary>
    public static float EaseBack(float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        var u = t - 1f;
        return 1f + 2.2f * u * u * u + 1.2f * u * u;
    }

    /// <summary>The share of the way to a target that a smoothing with <paramref name="rate"/> covers in <paramref name="dt"/> seconds.</summary>
    public static float Damp(float rate, float dt) => 1f - MathF.Exp(-dt * rate);

    /// <summary>Moves <paramref name="value"/> towards <paramref name="target"/> with the given rate (1/s).</summary>
    public static float Approach(float value, float target, float rate, float dt) =>
        value + (target - value) * Damp(rate, dt);

    /// <summary>Moves a point towards a target with the given rate (1/s).</summary>
    public static Vector2 Approach(Vector2 value, Vector2 target, float rate, float dt) =>
        value + (target - value) * Damp(rate, dt);

    /// <summary>The quadratic Bézier curve a new file flies along; <paramref name="t"/> is already eased.</summary>
    public static Vector2 Approach(Vector2 from, Vector2 control, Vector2 to, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        var u = 1f - t;
        return (u * u * from) + (2f * u * t * control) + (t * t * to);
    }

    /// <summary>The control point of the approach curve: above both ends, on the middle axis.</summary>
    public static Vector2 ApproachControl(Vector2 from, Vector2 to, float centerX, float scale) =>
        new(centerX, Math.Min(from.Y, to.Y) - 30f * scale);

    /// <summary>
    /// How far a particle at <paramref name="position"/> is dragged towards the pointer. Radius
    /// lerp(115, 185, hover) DIP, strength lerp(0.1, 0.4, hover), quadratic falloff, scaled by
    /// <paramref name="influence"/> (0..1).
    /// </summary>
    public static Vector2 PointerPull(Vector2 position, Vector2 pointerPosition, float hover, float influence, float scale)
    {
        if (influence <= 0f)
        {
            return Vector2.Zero;
        }

        var radius = float.Lerp(115f, 185f, Math.Clamp(hover, 0f, 1f)) * scale;
        var delta = pointerPosition - position;
        var distance = delta.Length();
        if (distance >= radius || distance <= float.Epsilon)
        {
            return Vector2.Zero;
        }

        var falloff = 1f - distance / radius;
        var strength = float.Lerp(0.1f, 0.4f, Math.Clamp(hover, 0f, 1f));
        return delta * (strength * falloff * falloff * influence);
    }

    /// <summary>Wraps an angle difference into [-π, π].</summary>
    public static float WrapAngle(float angle)
    {
        const float twoPi = MathF.PI * 2f;
        angle %= twoPi;
        if (angle > MathF.PI)
        {
            angle -= twoPi;
        }
        else if (angle < -MathF.PI)
        {
            angle += twoPi;
        }

        return angle;
    }
}
