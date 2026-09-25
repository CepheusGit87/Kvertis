using System.Collections.Generic;
using System.Numerics;
using Kvertis.App.Scenes;
using Kvertis.Engine.Abstractions;
using Shouldly;
using Xunit;

namespace Kvertis.App.Tests.Scenes;

public class GalaxySceneTests
{
    [Fact]
    public void Update_clamps_a_long_step_so_nothing_jumps_after_a_pause()
    {
        var scene = SceneTestData.NewScene();

        scene.Update(TimeSpan.FromSeconds(5));

        scene.Time.ShouldBe(GalaxyScene.MaxStep);
    }

    [Fact]
    public void A_new_file_waits_flies_and_lands_on_its_orbit()
    {
        var scene = SceneTestData.NewScene();
        var id = Guid.NewGuid();
        scene.Enqueue(new AddBody(id, MediaKind.Image, "PNG", "foto.png", 2_000_000));

        SceneTestData.Advance(scene, 0.1);
        var body = scene.Bodies.Single();
        body.Phase.ShouldBe(BodyPhase.Waiting);
        body.Position.ShouldBe(body.Origin);
        body.PhaseStart.ShouldBe(TimeSpan.FromSeconds(0.1));

        // 0.375 s into the 0.75 s flight: exactly the middle of the eased Bézier curve.
        SceneTestData.Advance(scene, 0.375);
        body.Phase.ShouldBe(BodyPhase.Approaching);

        var target = scene.OrbitPoint(body.Orbit, body.Angle);
        var control = GalaxyMotion.ApproachControl(body.Origin, target, scene.Layout.Center.X, scene.Layout.Scale);
        var expected = GalaxyMotion.Approach(body.Origin, control, target, GalaxyMotion.Ease(0.5f));
        Vector2.Distance(body.Position, expected).ShouldBeLessThan(0.01f);
        body.Trail.Count.ShouldBeGreaterThan(0);

        SceneTestData.Advance(scene, 0.375);
        body.Phase.ShouldBe(BodyPhase.Orbiting);
        Vector2.Distance(body.Position, scene.OrbitPoint(body.Orbit, body.Angle)).ShouldBeLessThan(0.01f);
        body.Trail.ShouldBeEmpty();
        body.ArrivedAt.ShouldBe(TimeSpan.FromSeconds(0.85));
    }

    [Fact]
    public void Files_of_one_batch_start_staggered_by_160_milliseconds()
    {
        var scene = SceneTestData.NewScene();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var third = Guid.NewGuid();
        scene.Enqueue(new AddBody(first, MediaKind.Image, "PNG", "a.png", 1000));
        scene.Enqueue(new AddBody(second, MediaKind.Audio, "MP3", "b.mp3", 1000));
        scene.Enqueue(new AddBody(third, MediaKind.Document, "PDF", "c.pdf", 1000));

        SceneTestData.Advance(scene, 0.85);

        var bodies = scene.Bodies;
        bodies[1].PhaseStart.ShouldBe(bodies[0].PhaseStart + GalaxyBody.ApproachStagger);
        bodies[2].PhaseStart.ShouldBe(bodies[0].PhaseStart + (GalaxyBody.ApproachStagger * 2));
        bodies[0].Phase.ShouldBe(BodyPhase.Orbiting);
        bodies[1].Phase.ShouldBe(BodyPhase.Approaching);
        bodies[2].Phase.ShouldBe(BodyPhase.Approaching);

        // The running number fixes the angle on the orbit: n * 1.9 rad plus the orbit phase.
        bodies[1].Ordinal.ShouldBe(1);
        bodies[2].Ordinal.ShouldBe(2);
    }

    [Fact]
    public void A_rejected_file_flies_to_the_rejected_card()
    {
        var scene = SceneTestData.NewScene();
        var anchor = new Vector2(640f, 40f);
        var id = Guid.NewGuid();
        scene.Enqueue(new SetRejectedAnchor(anchor));
        scene.Enqueue(new AddBody(id, MediaKind.Image, "PNG", "kaputt.png", 1000));
        SceneTestData.Advance(scene, 1.0);
        scene.Bodies.Single().Phase.ShouldBe(BodyPhase.Orbiting);

        scene.Enqueue(new RejectBody(id));
        SceneTestData.Advance(scene, 1.0);

        var body = scene.Bodies.Single();
        body.IsRejected.ShouldBeTrue();
        body.Orbit.ShouldBe(-1);
        body.Phase.ShouldBe(BodyPhase.Rejected);
        Vector2.Distance(body.Position, anchor).ShouldBeLessThan(0.01f);
    }

    [Fact]
    public void Removing_a_file_takes_its_planet_and_its_halo_out_of_the_scene()
    {
        var scene = SceneTestData.NewScene();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        scene.Enqueue(new AddBody(first, MediaKind.Image, "PNG", "a.png", 1000));
        scene.Enqueue(new AddBody(second, MediaKind.Audio, "MP3", "b.mp3", 1000));
        SceneTestData.Advance(scene, 1.2);
        CountHalos(scene).ShouldBe(68);

        scene.Enqueue(new RemoveBody(first));
        SceneTestData.Advance(scene, 0.1);

        scene.Bodies.Count.ShouldBe(1);
        scene.Bodies[0].Id.ShouldBe(second);
        scene.Snapshot.BodyPositions.ShouldNotContainKey(first);
        CountHalos(scene).ShouldBe(34);
        MaxBodyIndex(scene).ShouldBe(0);
    }

    [Fact]
    public void Clear_empties_the_scene_and_restarts_the_running_number()
    {
        var scene = SceneTestData.NewScene();
        scene.Enqueue(new AddBody(Guid.NewGuid(), MediaKind.Image, "PNG", "a.png", 1000));
        SceneTestData.Advance(scene, 1.2);

        scene.Enqueue(new Clear());
        scene.Enqueue(new AddBody(Guid.NewGuid(), MediaKind.Video, "MP4", "b.mp4", 1000));
        SceneTestData.Advance(scene, 0.1);

        scene.Bodies.Count.ShouldBe(1);
        scene.Bodies[0].Ordinal.ShouldBe(0);
        scene.Snapshot.BodyPositions.Count.ShouldBe(1);
    }

    [Fact]
    public void The_orbit_bulges_by_at_most_19_pixels_under_the_pointer()
    {
        var scene = SceneTestData.NewScene();
        var plain = scene.OrbitPoint(4, 0f);
        Vector2.Distance(plain, scene.Layout.Center).ShouldBe(scene.OrbitRadiusX[4], 0.001f);

        scene.Enqueue(new SetPointer(scene.Layout.Center + new Vector2(scene.OrbitRadiusX[4], 0f)));
        SceneTestData.Advance(scene, 2.0);

        scene.OrbitHover[4].ShouldBeGreaterThan(0.99f);
        var bulged = Vector2.Distance(scene.OrbitPoint(4, 0f), scene.Layout.Center) - scene.OrbitRadiusX[4];
        bulged.ShouldBeGreaterThan(18f);
        bulged.ShouldBeLessThanOrEqualTo(GalaxyScene.BulgeHeight + 0.001f);

        // On the far side of the same orbit the bulge has died away.
        var opposite = Vector2.Distance(scene.OrbitPoint(4, MathF.PI), scene.Layout.Center) - scene.OrbitRadiusX[4];
        opposite.ShouldBeLessThan(0.1f);
    }

    [Fact]
    public void Dust_near_the_pointer_is_pulled_towards_it_and_far_dust_is_not()
    {
        var scene = SceneTestData.NewScene();
        var pointer = scene.Layout.Center + new Vector2(204f, 0f);
        scene.Enqueue(new SetPointer(pointer));
        SceneTestData.Advance(scene, 1.0);

        var particles = scene.Particles;
        var nearest = -1;
        var nearestDistance = float.MaxValue;
        var farChecked = 0;

        for (var i = 0; i < particles.Length; i++)
        {
            ref readonly var particle = ref particles[i];
            if (particle.BodyIndex >= 0)
            {
                continue;
            }

            var origin = SceneTestData.DustBase(scene, particle);
            var distance = Vector2.Distance(origin, pointer);
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearest = i;
            }

            if (distance > 300f)
            {
                Vector2.Distance(particle.Position, origin).ShouldBeLessThan(0.001f);
                farChecked++;
            }
        }

        farChecked.ShouldBeGreaterThan(0);
        nearest.ShouldBeGreaterThanOrEqualTo(0);
        nearestDistance.ShouldBeLessThan(50f);

        ref readonly var pulled = ref particles[nearest];
        var basePosition = SceneTestData.DustBase(scene, pulled);
        var shift = pulled.Position - basePosition;
        shift.Length().ShouldBeGreaterThan(0f);
        shift.Length().ShouldBeLessThan(20f);
        Vector2.Dot(shift, pointer - basePosition).ShouldBeGreaterThan(0f);
    }

    [Fact]
    public void The_pointer_influence_fades_out_within_two_seconds()
    {
        var scene = SceneTestData.NewScene();
        scene.Enqueue(new SetPointer(scene.Layout.Center + new Vector2(204f, 0f)));
        SceneTestData.Advance(scene, 1.0);
        scene.PointerState.Strength.ShouldBeGreaterThan(0.9f);

        scene.Enqueue(new SetPointer(null));
        SceneTestData.Advance(scene, 2.0);

        scene.PointerState.Strength.ShouldBeLessThan(0.01f);
        scene.PointerState.HasPointer.ShouldBeFalse();
    }

    [Fact]
    public void A_hovered_tray_lights_up_the_orbit_of_its_kind()
    {
        var scene = SceneTestData.NewScene();
        scene.Enqueue(new SetTrayHover(MediaKind.Audio));

        SceneTestData.Advance(scene, 2.0);

        scene.OrbitHover[GalaxyLayout.OrbitOf(MediaKind.Audio)].ShouldBeGreaterThan(0.99f);
        scene.OrbitHover[4].ShouldBeLessThan(0.01f);
        scene.Snapshot.HoveredOrbit.ShouldBe(GalaxyLayout.OrbitOf(MediaKind.Audio));
    }

    [Fact]
    public void Zooming_in_puts_one_orbit_on_the_zoom_radius_and_fades_the_others()
    {
        var scene = SceneTestData.NewScene(1200f, 400f);
        scene.Enqueue(new ZoomTo(MediaKind.Image));

        SceneTestData.Advance(scene, 1.0);
        scene.Zoom.ShouldBe(ZoomState.ZoomingIn);
        scene.ZoomProgress.ShouldBeLessThan(1f);

        SceneTestData.Advance(scene, 0.2);
        scene.Zoom.ShouldBe(ZoomState.Zoomed);
        scene.ZoomKind.ShouldBe(MediaKind.Image);
        scene.ZoomProgress.ShouldBe(1f, 0.0001f);
        scene.OrbitRadiusX[4].ShouldBe(scene.Layout.ZoomRadius, 0.01f);
        scene.OrbitFlatten[4].ShouldBe(GalaxyLayout.FlattenZoomed, 0.0001f);
        for (var k = 0; k < 4; k++)
        {
            scene.OrbitAlpha[k].ShouldBe(0f, 0.0001f);
        }

        scene.Enqueue(new ZoomTo(null));
        SceneTestData.Advance(scene, 1.2);

        scene.Zoom.ShouldBe(ZoomState.Overview);
        scene.ZoomKind.ShouldBeNull();
        scene.ZoomProgress.ShouldBe(0f, 0.0001f);
        for (var k = 0; k < GalaxyLayout.OrbitCount; k++)
        {
            scene.OrbitRadiusX[k].ShouldBe(scene.Layout.BaseRadius(k), 0.01f);
            scene.OrbitAlpha[k].ShouldBe(1f, 0.0001f);
        }
    }

    [Fact]
    public void A_second_zoom_command_during_the_ride_does_not_jump()
    {
        // Reference: one whole ride in and one whole ride out, never interrupted. No interrupted ride can
        // move an orbit faster than that, because it always has less ground to cover in the same 1.15 s.
        var reference = SceneTestData.NewScene(1200f, 400f);
        var uninterrupted = new float[GalaxyLayout.OrbitCount];
        reference.Enqueue(new ZoomTo(MediaKind.Image));
        RunFrames(reference, 75, uninterrupted, -1);
        reference.Enqueue(new ZoomTo(null));
        RunFrames(reference, 75, uninterrupted, -1);

        var scene = SceneTestData.NewScene(1200f, 400f);
        var interrupted = new float[GalaxyLayout.OrbitCount];
        scene.Enqueue(new ZoomTo(MediaKind.Image));
        RunFrames(scene, 150, interrupted, 31);

        scene.Zoom.ShouldBe(ZoomState.Overview);
        for (var k = 0; k < GalaxyLayout.OrbitCount; k++)
        {
            interrupted[k].ShouldBeLessThanOrEqualTo(uninterrupted[k] + 0.01f);
        }

        // The orbit the camera drives to stays well under 15 px per 16 ms frame.
        interrupted[4].ShouldBeLessThan(15f);
    }

    [Fact]
    public void The_dwell_timer_grows_while_the_pointer_rests_and_resets_when_it_moves()
    {
        var scene = SceneTestData.NewScene();
        var pointer = scene.Layout.Center + new Vector2(316f, 0f);
        scene.Enqueue(new SetPointer(pointer));

        SceneTestData.Advance(scene, 1.0);
        scene.Dwell.Orbit.ShouldBe(4);
        scene.Dwell.OnHole.ShouldBeFalse();
        scene.Dwell.Duration.ShouldBeGreaterThan(TimeSpan.FromSeconds(0.9));

        scene.Enqueue(new SetPointer(pointer + new Vector2(10f, 0f)));
        SceneTestData.Advance(scene, 0.025);
        scene.Dwell.Duration.ShouldBe(TimeSpan.Zero);

        scene.Enqueue(new SetPointer(scene.Layout.Center));
        SceneTestData.Advance(scene, 0.5);
        scene.Dwell.OnHole.ShouldBeTrue();

        scene.Enqueue(new SetPointer(null));
        SceneTestData.Advance(scene, 0.1);
        scene.Dwell.Orbit.ShouldBeNull();
        scene.Dwell.OnHole.ShouldBeFalse();
        scene.Dwell.Duration.ShouldBe(TimeSpan.Zero);
    }

    [Fact]
    public void Many_files_get_the_smaller_halo_and_the_budget_is_never_exceeded()
    {
        var budget = new GalaxyBudget();
        var scene = SceneTestData.NewScene(budget: budget);

        for (var i = 0; i < 40; i++)
        {
            scene.Enqueue(new AddBody(Guid.NewGuid(), MediaKind.Image, "PNG", $"{i}.png", 1000));
        }

        SceneTestData.Advance(scene, 0.1);

        scene.Bodies.Count.ShouldBe(40);
        CountHalos(scene).ShouldBe(40 * budget.HaloPerBodyManyFiles);
        scene.Particles.Length.ShouldBe((5 * budget.DustPerOrbit) + (40 * budget.HaloPerBodyManyFiles));

        for (var i = 0; i < 400; i++)
        {
            scene.Enqueue(new AddBody(Guid.NewGuid(), MediaKind.Audio, "MP3", $"{i}.mp3", 1000));
        }

        SceneTestData.Advance(scene, 0.1);

        scene.Bodies.Count.ShouldBe(440);
        scene.Particles.Length.ShouldBeLessThanOrEqualTo(budget.MaxParticles);
    }

    [Fact]
    public void The_snapshot_is_immutable_and_keeps_no_reference_into_the_scene()
    {
        var scene = SceneTestData.NewScene();
        var id = Guid.NewGuid();
        scene.Enqueue(new AddBody(id, MediaKind.Image, "PNG", "a.png", 1000));
        SceneTestData.Advance(scene, 1.0);

        var snapshot = scene.Snapshot;
        var seen = snapshot.BodyPositions[id];
        snapshot.Time.ShouldBe(TimeSpan.FromSeconds(1.0));

        SceneTestData.Advance(scene, 1.0);

        scene.Snapshot.ShouldNotBeSameAs(snapshot);
        snapshot.BodyPositions[id].ShouldBe(seen);
        snapshot.Time.ShouldBe(TimeSpan.FromSeconds(1.0));
        scene.Snapshot.BodyPositions[id].ShouldNotBe(seen);
        Should.Throw<NotSupportedException>(() =>
            ((IDictionary<Guid, Vector2>)snapshot.BodyPositions).Add(Guid.NewGuid(), Vector2.Zero));
    }

    [Fact]
    public void Two_scenes_with_the_same_seed_and_the_same_commands_stay_identical()
    {
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        var first = SceneTestData.NewScene();
        var second = SceneTestData.NewScene();

        foreach (var scene in new[] { first, second })
        {
            scene.Enqueue(new AddBody(ids[0], MediaKind.Image, "PNG", "a.png", 1_500_000));
            scene.Enqueue(new AddBody(ids[1], MediaKind.Audio, "MP3", "b.mp3", 4_000_000));
            scene.Enqueue(new AddBody(ids[2], MediaKind.Document, "PDF", "c.pdf", 9_000_000));
            scene.Enqueue(new SetPointer(scene.Layout.Center + new Vector2(150f, 20f)));
            SceneTestData.Advance(scene, 5.0);
        }

        first.Snapshot.Time.ShouldBe(second.Snapshot.Time);
        first.Snapshot.ZoomProgress.ShouldBe(second.Snapshot.ZoomProgress);
        foreach (var id in ids)
        {
            first.Snapshot.BodyPositions[id].ShouldBe(second.Snapshot.BodyPositions[id]);
        }

        first.Particles.Length.ShouldBe(second.Particles.Length);
        for (var i = 0; i < first.Particles.Length; i++)
        {
            first.Particles[i].Position.ShouldBe(second.Particles[i].Position);
        }
    }

    [Fact]
    public void Resizing_moves_the_orbits_to_the_new_surface()
    {
        var scene = SceneTestData.NewScene();
        scene.Enqueue(new Resize(1400f, 560f));

        SceneTestData.Advance(scene, 0.1);

        scene.Layout.Width.ShouldBe(1400f);
        scene.Layout.Scale.ShouldBe(1.5f, 0.0001f);
        scene.OrbitRadiusX[0].ShouldBe(scene.Layout.BaseRadius(0), 0.01f);
        scene.HoleRadius.ShouldBe(GalaxyLayout.HoleRadiusOverview * 1.5f, 0.01f);
    }

    [Fact]
    public void A_drag_over_the_window_grows_the_hole()
    {
        var scene = SceneTestData.NewScene();
        var quiet = scene.HoleRadius;

        scene.Enqueue(new SetDragOver(true));
        SceneTestData.Advance(scene, 1.0);
        scene.DragOver.ShouldBeGreaterThan(0.99f);
        scene.HoleRadius.ShouldBe(quiet * 1.7f, 0.05f);

        scene.Enqueue(new SetDragOver(false));
        SceneTestData.Advance(scene, 1.0);
        scene.HoleRadius.ShouldBe(quiet, 0.05f);
    }

    [Fact]
    public void Paths_appear_only_well_into_the_zoom()
    {
        var scene = SceneTestData.NewScene(1200f, 400f);
        scene.Enqueue(new ZoomTo(MediaKind.Image));
        scene.Enqueue(new SetPathAnchors(
            [new PathAnchor("heic", new Vector2(120f, 180f))],
            [new PathAnchor("jpg", new Vector2(1080f, 150f)), new PathAnchor("gif", new Vector2(1080f, 220f))],
            "heic",
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "jpg" },
            "jpg"));

        SceneTestData.Advance(scene, 0.4);
        scene.Paths.ShouldBeEmpty();

        SceneTestData.Advance(scene, 0.8);
        var path = scene.Paths.ShouldHaveSingleItem();
        path.FormatId.ShouldBe("jpg");
        path.IsRecommended.ShouldBeTrue();
        path.PointAt(0f).ShouldBe(new Vector2(120f, 180f));
        path.PointAt(0.5f).ShouldBe(scene.Layout.Center);
        path.PointAt(1f).ShouldBe(new Vector2(1080f, 150f));
    }

    [Fact]
    public void The_planet_size_grows_slowly_with_the_file_size()
    {
        GalaxyScene.SizeFactorOf(0).ShouldBe(0.6f, 0.0001f);
        GalaxyScene.SizeFactorOf(20L * 1024 * 1024).ShouldBe(1.05f, 0.0001f);
        GalaxyScene.SizeFactorOf(100_000L * 1024 * 1024).ShouldBe(1.8f, 0.0001f);
    }

    /// <summary>Runs 16 ms frames and records the largest radius change per orbit; -1 = never interrupt.</summary>
    private static void RunFrames(GalaxyScene scene, int frames, float[] maxDelta, int zoomOutAt)
    {
        var frame = TimeSpan.FromMilliseconds(16);
        var previous = new float[GalaxyLayout.OrbitCount];
        for (var k = 0; k < GalaxyLayout.OrbitCount; k++)
        {
            previous[k] = scene.OrbitRadiusX[k];
        }

        for (var step = 0; step < frames; step++)
        {
            if (step == zoomOutAt)
            {
                scene.Enqueue(new ZoomTo(null));
            }

            scene.Update(frame);
            for (var k = 0; k < GalaxyLayout.OrbitCount; k++)
            {
                var delta = MathF.Abs(scene.OrbitRadiusX[k] - previous[k]);
                if (delta > maxDelta[k])
                {
                    maxDelta[k] = delta;
                }

                previous[k] = scene.OrbitRadiusX[k];
            }
        }
    }

    private static int CountHalos(GalaxyScene scene)
    {
        var count = 0;
        var particles = scene.Particles;
        for (var i = 0; i < particles.Length; i++)
        {
            if (particles[i].BodyIndex >= 0)
            {
                count++;
            }
        }

        return count;
    }

    private static int MaxBodyIndex(GalaxyScene scene)
    {
        var max = -1;
        var particles = scene.Particles;
        for (var i = 0; i < particles.Length; i++)
        {
            if (particles[i].BodyIndex > max)
            {
                max = particles[i].BodyIndex;
            }
        }

        return max;
    }
}
