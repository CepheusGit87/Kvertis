using Kvertis.App.Scenes;
using Kvertis.Engine.Abstractions;
using Shouldly;
using Xunit;

namespace Kvertis.App.Tests.Scenes;

public class WhiteHoleSceneTests
{
    private static SwirlScene WithPlan(int files, float width = 1000f)
    {
        var scene = SceneTestTime.NewSwirl(width: width);
        scene.Enqueue(new SetPlan(SceneTestTime.Files(files)));
        scene.Update(TimeSpan.Zero);
        return scene;
    }

    [Theory]
    [InlineData(1, 1, new[] { 1 })]
    [InlineData(7, 7, new[] { 1, 1, 1, 1, 1, 1, 1 })]
    [InlineData(10, 10, new[] { 1, 1, 1, 1, 1, 1, 1, 1, 1, 1 })]
    [InlineData(11, 10, new[] { 2, 1, 1, 1, 1, 1, 1, 1, 1, 1 })]
    [InlineData(25, 10, new[] { 3, 3, 3, 3, 3, 2, 2, 2, 2, 2 })]
    [InlineData(50, 10, new[] { 5, 5, 5, 5, 5, 5, 5, 5, 5, 5 })]
    public void Orbits_and_capacities_bundle_the_places(int n, int orbits, int[] capacities)
    {
        var hole = WithPlan(n).WhiteHole;

        hole.N.ShouldBe(n);
        hole.Orbits.ShouldBe(orbits);
        Enumerable.Range(0, orbits).Select(hole.Capacity).ShouldBe(capacities);
        capacities.Sum().ShouldBe(n);
        hole.PlanetRadius.ShouldBe(n <= 10 ? 2.8f : 2f);
    }

    [Fact]
    public void Orbit_radii_grow_by_12_with_a_300_dip_wide_hole()
    {
        var hole = WithPlan(25, width: 1000f).WhiteHole;

        hole.TotalWidth.ShouldBe(300f);
        hole.Centre.ShouldBe(new System.Numerics.Vector2(1000f - 22f - 150f, 100f));
        Enumerable.Range(0, 10).Select(hole.OrbitRadius).ShouldBe([24f, 36f, 48f, 60f, 72f, 84f, 96f, 108f, 120f, 132f]);

        // Inner orbit: 0.9 rad/s.
        (hole.Angle(0, 1f) - hole.Angle(0, 0f)).ShouldBe(0.9f, 1e-5f);
    }

    [Fact]
    public void Places_are_given_in_the_order_of_finishing_and_a_failure_is_marked()
    {
        var scene = WithPlan(7);
        scene.Enqueue(new FinishFile(SceneTestTime.Id(4), FileOutcome.Completed, PixelTarget.WhiteHole));
        scene.Enqueue(new FinishFile(SceneTestTime.Id(2), FileOutcome.Failed, PixelTarget.Inbox));
        scene.Enqueue(new FinishFile(SceneTestTime.Id(6), FileOutcome.Cancelled, PixelTarget.Inbox));
        scene.Enqueue(new FinishFile(SceneTestTime.Id(1), FileOutcome.Completed, PixelTarget.WhiteHole));
        scene.Update(SceneTestData.Step);

        var hole = scene.WhiteHole;
        hole.Slots.Select(s => s.Id).ShouldBe([SceneTestTime.Id(4), SceneTestTime.Id(2), SceneTestTime.Id(1)]);
        hole.Slots[1].Failed.ShouldBeTrue();

        var failed = hole.Planets.Single(p => p.Slot == 1);
        failed.Failed.ShouldBeTrue();
        failed.Color.ShouldBe(SceneTestData.Palette.Error);
        hole.Planets.Where(p => p.Slot != 1).ShouldAllBe(p => !p.Failed);
        hole.Share.ShouldBe(2f / 7f, 1e-5f);

        // Arrival ring for 0.8 s.
        hole.Planets.ShouldAllBe(p => p.ArrivalAlpha > 0f);
        SceneTestTime.Run(scene, 0.8);
        hole.Planets.ShouldAllBe(p => p.ArrivalAlpha == 0f);
    }

    [Fact]
    public void Up_to_ten_files_orbits_take_the_planet_colour_beyond_they_are_mint()
    {
        var few = WithPlan(3);
        few.Enqueue(new FinishFile(SceneTestTime.Id(1), FileOutcome.Completed, PixelTarget.WhiteHole));
        few.Update(SceneTestData.Step);
        few.WhiteHole.OrbitLines[0].Color.ShouldBe(SceneTestData.Palette.Image);
        few.WhiteHole.OrbitLines[0].Alpha.ShouldBe(0.38f);
        few.WhiteHole.OrbitLines[1].Dashed.ShouldBeTrue();
        few.WhiteHole.OrbitLines[1].Color.ShouldBe(SceneTestData.Palette.LineStrong);
        few.WhiteHole.EmptyPlaces.ShouldBeEmpty();

        var many = WithPlan(25);
        many.Enqueue(new FinishFile(SceneTestTime.Id(1), FileOutcome.Completed, PixelTarget.WhiteHole));
        many.Update(SceneTestData.Step);
        many.WhiteHole.OrbitLines[0].Color.ShouldBe(SceneTestData.Palette.Mint);
        many.WhiteHole.OrbitLines[0].Alpha.ShouldBe(0.12f + (0.25f / 3f), 1e-5f);
        many.WhiteHole.EmptyPlaces.Count.ShouldBe(24);
    }

    [Fact]
    public void Empty_places_are_capped_at_150()
    {
        var scene = WithPlan(400);
        scene.WhiteHole.EmptyPlaces.Count.ShouldBe(150);
        scene.WhiteHole.Stars.Count.ShouldBe(70);
    }

    [Fact]
    public void Files_of_other_kinds_keep_their_colour()
    {
        var scene = SceneTestTime.NewSwirl();
        scene.Enqueue(new SetPlan(SceneTestTime.Files(2, kind: i => i == 0 ? MediaKind.Model3D : MediaKind.Video)));
        scene.Enqueue(new FinishFile(SceneTestTime.Id(1), FileOutcome.Completed, PixelTarget.WhiteHole));
        scene.Enqueue(new FinishFile(SceneTestTime.Id(2), FileOutcome.Completed, PixelTarget.WhiteHole));
        scene.Update(SceneTestData.Step);

        scene.WhiteHole.Planets.Select(p => p.Color).ShouldBe([SceneTestData.Palette.Model3D, SceneTestData.Palette.Video]);
    }
}
