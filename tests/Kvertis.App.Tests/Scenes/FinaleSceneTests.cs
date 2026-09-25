using System.Diagnostics;
using System.Numerics;
using Kvertis.App.Scenes;
using Kvertis.App.Services;
using Kvertis.Engine.Abstractions;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace Kvertis.App.Tests.Scenes;

public class FinaleSceneTests(ITestOutputHelper output)
{
    /// <summary>A finished round of <paramref name="files"/> files (file 3 failed) with a running finale.</summary>
    private static SwirlScene Finished(int files = 7, int seed = 4711)
    {
        var scene = SceneTestTime.NewSwirl(seed: seed);
        scene.Enqueue(new SetPlan(SceneTestTime.Files(files)));
        var failed = 0;
        for (var i = files; i >= 1; i--)
        {
            var fail = i == 3;
            failed += fail ? 1 : 0;
            scene.Enqueue(new FinishFile(SceneTestTime.Id(i), fail ? FileOutcome.Failed : FileOutcome.Completed, fail ? PixelTarget.Inbox : PixelTarget.WhiteHole));
        }

        SceneTestTime.Run(scene, 1.05);
        scene.Enqueue(new BeginFinale(files - failed, failed));
        scene.Update(TimeSpan.Zero);
        return scene;
    }

    [Fact]
    public void Constants_follow_the_worksheet()
    {
        FinaleScene.Anl.ShouldBe(1.9f, 1e-6f);
        FinaleScene.Det.ShouldBe(4.58f, 1e-5f);
        FinaleScene.MaxAngularSpeed.ShouldBe(16.755f, 1e-3f);
        FinaleScene.Rounds.ShouldBe(4);
    }

    [Fact]
    public void Angular_speed_of_the_holes_never_exceeds_16_degrees_per_frame()
    {
        var finale = Finished().Finale.ShouldNotBeNull();
        finale.InitialAngularSpeed.ShouldBeLessThanOrEqualTo(FinaleScene.MaxAngularSpeed);

        const float h = 1f / 240f;
        var max = 0f;
        for (var f = 0f; f < FinaleScene.Anl + FinaleScene.M; f += h)
        {
            var speed = MathF.Abs(finale.HoleAngle(f + h) - finale.HoleAngle(f)) / h;
            max = MathF.Max(max, speed);
        }

        max.ShouldBeLessThanOrEqualTo(FinaleScene.MaxAngularSpeed * 1.001f);

        // The cap is actually reached shortly before the merger.
        max.ShouldBeGreaterThan(FinaleScene.MaxAngularSpeed * 0.99f);
    }

    [Fact]
    public void Distance_falls_monotonically_to_zero_at_the_merger()
    {
        var finale = Finished().Finale.ShouldNotBeNull();
        var last = float.MaxValue;
        for (var fd = 0f; fd <= FinaleScene.M; fd += 0.01f)
        {
            var d = finale.DistanceAt(fd);
            d.ShouldBeLessThanOrEqualTo(last + 1e-4f);
            last = d;
        }

        finale.DistanceAt(0f).ShouldBe(finale.InitialDistance, 1e-3f);
        finale.DistanceAt(FinaleScene.M).ShouldBe(0f, 1e-3f);
    }

    [Fact]
    public void Both_holes_mirror_each_other_around_the_moving_centre()
    {
        var finale = Finished().Finale.ShouldNotBeNull();
        for (var f = 0f; f <= FinaleScene.Anl + FinaleScene.M; f += 0.05f)
        {
            var (black, white, _) = finale.Holes(f);
            var m = finale.CentreAt(f);
            Vector2.Distance((black + white) / 2f, m).ShouldBeLessThan(0.01f);
        }

        var start = finale.Holes(0f);
        Vector2.Distance(start.Black, finale.BlackStart).ShouldBeLessThan(1e-3f);
        Vector2.Distance(start.White, finale.WhiteStart).ShouldBeLessThan(1e-3f);
        Vector2.Distance(finale.CentreAt(FinaleScene.Anl + 1.2f), finale.EndCentre).ShouldBeLessThan(1e-3f);
        finale.EndCentre.ShouldBe(new Vector2(500f, 150f));

        var merged = finale.Holes(FinaleScene.Anl + FinaleScene.M);
        Vector2.Distance(merged.Black, merged.White).ShouldBeLessThan(0.01f);
        merged.Near.ShouldBe(1f);
    }

    [Fact]
    public void Phases_run_in_order_with_dance_at_19_and_nova_at_458_seconds()
    {
        FinaleScene.PhaseAt(1.89f).ShouldBe(FinalePhase.RunUp);
        FinaleScene.PhaseAt(1.91f).ShouldBe(FinalePhase.Dance);
        FinaleScene.PhaseAt(4.29f).ShouldBe(FinalePhase.Dance);
        FinaleScene.PhaseAt(4.31f).ShouldBe(FinalePhase.Silence);
        FinaleScene.PhaseAt(4.57f).ShouldBe(FinalePhase.Silence);
        FinaleScene.PhaseAt(4.59f).ShouldBe(FinalePhase.Nova);
        FinaleScene.PhaseAt(4.58f + 1.31f).ShouldBe(FinalePhase.Ring);
        FinaleScene.PhaseAt(4.58f + 1.81f).ShouldBe(FinalePhase.Done);

        var scene = Finished();
        var seen = new List<FinalePhase> { scene.Finale!.Phase };
        var shakes = 0;
        var reportFrom = float.NaN;
        SceneTestTime.Run(
            dt =>
            {
                scene.Update(dt);
                var finale = scene.Finale!;
                if (seen[^1] != finale.Phase)
                {
                    seen.Add(finale.Phase);
                }

                if (scene.Snapshot.ShakeRequested)
                {
                    shakes++;
                    finale.E.ShouldBeInRange(0f, 0.03f);
                }

                if (float.IsNaN(reportFrom) && scene.Snapshot.ShowReport)
                {
                    reportFrom = finale.E;
                }
            },
            8.0);

        seen.ShouldBe([FinalePhase.RunUp, FinalePhase.Dance, FinalePhase.Silence, FinalePhase.Nova, FinalePhase.Ring, FinalePhase.Done]);
        shakes.ShouldBe(1);
        scene.Snapshot.ShakeCount.ShouldBe(1);
        reportFrom.ShouldBeInRange(1.2f, 1.23f);
    }

    [Fact]
    public void Inertia_never_pulls_a_planet_more_than_28_dip_off_its_place()
    {
        var scene = Finished(25);
        var finale = scene.Finale.ShouldNotBeNull();
        var maxDeviation = 0f;
        SceneTestTime.Run(
            dt =>
            {
                scene.Update(dt);
                foreach (var p in finale.Planets)
                {
                    maxDeviation = MathF.Max(maxDeviation, p.Deviation);
                }
            },
            FinaleScene.Anl + FinaleScene.M,
            TimeSpan.FromSeconds(1d / 60d));

        maxDeviation.ShouldBeLessThan(FinaleScene.MaxDeviation);
        maxDeviation.ShouldBeGreaterThan(1f);
        foreach (var p in finale.Planets)
        {
            p.Trail.Length.ShouldBe(FinalePlanet.TrailCapacity);
        }
    }

    [Fact]
    public void Ring_places_are_sorted_by_kind_and_the_planets_end_on_the_ellipse()
    {
        var scene = Finished(12);
        SceneTestTime.Run(scene, FinaleScene.Det + 1.7);
        var finale = scene.Finale.ShouldNotBeNull();

        var byRing = finale.Planets.OrderBy(p => p.RingOrder).ToList();
        var ranks = byRing.Select(p => TargetPlanner.KindOrder.ToList().IndexOf(p.Kind)).ToList();
        ranks.ShouldBe(ranks.Order().ToList());
        for (var o = 1; o < byRing.Count; o++)
        {
            if (byRing[o].Kind == byRing[o - 1].Kind)
            {
                byRing[o].Slot.ShouldBeGreaterThan(byRing[o - 1].Slot);
            }
        }

        var c = finale.EndCentre;
        var radii = finale.RingRadii;
        radii.ShouldBe(new Vector2(440f, 116f));
        foreach (var p in finale.Planets)
        {
            var q = p.RingPosition - c;
            var angle = MathF.Atan2(q.Y / radii.Y, q.X / radii.X);
            var onEllipse = c + new Vector2(MathF.Cos(angle) * radii.X, MathF.Sin(angle) * radii.Y);
            Vector2.Distance(p.RingPosition, onEllipse).ShouldBeLessThan(1f);
            p.RingAlpha.ShouldBe(1f);
        }

        byRing.ShouldContain(p => p.Failed);
        byRing.Single(p => p.Failed).Color.ShouldBe(SceneTestData.Palette.Error);
    }

    [Fact]
    public void Complete_jumps_to_the_end_without_a_shake()
    {
        var scene = Finished();
        SceneTestTime.Run(scene, 1.0);
        var finale = scene.Finale.ShouldNotBeNull();

        finale.Complete();
        scene.Update(SceneTestData.Step);

        finale.Phase.ShouldBe(FinalePhase.Done);
        finale.ShowReport.ShouldBeTrue();
        finale.ShakeCount.ShouldBe(0);
        scene.Snapshot.ShowReport.ShouldBeTrue();
        finale.ShakeOffset.ShouldBe(Vector2.Zero);
        finale.Sparks.ShouldBeEmpty();
    }

    [Fact]
    public void A_resize_during_the_finale_completes_it()
    {
        var scene = Finished();
        SceneTestTime.Run(scene, 2.0);
        scene.Enqueue(new SwirlResize(800f, 400f));
        scene.Update(SceneTestData.Step);
        scene.Finale!.IsCompleted.ShouldBeTrue();
    }

    [Fact]
    public void The_particle_budget_holds_during_the_explosion()
    {
        var scene = Finished(25);
        var max = 0;
        SceneTestTime.Run(dt => { scene.Update(dt); max = Math.Max(max, scene.ParticleCount); }, 8.0);
        max.ShouldBeLessThanOrEqualTo(scene.Budget.MaxParticles);
        max.ShouldBeGreaterThan(300);
    }

    [Fact]
    public void Sixty_seconds_of_finale_compute_in_under_two_seconds()
    {
        var scene = Finished(50);
        var watch = Stopwatch.StartNew();
        SceneTestTime.Run(scene, 60.0, TimeSpan.FromSeconds(1d / 60d));
        watch.Stop();

        output.WriteLine($"60 s finale with 50 planets: {watch.Elapsed.TotalMilliseconds:F0} ms");
        scene.Finale!.Phase.ShouldBe(FinalePhase.Done);
        watch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(2));
    }
}
