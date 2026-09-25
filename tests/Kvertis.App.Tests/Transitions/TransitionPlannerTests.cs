using System.Numerics;
using Kvertis.App.Scenes;
using Kvertis.App.Services;
using Kvertis.App.Tests.Scenes;
using Kvertis.Engine.Abstractions;
using Shouldly;
using Xunit;

namespace Kvertis.App.Tests.Transitions;

/// <summary>Anchors in, plan out: every ghost flies from the source anchor of its kind to the target anchor of the same kind.</summary>
public sealed class TransitionPlannerTests
{
    private static TransitionAnchor Tray(MediaKind kind, float x) => new(TransitionAnchorKind.Tray, kind, new Vector2(x, 400f), new Vector2(140f, 40f));

    private static TransitionAnchor Symbol(MediaKind kind, float y) => new(TransitionAnchorKind.KindSymbol, kind, new Vector2(80f, y), new Vector2(150f, 32f));

    private static TransitionAnchor Hole(float x, float y, float r) => new(TransitionAnchorKind.Hole, null, new Vector2(x, y), Vector2.Zero, r);

    private static GhostSpec Spec(GhostShape shape, MediaKind kind) => new(shape, kind, kind.ToString(), "2", null, null);

    private static GhostSpec Sheet(MediaKind kind, string name) => new(GhostShape.Sheet, kind, kind.ToString(), null, "PNG", name);

    private static TransitionAnchorSet Step1(params TransitionAnchor[] anchors) => new(WorkflowStep.Drop, anchors);

    private static TransitionAnchorSet Step2(params TransitionAnchor[] anchors) => new(WorkflowStep.Target, anchors);

    [Theory]
    [InlineData(WorkflowStep.Drop, WorkflowStep.Target, TransitionKind.WormholeForward)]
    [InlineData(WorkflowStep.Target, WorkflowStep.Drop, TransitionKind.ArcBack)]
    [InlineData(WorkflowStep.Target, WorkflowStep.Convert, TransitionKind.SheetsForward)]
    public void The_three_pairs_get_a_flight(WorkflowStep from, WorkflowStep to, TransitionKind expected) =>
        TransitionPlanner.KindOf(from, to).ShouldBe(expected);

    [Theory]
    [InlineData(WorkflowStep.Convert, WorkflowStep.Target)]
    [InlineData(WorkflowStep.Convert, WorkflowStep.Drop)]
    [InlineData(WorkflowStep.Drop, WorkflowStep.Convert)]
    [InlineData(WorkflowStep.Drop, WorkflowStep.Drop)]
    public void Every_other_pair_is_a_plain_switch(WorkflowStep from, WorkflowStep to) =>
        TransitionPlanner.KindOf(from, to).ShouldBeNull();

    [Fact]
    public void Wormhole_pairs_trays_and_symbols_by_kind_in_tray_order()
    {
        var source = Step1(Tray(MediaKind.Image, 100f), Tray(MediaKind.Video, 300f), Hole(420f, 200f, 18f));
        var target = Step2(Symbol(MediaKind.Video, 140f), Symbol(MediaKind.Image, 184f), Hole(520f, 300f, 12f));
        var specs = new[] { Spec(GhostShape.TrayHeader, MediaKind.Video), Spec(GhostShape.TrayHeader, MediaKind.Image) };

        var plan = TransitionPlanner.Build(TransitionKind.WormholeForward, source, target, specs, []);

        plan.ShouldNotBeNull();
        plan.Kind.ShouldBe(TransitionKind.WormholeForward);
        plan.Ghosts.Count.ShouldBe(2);
        plan.Ghosts[0].Ghost.Kind.ShouldBe(MediaKind.Image);
        plan.Ghosts[0].From.Centre.ShouldBe(new Vector2(100f, 400f));
        plan.Ghosts[0].To.Centre.ShouldBe(new Vector2(80f, 184f));
        plan.Ghosts[1].Ghost.Kind.ShouldBe(MediaKind.Video);
        plan.Ghosts[1].To.Centre.ShouldBe(new Vector2(80f, 140f));
        plan.HoleFrom.Centre.ShouldBe(new Vector2(420f, 200f));
        plan.HoleRadiusFrom.ShouldBe(18f);
        plan.HoleTo.Centre.ShouldBe(new Vector2(520f, 300f));
        plan.HoleRadiusTo.ShouldBe(12f);
    }

    [Fact]
    public void A_kind_without_a_target_anchor_is_left_out()
    {
        var source = Step1(Tray(MediaKind.Image, 100f), Tray(MediaKind.Audio, 200f), Hole(420f, 200f, 18f));
        var target = Step2(Symbol(MediaKind.Image, 140f), Hole(520f, 300f, 12f));
        var specs = new[] { Spec(GhostShape.TrayHeader, MediaKind.Image), Spec(GhostShape.TrayHeader, MediaKind.Audio) };

        var plan = TransitionPlanner.Build(TransitionKind.WormholeForward, source, target, specs, []);

        plan.ShouldNotBeNull();
        plan.Ghosts.Count.ShouldBe(1);
        plan.Ghosts[0].Ghost.Kind.ShouldBe(MediaKind.Image);
    }

    [Fact]
    public void Without_a_source_hole_or_without_any_pair_there_is_no_plan()
    {
        var specs = new[] { Spec(GhostShape.TrayHeader, MediaKind.Image) };
        var noHole = Step1(Tray(MediaKind.Image, 100f));
        var withHole = Step1(Tray(MediaKind.Image, 100f), Hole(420f, 200f, 18f));
        var target = Step2(Symbol(MediaKind.Audio, 140f), Hole(520f, 300f, 12f));

        TransitionPlanner.Build(TransitionKind.WormholeForward, noHole, target, specs, []).ShouldBeNull();
        TransitionPlanner.Build(TransitionKind.WormholeForward, withHole, target, specs, []).ShouldBeNull();
        TransitionPlanner.HasSource(TransitionKind.WormholeForward, noHole, specs, 0).ShouldBeFalse();
        TransitionPlanner.HasSource(TransitionKind.WormholeForward, withHole, specs, 0).ShouldBeTrue();
        TransitionPlanner.HasSource(TransitionKind.WormholeForward, withHole, [Spec(GhostShape.TrayHeader, MediaKind.Audio)], 0).ShouldBeFalse();
    }

    [Fact]
    public void Arc_back_flies_from_symbols_to_trays_and_the_hole_back_to_the_galaxy()
    {
        var source = Step2(Symbol(MediaKind.Image, 140f), Hole(520f, 300f, 12f));
        var target = Step1(Tray(MediaKind.Image, 100f), Hole(420f, 200f, 18f));

        var plan = TransitionPlanner.Build(TransitionKind.ArcBack, source, target, [Spec(GhostShape.KindSymbol, MediaKind.Image)], []);

        plan.ShouldNotBeNull();
        plan.Kind.ShouldBe(TransitionKind.ArcBack);
        plan.Ghosts.Single().From.Centre.ShouldBe(new Vector2(80f, 140f));
        plan.Ghosts.Single().To.Centre.ShouldBe(new Vector2(100f, 400f));
        plan.HoleRadiusFrom.ShouldBe(12f);
        plan.HoleTo.Centre.ShouldBe(new Vector2(420f, 200f));
        plan.HoleRadiusTo.ShouldBe(18f);
    }

    [Fact]
    public void A_target_without_a_hole_keeps_the_hole_where_it_is_with_the_page_radius()
    {
        var source = Step2(Symbol(MediaKind.Image, 140f), Hole(520f, 300f, 12f));
        var target = Step1(Tray(MediaKind.Image, 100f));

        var plan = TransitionPlanner.Build(TransitionKind.ArcBack, source, target, [Spec(GhostShape.KindSymbol, MediaKind.Image)], []);

        plan.ShouldNotBeNull();
        plan.HoleTo.Centre.ShouldBe(new Vector2(520f, 300f));
        plan.HoleRadiusTo.ShouldBe(TransitionPlanner.PageHoleRadius);
    }

    [Fact]
    public void Sheets_land_on_the_stack_places_of_the_swirl_surface()
    {
        var source = Step2(Symbol(MediaKind.Image, 140f), Hole(520f, 300f, 12f));
        var swirl = new TransitionAnchor(TransitionAnchorKind.SwirlCentre, null, new Vector2(700f, 400f), new Vector2(400f, 300f));
        var target = new TransitionAnchorSet(WorkflowStep.Convert, [swirl]);
        var sheets = new[] { Sheet(MediaKind.Image, "a.png"), Sheet(MediaKind.Image, "b.png"), Sheet(MediaKind.Image, "c.png") };

        var plan = TransitionPlanner.Build(TransitionKind.SheetsForward, source, target, [Spec(GhostShape.KindSymbol, MediaKind.Image)], sheets);

        plan.ShouldNotBeNull();
        plan.Kind.ShouldBe(TransitionKind.SheetsForward);
        plan.Ghosts.Count.ShouldBe(4);
        plan.Ghosts[0].Ghost.Shape.ShouldBe(GhostShape.KindSymbol);
        plan.Ghosts[0].From.ShouldBe(plan.Ghosts[0].To);
        for (var j = 0; j < 3; j++)
        {
            var ghost = plan.Ghosts[1 + j];
            ghost.Ghost.Shape.ShouldBe(GhostShape.Sheet);
            ghost.Ghost.FileName.ShouldBe(sheets[j].FileName);
            ghost.From.Centre.ShouldBe(new Vector2(520f, 300f));
            // Canvas coordinates of the stack place plus the top-left corner of the surface (500, 250).
            var place = SwirlScene.StackSlot(j, 3, 150f);
            ghost.To.Centre.ShouldBe(new Vector2(500f, 250f) + place.Centre);
            ghost.To.Size.ShouldBe(new Vector2(48f, 62f));
        }

        plan.HoleTo.Centre.ShouldBe(new Vector2(700f, 400f));
        plan.HoleRadiusTo.ShouldBe(12f);
    }

    [Fact]
    public void Sheets_need_the_swirl_anchor_and_at_least_one_file()
    {
        var source = Step2(Symbol(MediaKind.Image, 140f), Hole(520f, 300f, 12f));
        var swirl = new TransitionAnchor(TransitionAnchorKind.SwirlCentre, null, new Vector2(700f, 400f), new Vector2(400f, 300f));

        TransitionPlanner.Build(TransitionKind.SheetsForward, source, new TransitionAnchorSet(WorkflowStep.Convert, []), [], [Sheet(MediaKind.Image, "a.png")]).ShouldBeNull();
        TransitionPlanner.Build(TransitionKind.SheetsForward, source, new TransitionAnchorSet(WorkflowStep.Convert, [swirl]), [], []).ShouldBeNull();
        TransitionPlanner.HasSource(TransitionKind.SheetsForward, source, [], 0).ShouldBeFalse();
        TransitionPlanner.HasSource(TransitionKind.SheetsForward, source, [], 1).ShouldBeTrue();
    }

    [Fact]
    public void The_hold_plan_keeps_every_ghost_on_its_anchor_at_the_first_frame()
    {
        var source = Step1(Tray(MediaKind.Image, 100f), Tray(MediaKind.Video, 300f), Hole(420f, 200f, 18f));
        var specs = new[] { Spec(GhostShape.TrayHeader, MediaKind.Image), Spec(GhostShape.TrayHeader, MediaKind.Video) };

        var plan = TransitionPlanner.BuildHold(TransitionKind.WormholeForward, source, specs);
        var scene = new TransitionScene(plan, SceneTestData.Palette, new Random(1));

        scene.Ghosts.Count.ShouldBe(2);
        scene.Ghosts[0].Position.ShouldBe(new Vector2(100f, 400f));
        scene.Ghosts[0].Width.ShouldBe(140f);
        scene.Ghosts[0].AlphaA.ShouldBe(1f);
        scene.Ghosts[0].AlphaB.ShouldBe(0f);
        scene.Hole.ShouldNotBeNull();
        scene.Hole.Value.Position.ShouldBe(new Vector2(420f, 200f));
        scene.Hole.Value.Radius.ShouldBe(18f);
    }
}
