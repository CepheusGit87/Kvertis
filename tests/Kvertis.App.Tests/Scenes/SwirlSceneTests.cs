using System.Numerics;
using Kvertis.App.Scenes;
using Kvertis.Engine.Abstractions;
using Shouldly;
using Xunit;

namespace Kvertis.App.Tests.Scenes;

public class SwirlSceneTests
{
    private static SwirlScene WithPlan(int files, SwirlBudget? budget = null, Func<int, bool>? ownLocation = null)
    {
        var scene = SceneTestTime.NewSwirl(budget: budget);
        scene.Enqueue(new SetPlan(SceneTestTime.Files(files, ownLocation)));
        scene.Update(TimeSpan.Zero);
        return scene;
    }

    private static SwirlFile File(SwirlScene scene, int n) => scene.Files.Single(f => f.Id == SceneTestTime.Id(n));

    [Fact]
    public void Two_files_swirl_at_the_same_time_and_a_third_gets_no_slot()
    {
        var scene = WithPlan(5);
        scene.Enqueue(new BeginFile(SceneTestTime.Id(1)));
        scene.Enqueue(new BeginFile(SceneTestTime.Id(2)));
        scene.Enqueue(new BeginFile(SceneTestTime.Id(3)));
        SceneTestTime.Run(scene, 0.5);

        scene.Snapshot.FilesInSwirl.ShouldBe(2);
        File(scene, 1).HasPixels.ShouldBeTrue();
        File(scene, 2).HasPixels.ShouldBeTrue();
        File(scene, 3).State.ShouldBe(SwirlFileState.RunningOffSwirl);
        File(scene, 3).Pixels.ShouldBeEmpty();
        scene.StackSheets.Select(s => s.Spec.Id).ShouldContain(SceneTestTime.Id(3));
        scene.IsRunning.ShouldBeTrue();
    }

    [Fact]
    public void After_the_fly_in_every_pixel_sits_on_its_swirl_position()
    {
        var scene = WithPlan(3);
        scene.Enqueue(new BeginFile(SceneTestTime.Id(1)));
        scene.Update(TimeSpan.Zero);

        SceneTestTime.Run(scene, SwirlScene.Flight.TotalSeconds);

        var file = File(scene, 1);
        file.EnterShare.ShouldBe(1f);
        file.Pixels.Length.ShouldBe(file.Cells.Length);
        for (var i = 0; i < file.Pixels.Length; i++)
        {
            Vector2.Distance(file.Pixels[i].Position, scene.SwirlPosition(file.Cells[i], (float)SwirlScene.Flight.TotalSeconds)).ShouldBeLessThan(1e-3f);
        }

        file.TintShare.ShouldBe(0.95f, 1e-5f);
        file.Pixels.ShouldAllBe(p => p.Alpha >= 0.35f);
    }

    [Fact]
    public void Pixel_radii_and_angular_speeds_stay_in_their_bounds()
    {
        var scene = WithPlan(2);
        scene.Enqueue(new BeginFile(SceneTestTime.Id(1)));
        scene.Enqueue(new BeginFile(SceneTestTime.Id(2)));
        scene.Update(TimeSpan.Zero);

        foreach (var cell in scene.Files.SelectMany(f => f.Cells))
        {
            var speed = SwirlScene.AngularSpeed(cell.R2);
            speed.ShouldBeInRange(0.5f, 2.1f);
            SwirlScene.OrbitRadiusOf(cell.R2).ShouldBeInRange(14f, 114f);

            // 16° per frame at 60 fps is far away: at most 2.1 rad/s = 2.0° per frame.
            (speed / 60f * 180f / MathF.PI).ShouldBeLessThan(16f);
        }

        SwirlScene.AngularSpeed(0f).ShouldBe(2.1f, 1e-6f);
        SwirlScene.AngularSpeed(1f).ShouldBe(0.5f, 1e-6f);
    }

    [Fact]
    public void Progress_colours_the_pixels_and_a_completed_file_lands_on_the_white_hole()
    {
        var scene = WithPlan(3);
        scene.Enqueue(new BeginFile(SceneTestTime.Id(1)));
        SceneTestTime.Run(scene, 1.0);

        scene.Enqueue(new SetProgress(SceneTestTime.Id(1), 0.5f));
        scene.Update(SceneTestData.Step);
        File(scene, 1).ColourMix.ShouldBe(0.35f, 1e-5f);

        for (var p = 0.6f; p <= 1.0001f; p += 0.1f)
        {
            scene.Enqueue(new SetProgress(SceneTestTime.Id(1), p));
            SceneTestTime.Run(scene, 0.2);
        }

        scene.Enqueue(new FinishFile(SceneTestTime.Id(1), FileOutcome.Completed, PixelTarget.WhiteHole));
        scene.Update(TimeSpan.Zero);
        var file = File(scene, 1);
        file.State.ShouldBe(SwirlFileState.Leaving);
        file.Slot.ShouldBe(0);

        SceneTestTime.Run(scene, SwirlScene.Flight.TotalSeconds);

        file.LeaveShare.ShouldBe(1f);
        var slot = scene.WhiteHole.SlotPosition(0, (float)scene.Time.TotalSeconds);
        file.Pixels.ShouldAllBe(p => Vector2.Distance(p.Position, slot) < 6f);
        file.ColourMix.ShouldBe(1f);

        scene.Update(SceneTestData.Step);
        file.State.ShouldBe(SwirlFileState.Landed);
        file.Pixels.ShouldBeEmpty();
        scene.WhiteHole.Planets.Count.ShouldBe(1);
        scene.Snapshot.Landed.ShouldBe(1);
    }

    [Fact]
    public void A_failed_file_flies_back_to_the_stack_and_is_a_coral_ring_on_the_white_hole()
    {
        var scene = WithPlan(3);
        scene.Enqueue(new BeginFile(SceneTestTime.Id(2)));
        SceneTestTime.Run(scene, 1.2);
        scene.Enqueue(new FinishFile(SceneTestTime.Id(2), FileOutcome.Failed, PixelTarget.Inbox));
        scene.Update(TimeSpan.Zero);
        SceneTestTime.Run(scene, SwirlScene.Flight.TotalSeconds);

        var file = File(scene, 2);
        var stack = scene.StackPlacement(file);
        for (var i = 0; i < file.Pixels.Length; i++)
        {
            Vector2.Distance(file.Pixels[i].Position, stack.PointOf(file.Cells[i])).ShouldBeLessThan(1e-3f);
        }

        scene.Update(SceneTestData.Step);
        file.State.ShouldBe(SwirlFileState.BackOnStack);
        scene.StackSheets.ShouldContain(s => s.Spec.Id == file.Id);
        var planet = scene.WhiteHole.Planets.Single();
        planet.Failed.ShouldBeTrue();
        planet.Color.ShouldBe(SceneTestData.Palette.Error);
    }

    [Fact]
    public void Files_with_an_own_target_fly_into_the_bag_and_take_no_place()
    {
        var scene = WithPlan(3, ownLocation: i => i == 0);
        scene.WhiteHole.N.ShouldBe(2);
        scene.Enqueue(new SetBagAnchor(new Vector2(900f, 380f)));
        scene.Enqueue(new BeginFile(SceneTestTime.Id(1)));
        SceneTestTime.Run(scene, 1.0);
        scene.Enqueue(new FinishFile(SceneTestTime.Id(1), FileOutcome.Completed, PixelTarget.Bag));
        scene.Update(TimeSpan.Zero);
        SceneTestTime.Run(scene, SwirlScene.Flight.TotalSeconds);

        File(scene, 1).Pixels.ShouldAllBe(p => Vector2.Distance(p.Position, new Vector2(900f, 380f)) < 10f);
        scene.WhiteHole.Slots.ShouldBeEmpty();
    }

    [Fact]
    public void The_budget_is_never_exceeded_during_a_whole_round()
    {
        var budget = new SwirlBudget();
        var scene = WithPlan(30, budget);
        var next = 1;
        var running = new List<int>();

        void Check()
        {
            scene.Snapshot.FilesInSwirl.ShouldBeLessThanOrEqualTo(budget.MaxSwirlFiles);
            scene.ParticleCount.ShouldBeLessThanOrEqualTo(budget.MaxParticles);
            scene.StackSheets.Count.ShouldBeLessThanOrEqualTo(budget.MaxStackSheets);
            scene.WhiteHole.EmptyPlaces.Count.ShouldBeLessThanOrEqualTo(budget.MaxEmptyPlaces);
            scene.WhiteHole.Stars.Count.ShouldBeLessThanOrEqualTo(budget.StarsNear);
            scene.Files.Where(f => f.State == SwirlFileState.InSwirl).Sum(f => f.Pixels.Length).ShouldBeLessThanOrEqualTo(budget.MaxSwirlFiles * 744);
        }

        for (var round = 0; round < 40; round++)
        {
            // Three starts per tick: the scene takes at most two into the swirl.
            while (running.Count < 3 && next <= 30)
            {
                scene.Enqueue(new BeginFile(SceneTestTime.Id(next)));
                running.Add(next++);
            }

            SceneTestTime.Run(dt => { scene.Update(dt); Check(); }, 0.4);
            if (running.Count > 0)
            {
                var done = running[0];
                running.RemoveAt(0);
                var outcome = done % 7 == 0 ? FileOutcome.Failed : FileOutcome.Completed;
                scene.Enqueue(new FinishFile(SceneTestTime.Id(done), outcome, outcome == FileOutcome.Failed ? PixelTarget.Inbox : PixelTarget.WhiteHole));
            }
        }

        SceneTestTime.Run(dt => { scene.Update(dt); Check(); }, 2.0);
        scene.WhiteHole.Slots.Count.ShouldBe(30);
        scene.Files.ShouldAllBe(f => !f.HasPixels);
    }

    [Fact]
    public void A_small_particle_budget_cuts_the_pixels_of_a_file()
    {
        var scene = WithPlan(2, new SwirlBudget(MaxParticles: 1000));
        scene.Enqueue(new BeginFile(SceneTestTime.Id(1)));
        scene.Enqueue(new BeginFile(SceneTestTime.Id(2)));
        scene.Update(SceneTestData.Step);

        scene.ParticleCount.ShouldBe(1000);
        File(scene, 2).Pixels.Length.ShouldBeLessThan(File(scene, 1).Pixels.Length);
    }

    [Fact]
    public void Stack_places_follow_the_design_and_never_leave_the_surface()
    {
        var place = SwirlScene.StackSlot(0, 3, 200f);
        place.TopLeft.ShouldBe(new Vector2(48f, 200f - 70f - 10f));
        place.Scale.ShouldBe(0.5f);
        place.Rotation.ShouldBe(0.04f, 1e-6f);
        place.Size.ShouldBe(new Vector2(48f, 62f));

        var top = SwirlScene.StackSlot(0, 60, 200f);
        top.TopLeft.Y.ShouldBe(200f - 70f - (5f * 23f));
        SwirlScene.StackSlot(59, 60, 200f).ShouldBe(SwirlScene.StackSlot(23, 60, 200f));
    }

    [Fact]
    public void Update_clamps_a_long_step()
    {
        var scene = SceneTestTime.NewSwirl();
        scene.Update(TimeSpan.FromSeconds(4));
        scene.Time.ShouldBe(SwirlScene.MaxStep);
    }

    [Fact]
    public void Two_scenes_with_the_same_seed_and_commands_stay_identical()
    {
        SwirlScene Run()
        {
            var scene = WithPlan(9);
            scene.Enqueue(new BeginFile(SceneTestTime.Id(1)));
            scene.Enqueue(new BeginFile(SceneTestTime.Id(2)));
            SceneTestTime.Run(scene, 1.3);
            scene.Enqueue(new SetProgress(SceneTestTime.Id(1), 0.4f));
            scene.Enqueue(new FinishFile(SceneTestTime.Id(2), FileOutcome.Completed, PixelTarget.WhiteHole));
            scene.Enqueue(new BeginFile(SceneTestTime.Id(3)));
            SceneTestTime.Run(scene, 1.7);
            foreach (var n in new[] { 1, 3, 4, 5, 6, 7, 8, 9 })
            {
                scene.Enqueue(new FinishFile(SceneTestTime.Id(n), n == 5 ? FileOutcome.Failed : FileOutcome.Completed, PixelTarget.WhiteHole));
            }

            SceneTestTime.Run(scene, 1.0);
            scene.Enqueue(new BeginFinale(8, 1));
            SceneTestTime.Run(scene, 4.0);
            return scene;
        }

        var a = Run();
        var b = Run();

        a.Time.ShouldBe(TimeSpan.FromSeconds(8));
        a.Snapshot.ShouldBe(b.Snapshot);
        a.Finale.ShouldNotBeNull().Planets.Select(p => p.Position).ShouldBe(b.Finale.ShouldNotBeNull().Planets.Select(p => p.Position));
        a.Finale.Dust.ShouldBe(b.Finale.Dust);
        a.WhiteHole.Stars.ShouldBe(b.WhiteHole.Stars);
    }

    [Fact]
    public void A_snapshot_never_changes_after_it_was_published()
    {
        var scene = WithPlan(3);
        scene.Enqueue(new BeginFile(SceneTestTime.Id(1)));
        SceneTestTime.Run(scene, 0.5);
        var before = scene.Snapshot;
        var copy = before with { };

        scene.Enqueue(new FinishFile(SceneTestTime.Id(1), FileOutcome.Completed, PixelTarget.WhiteHole));
        SceneTestTime.Run(scene, 2.0);

        before.ShouldBe(copy);
        scene.Snapshot.ShouldNotBeSameAs(before);
        scene.Snapshot.Landed.ShouldBe(1);
        before.Landed.ShouldBe(0);
        typeof(SwirlSnapshot).GetProperties().ShouldAllBe(p => p.SetMethod == null || !p.SetMethod.IsPublic || p.SetMethod.ReturnParameter.GetRequiredCustomModifiers().Length > 0);
    }

    [Fact]
    public void Clear_drops_files_white_hole_and_finale()
    {
        var scene = WithPlan(2);
        scene.Enqueue(new FinishFile(SceneTestTime.Id(1), FileOutcome.Completed, PixelTarget.WhiteHole));
        scene.Enqueue(new BeginFinale(1, 0));
        SceneTestTime.Run(scene, 0.5);
        scene.Finale.ShouldNotBeNull();

        scene.Enqueue(new SwirlClear());
        scene.Update(SceneTestData.Step);

        scene.Files.ShouldBeEmpty();
        scene.Finale.ShouldBeNull();
        scene.WhiteHole.N.ShouldBe(0);
        scene.Snapshot.FinalePhase.ShouldBeNull();
    }
}
