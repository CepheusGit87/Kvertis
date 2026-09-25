using System.Numerics;
using Kvertis.App.Scenes;
using Kvertis.Engine.Abstractions;
using Shouldly;
using Xunit;

namespace Kvertis.App.Tests.Scenes;

public class SceneMotionTests
{
    // Reference values from a 200-step bisection in double precision.
    [Theory]
    [InlineData(0.25f, 0.378138f)]
    [InlineData(0.50f, 0.684643f)]
    [InlineData(0.75f, 0.906535f)]
    public void Css_ease_out_matches_known_values(float t, float expected)
    {
        SceneMotion.CubicBezier(0f, 0f, 0.58f, 1f, t).ShouldBe(expected, 1e-3f);
        SceneEasing.EaseOutCss.Apply(t).ShouldBe(expected, 1e-3f);
    }

    [Theory]
    [InlineData(0.25f, 0.032039f)]
    [InlineData(0.50f, 0.145659f)]
    [InlineData(0.75f, 0.391804f)]
    public void Wormhole_suck_curve_matches_known_values(float t, float expected)
    {
        SceneMotion.CubicBezier(0.6f, 0f, 0.9f, 0.5f, t).ShouldBe(expected, 1e-3f);
        SceneEasing.Suck.Apply(t).ShouldBe(expected, 1e-3f);
    }

    [Fact]
    public void Overshoot_curve_leaves_one_and_comes_back()
    {
        SceneMotion.CubicBezier(0.3f, 1.5f, 0.5f, 1f, 0.5f).ShouldBe(1.079436f, 1e-3f);
        SceneMotion.CubicBezier(0.3f, 1.5f, 0.5f, 1f, 1f).ShouldBe(1f);
    }

    [Fact]
    public void Bezier_hits_both_ends_is_monotone_and_linear_for_the_diagonal()
    {
        foreach (var easing in new[] { SceneEasing.Suck, SceneEasing.EaseOutCss, SceneEasing.ArcRise, SceneEasing.ArcLand, SceneEasing.SheetRise, SceneEasing.Shake })
        {
            easing.Apply(0f).ShouldBe(0f);
            easing.Apply(1f).ShouldBe(1f);
            var last = 0f;
            for (var i = 1; i <= 200; i++)
            {
                var v = easing.Apply(i / 200f);
                v.ShouldBeGreaterThanOrEqualTo(last - 1e-6f);
                last = v;
            }
        }

        for (var i = 0; i <= 10; i++)
        {
            SceneMotion.CubicBezier(0f, 0f, 1f, 1f, i / 10f).ShouldBe(i / 10f, 1e-5f);
        }
    }

    [Fact]
    public void Phase_is_a_clamped_window()
    {
        SceneMotion.Phase(0.5f, 1f, 2f).ShouldBe(0f);
        SceneMotion.Phase(1.5f, 1f, 2f).ShouldBe(0.5f, 1e-6f);
        SceneMotion.Phase(3f, 1f, 2f).ShouldBe(1f);
    }

    [Fact]
    public void Keyframes_hit_their_stations_and_use_the_easing_of_the_front_station()
    {
        var spec = new GhostSpec(GhostShape.KindSymbol, MediaKind.Audio, "Audio", null, null, null);
        GhostKeyframe[] track =
        [
            new(0f, new Vector2(0f, 0f), 100f, 1f, 1f, 0f, 1f, 0f, SceneEasing.Suck),
            new(0.5f, new Vector2(100f, 50f), 20f, 0.5f, 2f, -1f, 0f, 0.5f, SceneEasing.Linear),
            new(1f, new Vector2(200f, 50f), 60f, 1f, 1f, 0f, 0f, 1f, SceneEasing.Linear),
        ];

        SceneMotion.Evaluate(spec, track, 0f).Position.ShouldBe(Vector2.Zero);
        var station = SceneMotion.Evaluate(spec, track, 0.5f);
        station.Position.ShouldBe(new Vector2(100f, 50f));
        station.Width.ShouldBe(20f);
        station.Rotation.ShouldBe(-1f);

        // Halfway through the first section the share is Suck(0.5), not 0.5.
        var s = SceneEasing.Suck.Apply(0.5f);
        var early = SceneMotion.Evaluate(spec, track, 0.25f);
        early.Position.X.ShouldBe(100f * s, 1e-3f);
        early.Width.ShouldBe(float.Lerp(100f, 20f, s), 1e-3f);
        early.AlphaA.ShouldBe(1f - s, 1e-4f);

        // The second section is linear.
        SceneMotion.Evaluate(spec, track, 0.75f).Position.X.ShouldBe(150f, 1e-3f);
        SceneMotion.Evaluate(spec, track, 2f).Width.ShouldBe(60f);
    }
}
