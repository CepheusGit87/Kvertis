using System.Numerics;
using Kvertis.App.Scenes;
using Shouldly;
using Xunit;

namespace Kvertis.App.Tests.Scenes;

public class GalaxyMotionTests
{
    [Fact]
    public void Curves_start_at_zero_and_end_at_one()
    {
        GalaxyMotion.Ease(0f).ShouldBe(0f, 0.0001f);
        GalaxyMotion.Ease(0.5f).ShouldBe(0.5f, 0.0001f);
        GalaxyMotion.Ease(1f).ShouldBe(1f, 0.0001f);

        GalaxyMotion.Smooth(0f).ShouldBe(0f, 0.0001f);
        GalaxyMotion.Smooth(1f).ShouldBe(1f, 0.0001f);

        GalaxyMotion.EaseOut(0f).ShouldBe(0f, 0.0001f);
        GalaxyMotion.EaseOut(1f).ShouldBe(1f, 0.0001f);

        GalaxyMotion.EaseBack(0f).ShouldBe(0f, 0.0001f);
        GalaxyMotion.EaseBack(1f).ShouldBe(1f, 0.0001f);
        GalaxyMotion.EaseBack(0.7f).ShouldBeGreaterThan(1f);
    }

    [Fact]
    public void Curves_are_clamped_outside_zero_to_one()
    {
        GalaxyMotion.Ease(-2f).ShouldBe(0f);
        GalaxyMotion.Ease(7f).ShouldBe(1f);
    }

    [Fact]
    public void Approach_curve_hits_both_ends_and_bulges_towards_the_control_point()
    {
        var from = new Vector2(100f, 20f);
        var to = new Vector2(400f, 200f);
        var control = GalaxyMotion.ApproachControl(from, to, 250f, 1f);

        control.X.ShouldBe(250f, 0.0001f);
        control.Y.ShouldBe(-10f, 0.0001f);

        GalaxyMotion.Approach(from, control, to, 0f).ShouldBe(from);
        GalaxyMotion.Approach(from, control, to, 1f).ShouldBe(to);

        var middle = GalaxyMotion.Approach(from, control, to, 0.5f);
        middle.Y.ShouldBeLessThan((from.Y + to.Y) / 2f);
    }

    [Fact]
    public void Damping_is_frame_rate_independent()
    {
        // One step of 0.1 s must land where two steps of 0.05 s land.
        var once = GalaxyMotion.Approach(0f, 1f, 5f, 0.1f);
        var twice = GalaxyMotion.Approach(GalaxyMotion.Approach(0f, 1f, 5f, 0.05f), 1f, 5f, 0.05f);

        twice.ShouldBe(once, 0.0001f);
    }

    [Fact]
    public void Pointer_pull_fades_out_with_distance_and_stops_at_the_radius()
    {
        var pointer = new Vector2(500f, 300f);

        var near = GalaxyMotion.PointerPull(pointer + new Vector2(50f, 0f), pointer, 0f, 1f, 1f);
        near.Length().ShouldBeGreaterThan(0f);
        near.X.ShouldBeLessThan(0f);

        var far = GalaxyMotion.PointerPull(pointer + new Vector2(300f, 0f), pointer, 1f, 1f, 1f);
        far.ShouldBe(Vector2.Zero);

        var without = GalaxyMotion.PointerPull(pointer + new Vector2(50f, 0f), pointer, 1f, 0f, 1f);
        without.ShouldBe(Vector2.Zero);
    }

    [Fact]
    public void Wrap_angle_stays_between_minus_pi_and_pi()
    {
        GalaxyMotion.WrapAngle(MathF.PI * 2f + 0.5f).ShouldBe(0.5f, 0.0001f);
        GalaxyMotion.WrapAngle(-MathF.PI * 2f - 0.5f).ShouldBe(-0.5f, 0.0001f);
        MathF.Abs(GalaxyMotion.WrapAngle(MathF.PI * 3f)).ShouldBeLessThanOrEqualTo(MathF.PI + 0.0001f);
    }
}
