using System.Numerics;
using Kvertis.App.Scenes;
using Kvertis.Engine.Abstractions;
using Shouldly;
using Xunit;

namespace Kvertis.App.Tests.Scenes;

/// <summary>The drawn middle of step 2 without Win2D: layout, opening of the orbit, planets and the bending ways.</summary>
public class TargetPathsSceneTests
{
    private static TargetPathsScene NewScene(float width = 600f, float height = 500f) =>
        new(new TargetPathsLayout(width, height), SceneTestData.Palette);

    private static void Advance(TargetPathsScene scene, double seconds) => SceneTestTime.Run(scene.Update, seconds);

    private static ShowKind Images(params (string Id, long Bytes)[] files) =>
        new(MediaKind.Image, files.Select(f => new TargetPlanet(f.Id, f.Bytes)).ToList());

    private static SetTargetAnchors Anchors(string target, string? recommended = "jpg", params (string Id, string? Target)[] files) =>
        new(
            files.Select((f, i) => new TargetFileAnchor(f.Id, new Vector2(0f, 100f + i * 40f), f.Target, false)).ToList(),
            [new TargetFormatAnchor("jpg", new Vector2(600f, 80f)), new TargetFormatAnchor("png", new Vector2(600f, 130f)), new TargetFormatAnchor("webp", new Vector2(600f, 180f))],
            recommended);

    [Fact]
    public void Layout_puts_the_hole_in_the_middle_at_42_percent_and_caps_the_orbit()
    {
        var layout = new TargetPathsLayout(600f, 500f);

        layout.Hole.ShouldBe(new Vector2(300f, 210f));
        layout.OrbitRadiusX.ShouldBe(200f);
        layout.OrbitRadiusY.ShouldBe(84f);
        TargetPathsLayout.HoleRadius.ShouldBe(12f);

        new TargetPathsLayout(300f, 500f).OrbitRadiusX.ShouldBe(114f);
        new TargetPathsLayout(100f, 500f).OrbitRadiusX.ShouldBe(TargetPathsLayout.MinOrbitRadius);
    }

    [Fact]
    public void Update_clamps_a_long_step_so_nothing_jumps_after_a_pause()
    {
        var scene = NewScene();

        scene.Update(TimeSpan.FromSeconds(5));

        scene.Time.ShouldBe(TargetPathsScene.MaxStep);
    }

    [Fact]
    public void Without_a_kind_only_the_hole_exists()
    {
        var scene = NewScene();

        Advance(scene, 1.0);

        scene.Kind.ShouldBeNull();
        scene.Reveal.ShouldBe(0f);
        scene.Planets.ShouldBeEmpty();
        scene.Hole.ShouldBe(new Vector2(300f, 210f));
    }

    [Fact]
    public void Showing_a_kind_opens_the_orbit_over_0_9_seconds_from_a_quarter()
    {
        var scene = NewScene();
        scene.Enqueue(Images(("a.png", 1_000_000)));

        scene.Update(TimeSpan.Zero);
        scene.Reveal.ShouldBe(0f);
        scene.OrbitRadiusX.ShouldBe(50f, 0.01f);

        Advance(scene, 0.45);
        scene.Reveal.ShouldBe(GalaxyMotion.Ease(0.5f), 0.001f);

        Advance(scene, 0.5);
        scene.Reveal.ShouldBe(1f);
        scene.OrbitRadiusX.ShouldBe(200f, 0.01f);
        scene.Kind.ShouldBe(MediaKind.Image);
        scene.KindColor.ShouldBe(SceneTestData.Palette.Image);
    }

    [Fact]
    public void Planets_sit_on_the_orbit_and_turn_slowly()
    {
        var scene = NewScene();
        scene.Enqueue(Images(("a.png", 4_194_304), ("b.png", 1_048_576)));
        Advance(scene, 1.0);

        scene.Planets.Count.ShouldBe(2);
        var first = scene.Planets[0];
        Vector2.Distance(first.Position, scene.OrbitPoint(scene.PlanetAngle(0))).ShouldBeLessThan(0.01f);
        // 5 + sqrt(4 MB) * 0.6 = 6.2 at scale 1 (600 x 500 gives scale 1).
        first.Radius.ShouldBe(6.2f, 0.01f);
        scene.Planets[1].Radius.ShouldBe(5.6f, 0.01f);

        var before = first.Position;
        Advance(scene, 1.0);
        scene.PlanetAngle(0).ShouldBe(MathF.PI * 0.72f + 2f * TargetPathsScene.OrbitSpeed, 0.001f);
        Vector2.Distance(before, first.Position).ShouldBeGreaterThan(1f);
    }

    [Fact]
    public void Anchors_build_one_way_per_file_and_mark_the_recommended_target()
    {
        var scene = NewScene();
        scene.Enqueue(Images(("a.png", 1), ("b.png", 1)));
        scene.Enqueue(Anchors("jpg", "jpg", ("a.png", "jpg"), ("b.png", "png")));
        Advance(scene, 1.0);

        scene.Paths.Count.ShouldBe(2);
        scene.Paths[0].IsRecommended.ShouldBeTrue();
        scene.Paths[0].To.ShouldBe(new Vector2(600f, 80f));
        scene.Paths[1].IsRecommended.ShouldBeFalse();
        scene.Paths[1].To.ShouldBe(new Vector2(600f, 130f));
        scene.Paths.ShouldAllBe(p => p.HasTarget);
        // WebP is used by nobody: a faint line from the hole leads there.
        scene.IdleTargets.ShouldBe([new Vector2(600f, 180f)]);

        var path = scene.Paths[0];
        path.PointAt(scene.Hole, 0f).ShouldBe(path.From);
        path.PointAt(scene.Hole, 0.5f).ShouldBe(scene.Hole);
        path.PointAt(scene.Hole, 1f).ShouldBe(path.To);
    }

    [Fact]
    public void A_new_target_bends_the_way_instead_of_snapping_it()
    {
        var scene = NewScene();
        scene.Enqueue(Images(("a.png", 1)));
        scene.Enqueue(Anchors("jpg", "jpg", ("a.png", "jpg")));
        Advance(scene, 3.0);
        scene.Activity.ShouldBe(0f);

        scene.Enqueue(Anchors("png", "jpg", ("a.png", "png")));
        Advance(scene, 0.05);

        var way = scene.Paths.Single();
        way.TargetId.ShouldBe("png");
        way.IsRecommended.ShouldBeFalse();
        // On the way: below the old row, above the new one.
        way.To.Y.ShouldBeGreaterThan(80f);
        way.To.Y.ShouldBeLessThan(130f);
        // The dots run again for a moment after a change, never forever.
        scene.Activity.ShouldBeGreaterThan(0.9f);

        Advance(scene, 1.5);
        Vector2.Distance(way.To, new Vector2(600f, 130f)).ShouldBeLessThan(0.5f);

        Advance(scene, 1.5);
        scene.Activity.ShouldBe(0f);
    }

    [Fact]
    public void A_file_without_a_row_for_its_target_only_gets_the_first_half()
    {
        var scene = NewScene();
        scene.Enqueue(Images(("a.png", 1)));
        scene.Enqueue(Anchors("gif", "jpg", ("a.png", "gif")));
        Advance(scene, 0.1);

        var way = scene.Paths.Single();
        way.HasTarget.ShouldBeFalse();
        way.To.ShouldBe(scene.Hole);
    }

    [Fact]
    public void Another_kind_drops_the_old_ways_and_opens_again()
    {
        var scene = NewScene();
        scene.Enqueue(Images(("a.png", 1)));
        scene.Enqueue(Anchors("jpg", "jpg", ("a.png", "jpg")));
        Advance(scene, 2.0);
        scene.Reveal.ShouldBe(1f);

        scene.Enqueue(new ShowKind(MediaKind.Audio, [new TargetPlanet("a.mp3", 1)]));
        scene.Update(TimeSpan.Zero);

        scene.Kind.ShouldBe(MediaKind.Audio);
        scene.Reveal.ShouldBe(0f);
        scene.Paths.ShouldBeEmpty();
        scene.Planets.Single().FileId.ShouldBe("a.mp3");
    }

    [Fact]
    public void Showing_the_same_kind_again_keeps_the_ways_and_the_open_orbit()
    {
        var scene = NewScene();
        scene.Enqueue(Images(("a.png", 1)));
        scene.Enqueue(Anchors("jpg", "jpg", ("a.png", "jpg")));
        Advance(scene, 2.0);

        scene.Enqueue(Images(("a.png", 1), ("b.png", 1)));
        scene.Update(TimeSpan.Zero);

        scene.Reveal.ShouldBe(1f);
        scene.Paths.Count.ShouldBe(1);
        scene.Planets.Count.ShouldBe(2);
    }

    [Fact]
    public void Resize_moves_the_hole_and_the_ways_with_it()
    {
        var scene = NewScene();
        scene.Enqueue(Images(("a.png", 1)));
        scene.Enqueue(Anchors("jpg", "jpg", ("a.png", "jpg")));
        Advance(scene, 1.0);

        scene.Enqueue(new ResizeTargetPaths(800f, 600f));
        scene.Update(TimeSpan.Zero);

        Vector2.Distance(scene.Hole, new Vector2(400f, 252f)).ShouldBeLessThan(0.001f);
        Vector2.Distance(scene.Paths.Single().To, new Vector2(700f, 122f)).ShouldBeLessThan(0.001f);
        // A zero size is ignored; the last real layout stays.
        scene.Enqueue(new ResizeTargetPaths(0f, 0f));
        scene.Update(TimeSpan.Zero);
        Vector2.Distance(scene.Hole, new Vector2(400f, 252f)).ShouldBeLessThan(0.001f);
    }

    [Fact]
    public void Dust_follows_the_fixed_scatter_of_the_draft()
    {
        var scene = NewScene();
        scene.Enqueue(Images(("a.png", 1)));
        Advance(scene, 1.0);

        // Index 0: offset (0 % 11 - 5) * 1.4 = -7 on the horizontal axis at phase 0.12.
        var expected = scene.OrbitPoint(scene.OrbitPhase, -7f);
        Vector2.Distance(scene.DustPoint(0), expected).ShouldBeLessThan(0.001f);
        scene.DustPoint(0).ShouldNotBe(scene.DustPoint(1));
    }
}
