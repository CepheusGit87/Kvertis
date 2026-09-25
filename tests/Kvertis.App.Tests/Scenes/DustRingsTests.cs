using System.Diagnostics;
using System.Numerics;
using Kvertis.App.Scenes;
using Kvertis.App.Services;
using Kvertis.Engine.Abstractions;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace Kvertis.App.Tests.Scenes;

/// <summary>The white hole from 150 files on: dust rings per kind (design/weisses-loch-mengen.html, draft 2).</summary>
public class DustRingsTests(ITestOutputHelper output)
{
    private static SwirlScene WithPlan(int files, float width = 1000f, Func<int, MediaKind>? kind = null)
    {
        var scene = SceneTestTime.NewSwirl(width: width);
        scene.Enqueue(new SetPlan(SceneTestTime.Files(files, kind: kind)));
        scene.Update(TimeSpan.Zero);
        return scene;
    }

    /// <summary>Finishes files 1..<paramref name="files"/> (every <paramref name="failEvery"/>th fails) and lets them arrive.</summary>
    private static void FinishAll(SwirlScene scene, int files, int failEvery = 0)
    {
        for (var i = 1; i <= files; i++)
        {
            var fail = failEvery > 0 && i % failEvery == 0;
            scene.Enqueue(new FinishFile(SceneTestTime.Id(i), fail ? FileOutcome.Failed : FileOutcome.Completed, fail ? PixelTarget.Inbox : PixelTarget.WhiteHole));
        }

        scene.Update(SceneTestData.Step);
    }

    [Theory]
    [InlineData(149, WhiteHoleMode.Rings)]
    [InlineData(150, WhiteHoleMode.Dust)]
    [InlineData(500, WhiteHoleMode.Dust)]
    public void The_mode_switches_at_150_files(int n, WhiteHoleMode mode)
    {
        var hole = WithPlan(n).WhiteHole;

        hole.N.ShouldBe(n);
        hole.Mode.ShouldBe(mode);
        if (mode == WhiteHoleMode.Dust)
        {
            hole.OrbitLines.ShouldBeEmpty();
            hole.EmptyPlaces.ShouldBeEmpty();
            hole.Rings.Count.ShouldBe(5);
        }
        else
        {
            hole.Rings.ShouldBeEmpty();
            hole.Grains.ShouldBeEmpty();
            hole.OrbitLines.Count.ShouldBe(10);
        }
    }

    [Theory]
    [InlineData(5, 64)]
    [InlineData(20, 35)]
    [InlineData(70, 10)]
    [InlineData(150, 10)]
    [InlineData(500, 10)]
    [InlineData(5000, 10)]
    public void Grains_per_file_stay_between_10_and_64(int n, int expected)
    {
        var hole = WithPlan(n).WhiteHole;

        hole.GrainsPerFile.ShouldBe(expected);
        hole.GrainsPerFile.ShouldBeInRange(WhiteHoleScene.MinGrainsPerFile, WhiteHoleScene.MaxGrainsPerFile);
    }

    [Fact]
    public void One_ring_per_kind_in_display_order_with_the_radii_of_the_draft()
    {
        var hole = WithPlan(500).WhiteHole;

        hole.Kinds.ShouldBe(TargetPlanner.KindOrder);
        hole.RingCount.ShouldBe(5);
        // R0 = 36, dr = min(20, (150 - 70) / 4) = 20.
        Enumerable.Range(0, 5).Select(hole.RingRadius).ShouldBe([36f, 56f, 76f, 96f, 116f]);
        hole.Rings.Select(r => r.RadiusY).ShouldBe(hole.Rings.Select(r => r.RadiusX * WhiteHoleScene.Flatten));
        hole.Rings.ShouldAllBe(r => r.Count == 100 && r.Arrived == 0 && r.Fill == 0f && r.GlowAlpha == 0f);
        hole.RingScatter.ShouldBe(9f);

        // Inner rings turn faster: t·0.35·(36/rx)^1.2.
        hole.RingRotation(0, 1f).ShouldBe(0.35f, 1e-5f);
        hole.RingRotation(4, 1f).ShouldBeLessThan(hole.RingRotation(0, 1f));

        var single = WithPlan(200, kind: _ => MediaKind.Audio).WhiteHole;
        single.RingCount.ShouldBe(1);
        single.RingRadius(0).ShouldBe(62f);
        single.RingScatter.ShouldBe(26f);
    }

    [Fact]
    public void Arrivals_light_the_grains_of_their_ring_and_send_a_pulse()
    {
        var scene = WithPlan(200);
        var hole = scene.WhiteHole;

        // Files 1 and 6 are images (kind cycles by index): the image ring fills to 2/40.
        scene.Enqueue(new FinishFile(SceneTestTime.Id(1), FileOutcome.Completed, PixelTarget.WhiteHole));
        scene.Enqueue(new FinishFile(SceneTestTime.Id(6), FileOutcome.Completed, PixelTarget.WhiteHole));
        scene.Update(SceneTestData.Step);

        var image = hole.Rings[0];
        image.Kind.ShouldBe(MediaKind.Image);
        image.Arrived.ShouldBe(2);
        image.Fill.ShouldBe(2f / 40f, 1e-5f);
        image.GlowAlpha.ShouldBe(0.1f + 0.12f * image.Fill, 1e-5f);
        image.GlowWidth.ShouldBe(6f * image.Fill + 2f, 1e-5f);
        hole.Rings.Skip(1).ShouldAllBe(r => r.Arrived == 0 && r.GlowAlpha == 0f);

        // Grains: seeds of the ring times its fill; every grain in the image colour, within the ring's scatter.
        var expected = (int)MathF.Round(hole.RingSeeds(0) * image.Fill);
        hole.GrainCount.ShouldBe(expected);
        expected.ShouldBeGreaterThan(0);
        foreach (var grain in hole.Grains)
        {
            grain.Color.ShouldBe(SceneTestData.Palette.Image);
            var rx = grain.Offset.X;
            var ry = grain.Offset.Y / WhiteHoleScene.Flatten;
            MathF.Sqrt(rx * rx + ry * ry).ShouldBeInRange(image.RadiusX - hole.RingScatter / 2f - 0.01f, image.RadiusX + hole.RingScatter / 2f + 0.01f);
        }

        // Two arrival pulses for 0.8 s.
        hole.Pulses.Count.ShouldBe(2);
        hole.Pulses.ShouldAllBe(p => p.Color == SceneTestData.Palette.Image && p.Alpha > 0f);
        SceneTestTime.Run(scene, 0.8);
        hole.Pulses.ShouldBeEmpty();
        hole.Arrived.ShouldBe(2);
        hole.Planets.ShouldBeEmpty();
    }

    [Fact]
    public void A_failed_file_is_a_coral_ring_on_its_kind_ring_and_lights_no_grain()
    {
        var scene = WithPlan(150);
        scene.Enqueue(new FinishFile(SceneTestTime.Id(2), FileOutcome.Failed, PixelTarget.Inbox));
        scene.Update(SceneTestData.Step);

        var hole = scene.WhiteHole;
        hole.GrainCount.ShouldBe(0);
        hole.Arrived.ShouldBe(0);
        var marker = hole.Planets.ShouldHaveSingleItem();
        marker.Failed.ShouldBeTrue();
        marker.Color.ShouldBe(SceneTestData.Palette.Error);
        marker.OrbitRadius.ShouldBe(hole.RingRadius(hole.RingOf(MediaKind.Audio)));
        hole.Pulses.ShouldHaveSingleItem().Color.ShouldBe(SceneTestData.Palette.Error);
    }

    [Fact]
    public void The_pixel_target_of_a_slot_lies_on_the_ring_of_its_kind()
    {
        var scene = WithPlan(150);
        scene.Enqueue(new FinishFile(SceneTestTime.Id(3), FileOutcome.Completed, PixelTarget.WhiteHole));
        scene.Update(SceneTestData.Step);
        var hole = scene.WhiteHole;

        var slot = hole.SlotOf(SceneTestTime.Id(3));
        slot.ShouldBe(0);
        var ring = hole.RingOf(MediaKind.Video);
        var p = hole.SlotPosition(slot, 1f) - hole.Centre;
        var r = MathF.Sqrt(p.X * p.X + (p.Y / WhiteHoleScene.Flatten) * (p.Y / WhiteHoleScene.Flatten));
        r.ShouldBeInRange(hole.RingRadius(ring) - hole.RingScatter / 2f - 0.01f, hole.RingRadius(ring) + hole.RingScatter / 2f + 0.01f);

        // Deterministic: the same plan and time give the same picture.
        var again = WithPlan(150);
        again.Enqueue(new FinishFile(SceneTestTime.Id(3), FileOutcome.Completed, PixelTarget.WhiteHole));
        again.Update(SceneTestData.Step);
        again.WhiteHole.SlotPosition(0, 1f).ShouldBe(hole.SlotPosition(0, 1f));
        again.WhiteHole.Grains.ShouldBe(hole.Grains);
    }

    [Fact]
    public void The_particle_budget_holds_with_500_files_streams_and_finale()
    {
        var scene = WithPlan(500);
        var hole = scene.WhiteHole;
        var max = 0;

        // Two files circle (two pixel streams), the rest lands without a stream.
        scene.Enqueue(new BeginFile(SceneTestTime.Id(1)));
        scene.Enqueue(new BeginFile(SceneTestTime.Id(2)));
        scene.Update(SceneTestData.Step);
        for (var i = 3; i <= 500; i++)
        {
            scene.Enqueue(new FinishFile(SceneTestTime.Id(i), i % 50 == 0 ? FileOutcome.Failed : FileOutcome.Completed, PixelTarget.WhiteHole));
            if (i % 25 == 0)
            {
                scene.Update(SceneTestData.Step);
                max = Math.Max(max, scene.ParticleCount);
            }
        }

        scene.Enqueue(new FinishFile(SceneTestTime.Id(1), FileOutcome.Completed, PixelTarget.WhiteHole));
        scene.Enqueue(new FinishFile(SceneTestTime.Id(2), FileOutcome.Completed, PixelTarget.WhiteHole));
        SceneTestTime.Run(dt => { scene.Update(dt); max = Math.Max(max, scene.ParticleCount); }, 1.05);

        // All arrived: the rings hold exactly their seeds, never above the pool.
        var pool = scene.Budget.RingGrains;
        hole.GrainCount.ShouldBe(hole.Rings.Sum(r => (int)MathF.Round(hole.RingSeeds(r.Index) * r.Fill)));
        hole.GrainCount.ShouldBeLessThanOrEqualTo(pool);
        hole.GrainCount.ShouldBeGreaterThan(pool / 2);
        hole.Arrived.ShouldBe(490);
        hole.Planets.Count.ShouldBe(10);

        scene.Enqueue(new BeginFinale(490, 10));
        SceneTestTime.Run(dt => { scene.Update(dt); max = Math.Max(max, scene.ParticleCount); }, FinaleScene.Det + 2.0);

        max.ShouldBeLessThanOrEqualTo(scene.Budget.MaxParticles);
        max.ShouldBeGreaterThan(pool);
        output.WriteLine($"max particles {max}");
    }

    [Fact]
    public void The_finale_of_dust_mode_has_one_numbered_planet_per_kind()
    {
        var scene = WithPlan(500);
        FinishAll(scene, 500, failEvery: 50);
        SceneTestTime.Run(scene, 1.05);
        scene.Enqueue(new BeginFinale(490, 10));
        scene.Update(TimeSpan.Zero);
        var finale = scene.Finale.ShouldNotBeNull();

        finale.IsDust.ShouldBeTrue();
        finale.PlanetRadius.ShouldBe(4.5f);
        var planets = finale.Planets;
        planets.Count(p => !p.Failed).ShouldBe(5);
        planets.Where(p => !p.Failed).Sum(p => p.Count).ShouldBe(490);
        planets.Where(p => p.Failed).Sum(p => p.Count).ShouldBe(10);
        planets.Select(p => p.Kind).Distinct().Count().ShouldBe(5);

        // They fade in over 0.35 s and ride their ring, contracting with it.
        planets.ShouldAllBe(p => p.Alpha == 0f);
        SceneTestTime.Run(scene, 0.4);
        planets.ShouldAllBe(p => p.Alpha == 1f);
        var hole = scene.WhiteHole;
        foreach (var p in planets)
        {
            var offset = p.Target - finale.CurrentHoles.White;
            var r = MathF.Sqrt(offset.X * offset.X + (offset.Y / WhiteHoleScene.Flatten) * (offset.Y / WhiteHoleScene.Flatten));
            r.ShouldBe(hole.RingRadius(hole.RingOf(p.Kind)) * finale.OrbitScale, 0.05f);
        }

        // The rings are still drawn while the orbits fade; afterwards the grains are dropped.
        finale.OrbitAlpha.ShouldBeGreaterThan(0f);
        hole.GrainCount.ShouldBeGreaterThan(0);
        SceneTestTime.Run(scene, FinaleScene.Ph1 + 0.3);
        finale.OrbitAlpha.ShouldBe(0f);
        hole.GrainCount.ShouldBe(0);

        // On the end ring: sorted by kind, every planet on the ellipse, with its number.
        SceneTestTime.Run(scene, FinaleScene.Det + 1.7 - scene.Finale!.F);
        var byRing = planets.OrderBy(p => p.RingOrder).ToList();
        var ranks = byRing.Select(p => TargetPlanner.KindOrder.ToList().IndexOf(p.Kind)).ToList();
        ranks.ShouldBe(ranks.Order().ToList());
        var radii = finale.RingRadii;
        foreach (var p in planets)
        {
            p.RingAlpha.ShouldBe(1f);
            p.Count.ShouldBeGreaterThan(0);
            var d = p.RingPosition - finale.EndCentre;
            (d.X * d.X / (radii.X * radii.X) + d.Y * d.Y / (radii.Y * radii.Y)).ShouldBe(1f, 0.02f);
        }
    }

    [Fact]
    public void Sixty_seconds_of_finale_with_500_files_compute_in_under_two_seconds()
    {
        var scene = WithPlan(500);
        FinishAll(scene, 500, failEvery: 50);
        SceneTestTime.Run(scene, 1.05);
        scene.Enqueue(new BeginFinale(490, 10));

        var watch = Stopwatch.StartNew();
        SceneTestTime.Run(scene, 60, TimeSpan.FromSeconds(1d / 60d));
        watch.Stop();

        output.WriteLine($"60 s of finale with 500 files: {watch.ElapsedMilliseconds} ms");
        scene.Finale.ShouldNotBeNull().Phase.ShouldBe(FinalePhase.Done);
        watch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void Below_150_files_nothing_changes_for_the_orbits()
    {
        var scene = WithPlan(149);
        FinishAll(scene, 149, failEvery: 40);
        var hole = scene.WhiteHole;

        hole.Mode.ShouldBe(WhiteHoleMode.Rings);
        hole.Planets.Count.ShouldBe(149);
        hole.GrainCount.ShouldBe(0);
        hole.Rings.ShouldBeEmpty();
        hole.CounterY.ShouldBe(hole.Centre.Y + hole.OrbitRadius(9) * WhiteHoleScene.Flatten + 22f);
        scene.Enqueue(new BeginFinale(146, 3));
        scene.Update(TimeSpan.Zero);
        scene.Finale.ShouldNotBeNull().IsDust.ShouldBeFalse();
        scene.Finale!.Planets.Count.ShouldBe(149);
        scene.Finale!.Planets.ShouldAllBe(p => p.Count == 0);
    }
}
