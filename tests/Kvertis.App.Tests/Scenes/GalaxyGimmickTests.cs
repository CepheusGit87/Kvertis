using System.Numerics;
using Kvertis.App.Scenes;
using Kvertis.Engine.Abstractions;
using Shouldly;
using Xunit;

namespace Kvertis.App.Tests.Scenes;

/// <summary>
/// The gimmicks of step 1: whirl and new planet after about four seconds on an orbit, big bang after ten
/// seconds on the hole. Timeline, reset on movement, budget, restoration after the big bang, and the cases
/// in which nothing may happen (zoom, drag, files in flight, gimmicks not enabled).
/// </summary>
public class GalaxyGimmickTests
{
    private static readonly TimeSpan Frame = SceneTestData.Step;

    [Fact]
    public void Nothing_happens_while_the_gimmicks_are_not_enabled()
    {
        var scene = SceneTestData.NewScene();
        scene.Enqueue(new SetPointer(OnOrbit(scene, 4)));

        SceneTestData.Advance(scene, 6.0);

        scene.GimmicksEnabled.ShouldBeFalse();
        scene.Gimmick.ShouldBe(GimmickState.Idle);
        scene.WhirlCharge.ShouldBe(0f);
        scene.Planets.ShouldBeEmpty();

        scene.Enqueue(new SetPointer(scene.Layout.Center));
        SceneTestData.Advance(scene, 12.0);

        scene.Gimmick.ShouldBe(GimmickState.Idle);
        scene.HoleCharge.ShouldBe(0f);
        scene.SurfaceSuction.ShouldBe(0f);
    }

    [Fact]
    public void A_pointer_resting_four_seconds_on_an_orbit_forms_a_planet()
    {
        var scene = Enabled();
        scene.Enqueue(new SetPointer(OnOrbit(scene, 4)));

        SceneTestData.Advance(scene, 2.0);
        scene.Gimmick.ShouldBe(GimmickState.Whirl);
        scene.WhirlOrbit.ShouldBe(4);
        scene.WhirlCharge.ShouldBeInRange(0.4f, 0.5f);
        scene.WhirlSpin.ShouldBeGreaterThan(0f);
        scene.Planets.ShouldBeEmpty();

        SceneTestData.Advance(scene, 2.3);
        var planet = scene.Planets.ShouldHaveSingleItem();
        scene.Gimmick.ShouldBe(GimmickState.Flash);
        scene.Bursts.ShouldHaveSingleItem();
        planet.Orbit.ShouldBe(4);
        planet.BornAt.ShouldBeInRange(TimeSpan.FromSeconds(4.1), TimeSpan.FromSeconds(4.3));
        planet.Fall.ShouldBe(0f);
        scene.WhirlCharge.ShouldBe(0f);
        CountPlanetHalo(scene).ShouldBe(new GalaxyBudget().HaloPerPlanet);
        scene.Snapshot.Gimmick.ShouldBe(GimmickState.Flash);

        SceneTestData.Advance(scene, 1.0);
        scene.Gimmick.ShouldBe(GimmickState.NewPlanet);
        scene.Bursts.ShouldBeEmpty();
        Vector2.Distance(planet.Position, scene.OrbitPoint(4, planet.Angle + scene.OrbitPhase[4])).ShouldBeLessThan(0.01f);

        // Resting on does not form a second one: the pointer has to move away first.
        SceneTestData.Advance(scene, 5.0);
        scene.Planets.Count.ShouldBe(1);
    }

    [Fact]
    public void Six_kinds_of_planet_take_turns()
    {
        var scene = Enabled();
        var spot = OnOrbit(scene, 4);
        var kinds = new List<PlanetKind>();
        for (var i = 0; i < 7; i++)
        {
            // Every birth needs a fresh dwell: 10 px along the orbit is enough to unlock and start again.
            scene.Enqueue(new SetPointer(spot + new Vector2(i % 2 == 0 ? 0f : -10f, 0f)));
            SceneTestData.Advance(scene, 4.4);
            scene.Planets.Count.ShouldBe(i + 1);
            kinds.Add(scene.Planets[i].Kind);
        }

        kinds.Take(6).Distinct().Count().ShouldBe(6);
        for (var i = 1; i < kinds.Count; i++)
        {
            ((int)kinds[i]).ShouldBe(((int)kinds[i - 1] + 1) % 6);
        }
    }

    [Fact]
    public void Moving_the_pointer_more_than_six_pixels_resets_the_whirl()
    {
        var scene = Enabled();
        var spot = OnOrbit(scene, 4);
        scene.Enqueue(new SetPointer(spot));
        SceneTestData.Advance(scene, 2.0);
        var charged = scene.WhirlCharge;
        charged.ShouldBeGreaterThan(0.4f);

        scene.Enqueue(new SetPointer(spot + new Vector2(-8f, 0f)));
        var previous = charged;
        for (var i = 0; i < 4; i++)
        {
            scene.Update(Frame);
            scene.WhirlCharge.ShouldBeLessThan(previous);
            (previous - scene.WhirlCharge).ShouldBeLessThanOrEqualTo(1.4f * (float)Frame.TotalSeconds + 0.001f);
            previous = scene.WhirlCharge;
        }

        // Four seconds after the old start there is no planet; it needs four seconds from the new rest.
        SceneTestData.Advance(scene, 2.4);
        scene.Planets.ShouldBeEmpty();
        SceneTestData.Advance(scene, 2.5);
        scene.Planets.ShouldHaveSingleItem();
    }

    [Fact]
    public void Leaving_the_surface_lets_the_whirl_collapse_softly()
    {
        var scene = Enabled();
        scene.Enqueue(new SetPointer(OnOrbit(scene, 4)));
        SceneTestData.Advance(scene, 2.0);
        scene.WhirlCharge.ShouldBeGreaterThan(0.4f);

        scene.Enqueue(new SetPointer(null));
        var previous = scene.WhirlCharge;
        var frames = 0;
        while (scene.WhirlCharge > 0f && frames < 200)
        {
            scene.Update(Frame);
            frames++;
            (previous - scene.WhirlCharge).ShouldBeLessThanOrEqualTo(1.1f * (float)Frame.TotalSeconds + 0.001f);
            previous = scene.WhirlCharge;
        }

        scene.WhirlCharge.ShouldBe(0f);
        frames.ShouldBeLessThan(60);
        scene.Gimmick.ShouldBe(GimmickState.Idle);
        scene.Planets.ShouldBeEmpty();
    }

    [Fact]
    public void Ten_seconds_on_the_hole_end_in_a_big_bang_that_gives_every_file_back()
    {
        var scene = Enabled();
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        scene.Enqueue(new AddBody(ids[0], MediaKind.Image, "PNG", "a.png", 2_000_000));
        scene.Enqueue(new AddBody(ids[1], MediaKind.Audio, "MP3", "b.mp3", 4_000_000));
        scene.Enqueue(new AddBody(ids[2], MediaKind.Document, "PDF", "c.pdf", 9_000_000));
        SceneTestData.Advance(scene, 1.5);
        scene.Bodies.All(b => b.Phase == BodyPhase.Orbiting).ShouldBeTrue();
        var particlesBefore = scene.Particles.Length;

        scene.Enqueue(new SetPointer(scene.Layout.Center));
        SceneTestData.Advance(scene, 5.0);
        scene.Gimmick.ShouldBe(GimmickState.Suction);
        scene.HoleCharge.ShouldBe(0.5f, 0.01f);
        scene.HoleSpin.ShouldBeGreaterThan(0f);
        scene.GimmickOrbitAlpha.ShouldBe(0.7f, 0.01f);
        scene.SurfaceSuction.ShouldBe(0f);

        SceneTestData.Advance(scene, 3.5);
        scene.Gimmick.ShouldBe(GimmickState.Swallowing);
        scene.SurfaceSuction.ShouldBeGreaterThan(0f);
        scene.SurfaceOpacity.ShouldBeLessThan(1f);
        scene.SurfaceJitter.ShouldBeGreaterThan(0f);
        scene.Snapshot.SurfaceAffected.ShouldBeTrue();
        scene.Snapshot.HoleCenter.ShouldBe(scene.Layout.Center);

        SceneTestData.Advance(scene, 1.6);
        scene.Gimmick.ShouldBe(GimmickState.BigBang);
        scene.HoleCharge.ShouldBe(0f);
        scene.SurfaceSuction.ShouldBe(1f);
        scene.SurfaceOpacity.ShouldBe(0f);
        scene.GimmickOrbitAlpha.ShouldBe(0f);
        scene.Snapshot.Gimmick.ShouldBe(GimmickState.BigBang);

        SceneTestData.Advance(scene, 1.4);
        scene.Gimmick.ShouldBe(GimmickState.NewUniverse);

        SceneTestData.Advance(scene, 3.5);
        scene.Gimmick.ShouldBe(GimmickState.Idle);
        scene.SurfaceSuction.ShouldBe(0f);
        scene.SurfaceOpacity.ShouldBe(1f);
        scene.Snapshot.SurfaceAffected.ShouldBeFalse();
        scene.GimmickOrbitAlpha.ShouldBe(1f);

        // No file was lost: every planet circles on its orbit again, every halo particle sits where it belongs.
        scene.Bodies.Count.ShouldBe(3);
        scene.Particles.Length.ShouldBe(particlesBefore);
        foreach (var body in scene.Bodies)
        {
            body.Phase.ShouldBe(BodyPhase.Orbiting);
            Vector2.Distance(body.Position, scene.OrbitPoint(body.Orbit, body.Angle)).ShouldBeLessThan(0.01f);
        }

        scene.Enqueue(new SetPointer(null));
        SceneTestData.Advance(scene, 3.0);
        AssertParticlesAtRest(scene);

        // The lock after the explosion: resting on again starts nothing until the pointer left the hole.
        scene.Enqueue(new SetPointer(scene.Layout.Center));
        SceneTestData.Advance(scene, 2.0);
        scene.HoleCharge.ShouldBeGreaterThan(0.1f);
    }

    [Fact]
    public void Self_formed_planets_crash_into_the_charging_hole()
    {
        var scene = Enabled();
        scene.Enqueue(new SetPointer(OnOrbit(scene, 4)));
        SceneTestData.Advance(scene, 4.4);
        scene.Planets.ShouldHaveSingleItem();
        scene.Particles.Length.ShouldBe(5 * 160 + 44);

        scene.Enqueue(new SetPointer(scene.Layout.Center));
        var seen = new HashSet<GimmickState>();
        var maxFall = 0f;
        for (var i = 0; i < 280; i++)
        {
            scene.Update(Frame);
            seen.Add(scene.Gimmick);
            if (scene.Planets.Count == 1)
            {
                maxFall = Math.Max(maxFall, scene.Planets[0].Fall);
                if (scene.Planets[0].Fall > 0.1f)
                {
                    scene.Planets[0].Trail.Count.ShouldBeGreaterThan(0);
                    scene.Planets[0].Shrink.ShouldBeLessThan(1f);
                }
            }
        }

        seen.ShouldContain(GimmickState.Impacts);
        seen.ShouldNotContain(GimmickState.BigBang);
        maxFall.ShouldBeGreaterThan(0.5f);
        scene.Planets.ShouldBeEmpty();
        scene.Particles.Length.ShouldBe(5 * 160);
        scene.HoleFlash.At.ShouldNotBeNull();
        scene.HoleFlash.At!.Value.ShouldBeGreaterThan(TimeSpan.FromSeconds(4.4));
        scene.Bursts.Count.ShouldBeLessThanOrEqualTo(1);
    }

    [Fact]
    public void Leaving_the_hole_lets_the_charge_and_the_surface_return_softly()
    {
        var scene = Enabled();
        scene.Enqueue(new SetPointer(scene.Layout.Center));
        SceneTestData.Advance(scene, 9.0);
        scene.Gimmick.ShouldBe(GimmickState.Swallowing);
        var suction = scene.SurfaceSuction;
        suction.ShouldBeGreaterThan(0f);

        scene.Enqueue(new SetPointer(null));
        var charge = scene.HoleCharge;
        var frames = 0;
        while (scene.HoleCharge > 0f && frames < 400)
        {
            scene.Update(Frame);
            frames++;
            scene.Gimmick.ShouldNotBe(GimmickState.BigBang);
            (charge - scene.HoleCharge).ShouldBeLessThanOrEqualTo(0.6f * (float)Frame.TotalSeconds + 0.001f);
            scene.SurfaceSuction.ShouldBeLessThanOrEqualTo(suction + 0.0001f);
            charge = scene.HoleCharge;
            suction = scene.SurfaceSuction;
        }

        frames.ShouldBeInRange(50, 80);
        scene.Gimmick.ShouldBe(GimmickState.Idle);
        scene.SurfaceSuction.ShouldBe(0f);
        scene.Shake.ShouldBe(Vector2.Zero);
    }

    [Fact]
    public void Nothing_happens_in_the_zoom()
    {
        var scene = Enabled(1200f, 400f);
        scene.Enqueue(new ZoomTo(MediaKind.Image));
        SceneTestData.Advance(scene, 1.3);
        scene.Zoom.ShouldBe(ZoomState.Zoomed);

        scene.Enqueue(new SetPointer(scene.Layout.Center));
        SceneTestData.Advance(scene, 12.0);
        scene.Gimmick.ShouldBe(GimmickState.Idle);
        scene.HoleCharge.ShouldBe(0f);

        scene.Enqueue(new SetPointer(scene.OrbitPoint(4, 0f)));
        SceneTestData.Advance(scene, 6.0);
        scene.Gimmick.ShouldBe(GimmickState.Idle);
        scene.Planets.ShouldBeEmpty();

        // Back in the overview the same rest works again.
        scene.Enqueue(new ZoomTo(null));
        SceneTestData.Advance(scene, 1.3);
        scene.Enqueue(new SetPointer(OnOrbit(scene, 4)));
        SceneTestData.Advance(scene, 4.4);
        scene.Planets.ShouldHaveSingleItem();
    }

    [Fact]
    public void Nothing_happens_while_files_are_dragged_or_still_flying()
    {
        var scene = Enabled();
        scene.Enqueue(new SetDragOver(true));
        scene.Enqueue(new SetPointer(OnOrbit(scene, 4)));
        SceneTestData.Advance(scene, 6.0);
        scene.Planets.ShouldBeEmpty();
        scene.Gimmick.ShouldBe(GimmickState.Idle);

        scene.Enqueue(new SetDragOver(false));
        scene.Enqueue(new AddBody(Guid.NewGuid(), MediaKind.Image, "PNG", "a.png", 1000));
        SceneTestData.Advance(scene, 0.5);
        scene.Bodies[0].Phase.ShouldBe(BodyPhase.Approaching);
        scene.WhirlCharge.ShouldBe(0f);

        SceneTestData.Advance(scene, 4.6);
        scene.Planets.ShouldHaveSingleItem();
    }

    [Fact]
    public void The_budget_caps_planets_and_their_halos()
    {
        var scene = Enabled(budget: new GalaxyBudget(MaxPlanets: 2));
        var spot = OnOrbit(scene, 4);
        for (var i = 0; i < 3; i++)
        {
            scene.Enqueue(new SetPointer(spot + new Vector2(i % 2 == 0 ? 0f : -10f, 0f)));
            SceneTestData.Advance(scene, 4.4);
        }

        scene.Planets.Count.ShouldBe(2);
        scene.Gimmick.ShouldBe(GimmickState.Idle);
        scene.Particles.Length.ShouldBe(5 * 160 + 2 * 44);

        // A tight particle budget: the planet still forms, only without its halo.
        var tight = Enabled(budget: new GalaxyBudget(MaxParticles: 820));
        tight.Enqueue(new SetPointer(OnOrbit(tight, 4)));
        SceneTestData.Advance(tight, 4.4);
        tight.Planets.ShouldHaveSingleItem();
        tight.Particles.Length.ShouldBe(5 * 160);
    }

    [Fact]
    public void Halo_particles_follow_the_whirl_only_near_it()
    {
        var scene = Enabled();
        var spot = OnOrbit(scene, 4);
        scene.Enqueue(new SetPointer(spot));
        SceneTestData.Advance(scene, 3.5);
        scene.WhirlCharge.ShouldBeGreaterThan(0.7f);

        var particles = scene.Particles;
        var moved = 0;
        for (var i = 0; i < particles.Length; i++)
        {
            ref readonly var particle = ref particles[i];
            if (particle.BodyIndex >= 0 || particle.PlanetIndex >= 0)
            {
                continue;
            }

            var rest = SceneTestData.DustBase(scene, particle);
            var shift = Vector2.Distance(particle.Position, rest);
            if (Vector2.Distance(rest, spot) > 400f)
            {
                shift.ShouldBeLessThan(0.001f);
            }
            else if (shift > 5f)
            {
                moved++;
            }
        }

        moved.ShouldBeGreaterThan(5);
    }

    private static GalaxyScene Enabled(float width = 700f, float height = 280f, GalaxyBudget? budget = null)
    {
        var scene = SceneTestData.NewScene(width, height, budget: budget);
        scene.Enqueue(new SetGimmicksEnabled(true));
        SceneTestData.Advance(scene, 0.1);
        return scene;
    }

    private static Vector2 OnOrbit(GalaxyScene scene, int orbit) =>
        scene.Layout.Center + new Vector2(scene.Layout.BaseRadius(orbit), 0f);

    private static int CountPlanetHalo(GalaxyScene scene)
    {
        var count = 0;
        var particles = scene.Particles;
        for (var i = 0; i < particles.Length; i++)
        {
            if (particles[i].PlanetIndex >= 0)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>Every dust and halo particle sits where the orbit alone puts it (no pointer, no gimmick).</summary>
    private static void AssertParticlesAtRest(GalaxyScene scene)
    {
        var particles = scene.Particles;
        var seconds = (float)scene.Time.TotalSeconds;
        for (var i = 0; i < particles.Length; i++)
        {
            ref readonly var particle = ref particles[i];
            particle.PlanetIndex.ShouldBe(-1);
            Vector2 expected;
            if (particle.BodyIndex < 0)
            {
                expected = SceneTestData.DustBase(scene, particle);
            }
            else
            {
                var body = scene.Bodies[particle.BodyIndex];
                var particleScale = MathF.Sqrt(Math.Clamp(scene.OrbitRadiusX[body.Orbit] / (GalaxyLayout.BaseRadiusInner * scene.Layout.Scale), 0.5f, 2.6f));
                var angle = particle.R1 * MathF.PI * 2f + 2f * seconds;
                var radius = (3f + 7f * particle.R2) * particleScale * body.SizeFactor;
                expected = body.Position + new Vector2(radius * MathF.Cos(angle), radius * 0.6f * MathF.Sin(angle));
            }

            Vector2.Distance(particle.Position, expected).ShouldBeLessThan(0.05f);
            particle.Alpha.ShouldBeGreaterThan(0.5f);
        }
    }
}
