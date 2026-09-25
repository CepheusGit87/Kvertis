using System.Numerics;
using Kvertis.App.Scenes;
using Kvertis.Engine.Abstractions;
using Shouldly;
using Xunit;

namespace Kvertis.App.Tests.Scenes;

public class TransitionSceneTests
{
    private static readonly MediaKind[] Kinds = [MediaKind.Image, MediaKind.Audio, MediaKind.Video, MediaKind.Model3D, MediaKind.Document];
    private static readonly Vector2 HoleA = new(420f, 260f);
    private static readonly Vector2 HoleB = new(520f, 330f);
    private static readonly Vector2 SwirlCentre = new(700f, 380f);

    private static TransitionEndpoint Tray(int i) => new(new Vector2(120f + 160f * i, 420f), new Vector2(140f, 40f));

    private static TransitionEndpoint Symbol(int i) => new(new Vector2(80f, 140f + 44f * i), new Vector2(150f, 32f));

    private static GhostSpec Spec(GhostShape shape, MediaKind kind) => new(shape, kind, kind.ToString(), "3", null, null);

    private static TransitionPlan Wormhole(int kinds) => new(
        TransitionKind.WormholeForward,
        Enumerable.Range(0, kinds).Select(i => new TransitionGhost(Spec(GhostShape.TrayHeader, Kinds[i]), Tray(i), Symbol(i))).ToList(),
        new TransitionEndpoint(HoleA, Vector2.Zero),
        18f,
        new TransitionEndpoint(HoleB, Vector2.Zero),
        12f);

    private static TransitionPlan Arc(int kinds) => new(
        TransitionKind.ArcBack,
        Enumerable.Range(0, kinds).Select(i => new TransitionGhost(Spec(GhostShape.KindSymbol, Kinds[i]), Symbol(i), Tray(i))).ToList(),
        new TransitionEndpoint(HoleB, Vector2.Zero),
        12f,
        new TransitionEndpoint(HoleA, Vector2.Zero),
        18f);

    private static TransitionPlan Sheets(int files, int kinds = 3)
    {
        var ghosts = new List<TransitionGhost>();
        for (var i = 0; i < kinds; i++)
        {
            ghosts.Add(new TransitionGhost(Spec(GhostShape.KindSymbol, Kinds[i]), Symbol(i), Symbol(i)));
        }

        for (var j = 0; j < files; j++)
        {
            var place = SwirlScene.StackSlot(j, files, 300f);
            var to = new TransitionEndpoint(place.Centre + new Vector2(360f, 60f), place.Size);
            ghosts.Add(new TransitionGhost(new GhostSpec(GhostShape.Sheet, Kinds[j % kinds], "Bilder", null, "PNG", $"f{j}.png"), to, to));
        }

        return new TransitionPlan(
            TransitionKind.SheetsForward,
            ghosts,
            new TransitionEndpoint(HoleB, Vector2.Zero),
            12f,
            new TransitionEndpoint(SwirlCentre, Vector2.Zero),
            12f);
    }

    private static TransitionScene NewScene(TransitionPlan plan) => new(plan, SceneTestData.Palette, new Random(7));

    [Fact]
    public void Wormhole_with_five_kinds_takes_188_seconds_and_staggers_by_120_ms()
    {
        var scene = NewScene(Wormhole(5));

        scene.FlightsEnd.ShouldBe(1.88f, 1e-4f);
        scene.Duration.TotalSeconds.ShouldBe(1.88 + 0.08 + 0.28, 1e-4);
        scene.TrackTiming(4).Delay.ShouldBe(0.48f, 1e-5f);
        scene.TrackTiming(4).Length.ShouldBe(1.4f, 1e-5f);

        var (start, length) = scene.HoleTravel;
        start.ShouldBe(1.034f, 1e-4f);
        (start + length).ShouldBe(1.786f, 1e-4f);
    }

    [Theory]
    [InlineData(0.00, 0)]
    [InlineData(0.30, 1)]
    [InlineData(0.42, 2)]
    [InlineData(0.56, 3)]
    [InlineData(0.82, 4)]
    [InlineData(1.00, 5)]
    public void Wormhole_ghost_passes_its_stations(double share, int station)
    {
        var scene = NewScene(Wormhole(5));
        var from = Tray(0);
        var to = Symbol(0);
        (Vector2 Pos, float W, float Sx, float Sy, float Rot, float A, float B)[] stations =
        [
            (from.Centre, from.Size.X, 1f, 1f, 0f, 1f, 0f),
            (Vector2.Lerp(from.Centre, HoleA, 0.62f), 0.4f * from.Size.X, 0.55f, 1.5f, -0.5f, 1f, 0f),
            (HoleA, 6f, 0.3f, 2f, -1.2f, 0f, 0f),
            (to.Centre, 6f, 1f, 1f, 0f, 0f, 0f),
            (to.Centre, 1.06f * to.Size.X, 1f, 1f, 0f, 0f, 1f),
            (to.Centre, to.Size.X, 1f, 1f, 0f, 0f, 1f),
        ];
        var expected = stations[station];

        SceneTestTime.Run(scene, share * 1.4);
        var ghost = scene.Ghosts[0];

        Vector2.Distance(ghost.Position, expected.Pos).ShouldBeLessThan(0.05f);
        ghost.Width.ShouldBe(expected.W, 0.02f);
        ghost.ScaleX.ShouldBe(expected.Sx, 1e-3f);
        ghost.ScaleY.ShouldBe(expected.Sy, 1e-3f);
        ghost.Rotation.ShouldBe(expected.Rot, 1e-3f);
        ghost.AlphaA.ShouldBe(expected.A, 1e-3f);
        ghost.AlphaB.ShouldBe(expected.B, 1e-3f);
        ghost.Spec.Shape.ShouldBe(GhostShape.TrayHeader);
        ghost.ShapeB.ShouldBe(GhostShape.KindSymbol);
        ghost.NaturalSizeA.ShouldBe(from.Size);
        ghost.NaturalSizeB.ShouldBe(to.Size);
    }

    [Fact]
    public void Wormhole_ghost_four_starts_048_seconds_later()
    {
        var scene = NewScene(Wormhole(5));
        SceneTestTime.Run(scene, 0.48);
        Vector2.Distance(scene.Ghosts[4].Position, Tray(4).Centre).ShouldBeLessThan(1e-3f);

        SceneTestTime.Run(scene, 0.588);
        Vector2.Distance(scene.Ghosts[4].Position, HoleA).ShouldBeLessThan(0.05f);
        scene.Ghosts[4].AlphaA.ShouldBe(0f, 1e-3f);
    }

    [Fact]
    public void Wormhole_plans_two_rings_per_kind_at_the_hole_and_at_the_target()
    {
        var scene = NewScene(Wormhole(5));

        scene.RingStarts.Count.ShouldBe(10);
        for (var i = 0; i < 5; i++)
        {
            scene.RingStarts[2 * i].ShouldBe(0.12f * i + 0.42f * 1.4f, 1e-4f);
            scene.RingStarts[(2 * i) + 1].ShouldBe(0.12f * i + 0.56f * 1.4f, 1e-4f);
        }

        SceneTestTime.Run(scene, 0.588 + 0.325);
        var ring = scene.Rings.First(r => r.Centre == HoleA);
        ring.Color.ShouldBe(SceneTestData.Palette.Image);
        ring.Flatten.ShouldBe(0.36f);
        ring.Radius.ShouldBe(float.Lerp(14f, 44f, SceneMotion.EaseOut(0.5f)), 0.05f);
        ring.Alpha.ShouldBe(0.45f, 0.01f);
        scene.Rings.ShouldContain(r => r.Color == SceneTestData.Palette.Mint && r.Centre == Symbol(0).Centre - new Vector2(0f, 8f));
    }

    [Fact]
    public void Arc_back_takes_110_seconds_and_lifts_each_kind_to_its_own_apex()
    {
        var scene = NewScene(Arc(5));
        scene.FlightsEnd.ShouldBe(1.10f, 1e-4f);
        scene.HoleTravel.Length.ShouldBe(0.99f, 1e-4f);

        for (var i = 0; i < 5; i++)
        {
            var keys = scene.TrackKeys(i);
            var s = Symbol(i).Centre;
            var t = Tray(i).Centre;
            keys[1].Offset.ShouldBe(0.5f);
            keys[1].Position.Y.ShouldBe(MathF.Min(s.Y, t.Y) + 0.35f * MathF.Abs(s.Y - t.Y) - 40f - 14f * i, 1e-3f);
            keys[1].Position.X.ShouldBe(float.Lerp(s.X, t.X, 0.5f) - 30f, 1e-3f);
            scene.TrackTiming(i).Delay.ShouldBe(0.07f * i, 1e-5f);
        }

        SceneTestTime.Run(scene, (0.07 * 2) + 0.41);
        scene.Ghosts[2].Position.Y.ShouldBe(scene.TrackKeys(2)[1].Position.Y, 0.05f);
        scene.Rings.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(7)]
    [InlineData(11)]
    [InlineData(24)]
    [InlineData(60)]
    public void Sheets_never_take_longer_than_two_seconds_and_at_most_24_fly(int files)
    {
        var scene = NewScene(Sheets(files));

        scene.FlightsEnd.ShouldBeLessThanOrEqualTo(2.0f + 1e-4f);
        scene.TrackCount.ShouldBe(3 + Math.Min(files, 24));

        var stagger = TransitionScene.SheetStagger(files);
        stagger.ShouldBeLessThanOrEqualTo(0.09f);
        for (var j = 0; j < Math.Min(files, 24); j++)
        {
            scene.TrackTiming(3 + j).Delay.ShouldBe(0.15f + j * stagger, 1e-4f);
            scene.TrackTiming(3 + j).Length.ShouldBe(0.95f, 1e-5f);
        }

        if (files == 1)
        {
            scene.FlightsEnd.ShouldBe(1.10f, 1e-4f);
        }
    }

    [Fact]
    public void Sheets_land_on_their_stack_place_with_its_rotation()
    {
        var scene = NewScene(Sheets(7));
        SceneTestTime.Run(scene, scene.FlightsEnd);

        var sheets = scene.Ghosts.Where(g => g.Spec.Shape == GhostShape.Sheet).ToList();
        sheets.Count.ShouldBe(7);
        for (var j = 0; j < 7; j++)
        {
            var place = SwirlScene.StackSlot(j, 7, 300f);
            Vector2.Distance(sheets[j].Position, place.Centre + new Vector2(360f, 60f)).ShouldBeLessThan(0.01f);
            sheets[j].Rotation.ShouldBe(place.Rotation, 1e-5f);
            sheets[j].Width.ShouldBe(48f, 1e-3f);
            sheets[j].NaturalSizeA.ShouldBe(new Vector2(96f, 124f));
        }
    }

    [Fact]
    public void Sheets_hole_travels_on_a_30_dip_arc()
    {
        var scene = NewScene(Sheets(7));
        var (start, length) = scene.HoleTravel;
        start.ShouldBe(0f);
        length.ShouldBe(0.85f * scene.FlightsEnd, 1e-4f);

        // Seven sheets: 1.64 s in total, the hole travels 1.394 s; its middle is at 0.697 s.
        SceneTestTime.Run(scene, 0.697);
        var hole = scene.Hole.ShouldNotBeNull();
        var straight = Vector2.Lerp(HoleB, SwirlCentre, 0.5f);
        hole.Position.X.ShouldBe(straight.X, 0.1f);
        hole.Position.Y.ShouldBe(straight.Y - 30f, 0.1f);
    }

    [Fact]
    public void Sparks_appear_only_in_the_last_028_seconds()
    {
        var scene = NewScene(Sheets(7));
        var total = scene.Duration.TotalSeconds;
        var sawSparks = false;
        SceneTestTime.Run(
            dt =>
            {
                scene.Update(dt);
                if (scene.Sparks.Count > 0)
                {
                    sawSparks = true;
                    scene.Time.TotalSeconds.ShouldBeGreaterThanOrEqualTo(total - 0.28 - 1e-6);
                    scene.Sparks.Count.ShouldBe(TransitionScene.SparkCount);
                    scene.Sparks.ShouldAllBe(s => s.Size == 2f);
                }
            },
            total);

        sawSparks.ShouldBeTrue();
        NewScene(Wormhole(3)).Sparks.ShouldBeEmpty();
    }

    [Fact]
    public void Page_fades_in_from_026_seconds_before_the_end_of_the_flights()
    {
        var scene = NewScene(Wormhole(5));
        SceneTestTime.Run(scene, 1.60);
        scene.PageFadeIn.ShouldBe(0f, 1e-3f);
        SceneTestTime.Run(scene, 0.18);
        scene.PageFadeIn.ShouldBe(0.5f, 0.01f);
        SceneTestTime.Run(scene, 0.20);
        scene.PageFadeIn.ShouldBe(1f);
    }

    [Fact]
    public void Finish_jumps_to_the_end_state()
    {
        var scene = NewScene(Wormhole(4));
        SceneTestTime.Run(scene, 0.3);

        scene.Finish();
        scene.Update(TimeSpan.Zero);

        scene.IsFinished.ShouldBeTrue();
        scene.Time.ShouldBe(scene.Duration);
        scene.PageFadeIn.ShouldBe(1f);
        scene.Ghosts.ShouldBeEmpty();
        scene.Hole.ShouldBeNull();
        scene.Snapshot.IsFinished.ShouldBeTrue();
    }

    [Fact]
    public void Update_clamps_a_long_step()
    {
        var scene = NewScene(Wormhole(2));
        scene.Update(TimeSpan.FromSeconds(3));
        scene.Time.ShouldBe(TransitionScene.MaxStep);
    }

    [Fact]
    public void A_snapshot_never_changes_after_it_was_published()
    {
        var scene = NewScene(Wormhole(3));
        SceneTestTime.Run(scene, 0.5);
        var before = scene.Snapshot;
        var copy = before with { };

        SceneTestTime.Run(scene, 1.5);

        before.ShouldBe(copy);
        scene.Snapshot.ShouldNotBeSameAs(before);
        scene.Snapshot.Time.ShouldBeGreaterThan(before.Time);
    }
}
