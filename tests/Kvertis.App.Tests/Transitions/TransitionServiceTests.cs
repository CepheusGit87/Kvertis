using System.Numerics;
using Kvertis.App.Scenes;
using Kvertis.App.Services;
using Kvertis.Engine.Abstractions;
using Kvertis.Engine.Formats;
using Kvertis.App.Tests.Scenes;
using Kvertis.Engine.Naming;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Kvertis.App.Tests.Transitions;

/// <summary>The flow of a transition against a fake stage: decision, order of the calls, end state, abort.</summary>
public sealed class TransitionServiceTests
{
    private static StagedFile File(string path, MediaKind kind = MediaKind.Image) =>
        new(new InputInfo(path, FormatRegistry.Png, kind, 1000, null, 100, 100, null, []), path);

    private static PlannedConversion Item(string path, MediaKind kind = MediaKind.Image) =>
        new(new InputInfo(path, FormatRegistry.Png, kind, 1000, null, 100, 100, null, []), new ConversionSettings(FormatRegistry.Jpg), OutputNamePattern.Default);

    private static TransitionAnchorSet Step1Anchors(params MediaKind[] kinds)
    {
        var anchors = kinds.Select((k, i) => new TransitionAnchor(TransitionAnchorKind.Tray, k, new Vector2(100f + 160f * i, 400f), new Vector2(140f, 40f))).ToList();
        anchors.Add(new TransitionAnchor(TransitionAnchorKind.Hole, null, new Vector2(420f, 200f), Vector2.Zero, 18f));
        return new TransitionAnchorSet(WorkflowStep.Drop, anchors);
    }

    private static TransitionAnchorSet Step2Anchors(params MediaKind[] kinds)
    {
        var anchors = kinds.Select((k, i) => new TransitionAnchor(TransitionAnchorKind.KindSymbol, k, new Vector2(80f, 140f + 44f * i), new Vector2(150f, 32f))).ToList();
        anchors.Add(new TransitionAnchor(TransitionAnchorKind.Hole, null, new Vector2(520f, 300f), Vector2.Zero, 12f));
        return new TransitionAnchorSet(WorkflowStep.Target, anchors);
    }

    private static TransitionAnchorSet Step3Anchors() => new(
        WorkflowStep.Convert,
        [new TransitionAnchor(TransitionAnchorKind.SwirlCentre, null, new Vector2(700f, 400f), new Vector2(400f, 300f))]);

    private sealed class Fixture
    {
        public readonly FakeMotion Motion = new();
        public readonly WorkflowSession Session = new();
        public readonly FakeStage Stage = new();
        public readonly TransitionService Service;

        public Fixture(bool attach = true)
        {
            Service = new TransitionService(Motion, Session, TestDoubles.Localizer(), new FormatRegistry(), random: () => new Random(7));
            if (attach)
            {
                Service.Attach(Stage);
            }
        }
    }

    private sealed class FakeMotion : IMotionSettings
    {
        public bool AnimationsEnabled { get; set; } = true;

        public bool IsHighContrast { get; set; }

        public bool ReducedMotion => !AnimationsEnabled || IsHighContrast;

        public event EventHandler? Changed;

        public void Raise() => Changed?.Invoke(this, EventArgs.Empty);
    }

    private sealed class FakeStage : ITransitionStage
    {
        public readonly List<string> Calls = [];

        public TransitionAnchorSet? Current { get; set; }

        public TransitionAnchorSet? Target { get; set; }

        public bool CanBegin { get; set; } = true;

        public ScenePalette Palette => SceneTestData.Palette;

        public TransitionScene? Held { get; private set; }

        public TransitionScene? Started { get; private set; }

        public bool InputLocked { get; private set; }

        public event EventHandler? FlightsEnded;

        public event EventHandler? Completed;

        public event EventHandler? DrawFailed;

        public TransitionAnchorSet? MeasureCurrent()
        {
            Calls.Add("measure-source");
            return Current;
        }

        public void Navigate(WorkflowStep step, bool hidden) => Calls.Add($"navigate:{step}:{(hidden ? "hidden" : "shown")}");

        public void NavigateFaded(WorkflowStep step) => Calls.Add($"fade:{step}");

        public TransitionAnchorKind? RequiredAnchor { get; private set; }

        /// <summary>When set, <see cref="HoldAsync"/> waits for it instead of completing at once.</summary>
        public TaskCompletionSource? HoldGate { get; set; }

        public Task<TransitionAnchorSet?> MeasureTargetAsync(TransitionAnchorKind required)
        {
            RequiredAnchor = required;
            Calls.Add("measure-target");
            return Task.FromResult(Target);
        }

        public void SetFlightVisibility(TransitionSide side, bool visible, IReadOnlySet<MediaKind> kinds) =>
            Calls.Add($"{side}:{(visible ? "show" : "hide")}:{string.Join("+", kinds.OrderBy(k => k))}");

        public void SetInputLocked(bool locked)
        {
            InputLocked = locked;
            Calls.Add(locked ? "lock" : "unlock");
        }

        public Task HoldAsync(TransitionScene scene)
        {
            Held = scene;
            Calls.Add("hold");
            return HoldGate?.Task ?? Task.CompletedTask;
        }

        public void Start(TransitionScene scene)
        {
            Started = scene;
            Calls.Add("start");
        }

        public void End() => Calls.Add("end");

        public void RaiseFlightsEnded() => FlightsEnded?.Invoke(this, EventArgs.Empty);

        public void RaiseCompleted() => Completed?.Invoke(this, EventArgs.Empty);

        public void RaiseDrawFailed() => DrawFailed?.Invoke(this, EventArgs.Empty);
    }

    [Fact]
    public void Without_a_stage_nothing_happens()
    {
        var f = new Fixture(attach: false);
        f.Session.SetStaged([File("a.png")]);

        f.Service.TryBegin(WorkflowStep.Drop, WorkflowStep.Target).ShouldBeFalse();
        f.Service.IsTransitioning.ShouldBeFalse();
    }

    [Fact]
    public void Reduced_motion_means_a_plain_switch()
    {
        var f = new Fixture();
        f.Session.SetStaged([File("a.png")]);
        f.Stage.Current = Step1Anchors(MediaKind.Image);
        f.Motion.AnimationsEnabled = false;

        f.Service.TryBegin(WorkflowStep.Drop, WorkflowStep.Target).ShouldBeFalse();
        f.Service.TryBegin(WorkflowStep.Convert, WorkflowStep.Target).ShouldBeFalse();
        f.Stage.Calls.ShouldBeEmpty();
    }

    [Fact]
    public void High_contrast_means_a_plain_switch()
    {
        var f = new Fixture();
        f.Session.SetStaged([File("a.png")]);
        f.Stage.Current = Step1Anchors(MediaKind.Image);
        f.Motion.IsHighContrast = true;

        f.Service.TryBegin(WorkflowStep.Drop, WorkflowStep.Target).ShouldBeFalse();
        f.Stage.Calls.ShouldBeEmpty();
    }

    [Fact]
    public void Three_to_two_is_only_a_cross_fade()
    {
        var f = new Fixture();
        f.Session.SetStaged([File("a.png")]);

        f.Service.TryBegin(WorkflowStep.Convert, WorkflowStep.Target).ShouldBeTrue();

        f.Stage.Calls.ShouldBe(["fade:Target"]);
        f.Service.IsTransitioning.ShouldBeFalse();
    }

    [Fact]
    public void No_staged_files_no_source_anchors_or_a_busy_stage_mean_a_plain_switch()
    {
        var f = new Fixture();
        f.Stage.Current = Step1Anchors(MediaKind.Image);
        f.Service.TryBegin(WorkflowStep.Drop, WorkflowStep.Target).ShouldBeFalse();

        f.Session.SetStaged([File("a.png")]);
        f.Stage.Current = null;
        f.Service.TryBegin(WorkflowStep.Drop, WorkflowStep.Target).ShouldBeFalse();

        f.Stage.Current = Step1Anchors(MediaKind.Image);
        f.Stage.CanBegin = false;
        f.Service.TryBegin(WorkflowStep.Drop, WorkflowStep.Target).ShouldBeFalse();

        // A tray of a kind that is not staged is no source either.
        f.Stage.CanBegin = true;
        f.Stage.Current = Step1Anchors(MediaKind.Audio);
        f.Service.TryBegin(WorkflowStep.Drop, WorkflowStep.Target).ShouldBeFalse();

        f.Service.IsTransitioning.ShouldBeFalse();
    }

    [Fact]
    public void The_wormhole_runs_hold_navigate_measure_start_and_ends_on_completed()
    {
        var f = new Fixture();
        f.Session.SetStaged([File("a.png"), File("b.png"), File("c.mp4", MediaKind.Video)]);
        f.Stage.Current = Step1Anchors(MediaKind.Image, MediaKind.Video);
        f.Stage.Target = Step2Anchors(MediaKind.Image, MediaKind.Video);
        var changes = 0;
        f.Service.Changed += (_, _) => changes++;

        f.Service.TryBegin(WorkflowStep.Drop, WorkflowStep.Target).ShouldBeTrue();

        f.Service.IsTransitioning.ShouldBeTrue();
        changes.ShouldBe(1);
        f.Stage.Calls.ShouldBe([
            "measure-source",
            "lock",
            "Source:hide:Image+Video",
            "hold",
            "navigate:Target:hidden",
            "Target:hide:Image+Video",
            "measure-target",
            "start",
        ]);
        f.Stage.Held.ShouldNotBeNull();
        f.Stage.Started.ShouldNotBeNull();
        f.Stage.Started.Plan.Kind.ShouldBe(TransitionKind.WormholeForward);
        f.Stage.Started.Plan.Ghosts.Count.ShouldBe(2);
        f.Stage.Started.Plan.Ghosts[0].Ghost.Label.ShouldBe("Kind_Image_Name");
        f.Stage.Started.Plan.Ghosts[0].Ghost.Count.ShouldBe("2");
        f.Stage.Started.Plan.Ghosts[1].Ghost.Count.ShouldBe("1");
        f.Stage.Started.FlightsEnd.ShouldBe(0.12f + 1.40f, 0.001f);

        f.Stage.RaiseFlightsEnded();
        f.Stage.Calls[^1].ShouldBe("Target:show:Image+Video");
        f.Service.IsTransitioning.ShouldBeTrue();

        f.Stage.RaiseCompleted();
        f.Service.IsTransitioning.ShouldBeFalse();
        changes.ShouldBe(2);
        f.Stage.Calls.TakeLast(4).ShouldBe(["end", "Source:show:Image+Video", "Target:show:Image+Video", "unlock"]);
    }

    [Fact]
    public void Finish_in_the_middle_of_a_flight_gives_the_end_state_at_once()
    {
        var f = new Fixture();
        f.Session.SetStaged([File("a.png")]);
        f.Stage.Current = Step1Anchors(MediaKind.Image);
        f.Stage.Target = Step2Anchors(MediaKind.Image);
        f.Service.TryBegin(WorkflowStep.Drop, WorkflowStep.Target).ShouldBeTrue();

        f.Service.Finish();

        f.Service.IsTransitioning.ShouldBeFalse();
        f.Stage.InputLocked.ShouldBeFalse();
        f.Stage.Calls.TakeLast(4).ShouldBe(["end", "Source:show:Image", "Target:show:Image", "unlock"]);
        // A late Completed from the overlay changes nothing.
        var count = f.Stage.Calls.Count;
        f.Stage.RaiseCompleted();
        f.Stage.Calls.Count.ShouldBe(count);
    }

    [Fact]
    public void Finish_during_the_hold_still_navigates_to_the_step()
    {
        var f = new Fixture();
        f.Session.SetStaged([File("a.png")]);
        f.Stage.Current = Step1Anchors(MediaKind.Image);
        f.Stage.Target = Step2Anchors(MediaKind.Image);
        f.Stage.HoldGate = new TaskCompletionSource();
        f.Service.TryBegin(WorkflowStep.Drop, WorkflowStep.Target).ShouldBeTrue();
        f.Stage.Calls.ShouldNotContain(c => c.StartsWith("navigate", StringComparison.Ordinal));

        f.Service.Finish();

        f.Service.IsTransitioning.ShouldBeFalse();
        f.Stage.Calls.ShouldContain("navigate:Target:shown");
        f.Stage.Calls.IndexOf("navigate:Target:shown").ShouldBeLessThan(f.Stage.Calls.IndexOf("end"));
        // The hold resolving later must not navigate a second time or start a scene.
        f.Stage.HoldGate.SetResult();
        f.Stage.Calls.Count(c => c.StartsWith("navigate", StringComparison.Ordinal)).ShouldBe(1);
        f.Stage.Started.ShouldBeNull();
    }

    [Fact]
    public void The_target_measurement_asks_for_the_anchor_kind_the_ghosts_fly_to()
    {
        var f = new Fixture();
        f.Session.SetStaged([File("a.png")]);
        f.Session.Plan = new TargetPlan([Item(@"C:\x\a.png")], []);

        f.Stage.Current = Step1Anchors(MediaKind.Image);
        f.Stage.Target = Step2Anchors(MediaKind.Image);
        f.Service.TryBegin(WorkflowStep.Drop, WorkflowStep.Target).ShouldBeTrue();
        f.Stage.RequiredAnchor.ShouldBe(TransitionAnchorKind.KindSymbol);

        f.Stage.Current = Step2Anchors(MediaKind.Image);
        f.Stage.Target = Step1Anchors(MediaKind.Image);
        f.Service.TryBegin(WorkflowStep.Target, WorkflowStep.Drop).ShouldBeTrue();
        f.Stage.RequiredAnchor.ShouldBe(TransitionAnchorKind.Tray);

        f.Stage.Current = Step2Anchors(MediaKind.Image);
        f.Stage.Target = Step3Anchors();
        f.Service.TryBegin(WorkflowStep.Target, WorkflowStep.Convert).ShouldBeTrue();
        f.Stage.RequiredAnchor.ShouldBe(TransitionAnchorKind.SwirlCentre);
    }

    [Fact]
    public void A_target_that_cannot_be_measured_ends_with_the_plain_end_state()
    {
        var f = new Fixture();
        f.Session.SetStaged([File("a.png")]);
        f.Stage.Current = Step1Anchors(MediaKind.Image);
        f.Stage.Target = null;

        f.Service.TryBegin(WorkflowStep.Drop, WorkflowStep.Target).ShouldBeTrue();

        f.Service.IsTransitioning.ShouldBeFalse();
        f.Stage.Started.ShouldBeNull();
        f.Stage.Calls.ShouldContain("navigate:Target:hidden");
        f.Stage.Calls.TakeLast(4).ShouldBe(["end", "Source:show:Image", "Target:show:Image", "unlock"]);
    }

    [Fact]
    public void A_new_transition_finishes_the_running_one_first()
    {
        var f = new Fixture();
        f.Session.SetStaged([File("a.png")]);
        f.Stage.Current = Step1Anchors(MediaKind.Image);
        f.Stage.Target = Step2Anchors(MediaKind.Image);
        f.Service.TryBegin(WorkflowStep.Drop, WorkflowStep.Target).ShouldBeTrue();
        var first = f.Stage.Started;

        f.Stage.Current = Step2Anchors(MediaKind.Image);
        f.Stage.Target = Step1Anchors(MediaKind.Image);
        f.Service.TryBegin(WorkflowStep.Target, WorkflowStep.Drop).ShouldBeTrue();

        f.Stage.Started.ShouldNotBeSameAs(first);
        f.Stage.Started!.Plan.Kind.ShouldBe(TransitionKind.ArcBack);
        f.Stage.Calls.Count(c => c == "end").ShouldBe(1);
        f.Service.IsTransitioning.ShouldBeTrue();
    }

    [Fact]
    public void A_change_of_the_motion_setting_finishes_the_flight()
    {
        var f = new Fixture();
        f.Session.SetStaged([File("a.png")]);
        f.Stage.Current = Step1Anchors(MediaKind.Image);
        f.Stage.Target = Step2Anchors(MediaKind.Image);
        f.Service.TryBegin(WorkflowStep.Drop, WorkflowStep.Target).ShouldBeTrue();

        f.Motion.Raise();

        f.Service.IsTransitioning.ShouldBeFalse();
    }

    [Fact]
    public void After_a_drawing_error_the_overlay_is_never_used_again()
    {
        var f = new Fixture();
        f.Session.SetStaged([File("a.png")]);
        f.Stage.Current = Step1Anchors(MediaKind.Image);
        f.Stage.Target = Step2Anchors(MediaKind.Image);
        f.Service.TryBegin(WorkflowStep.Drop, WorkflowStep.Target).ShouldBeTrue();

        f.Stage.RaiseDrawFailed();

        f.Service.IsTransitioning.ShouldBeFalse();
        f.Service.TryBegin(WorkflowStep.Drop, WorkflowStep.Target).ShouldBeFalse();
    }

    [Fact]
    public void Sheets_take_the_files_of_the_plan_with_format_and_name()
    {
        var f = new Fixture();
        f.Session.SetStaged([File("a.png"), File("b.png")]);
        f.Session.Plan = new TargetPlan([Item(@"C:\x\a.png"), Item(@"C:\x\b.png")], []);
        f.Stage.Current = Step2Anchors(MediaKind.Image);
        f.Stage.Target = Step3Anchors();

        f.Service.TryBegin(WorkflowStep.Target, WorkflowStep.Convert).ShouldBeTrue();

        var plan = f.Stage.Started.ShouldNotBeNull().Plan;
        plan.Kind.ShouldBe(TransitionKind.SheetsForward);
        plan.Ghosts.Count.ShouldBe(3);
        plan.Ghosts[1].Ghost.Shape.ShouldBe(GhostShape.Sheet);
        plan.Ghosts[1].Ghost.FileName.ShouldBe("a.png");
        plan.Ghosts[1].Ghost.FormatLabel.ShouldBe(TargetPlanner.Label(new FormatRegistry(), FormatRegistry.Jpg));
        plan.Ghosts[2].Ghost.FileName.ShouldBe("b.png");
    }

    [Fact]
    public void Sheets_without_a_plan_mean_a_plain_switch()
    {
        var f = new Fixture();
        f.Session.SetStaged([File("a.png")]);
        f.Stage.Current = Step2Anchors(MediaKind.Image);

        f.Service.TryBegin(WorkflowStep.Target, WorkflowStep.Convert).ShouldBeFalse();
    }

    [Fact]
    public void The_step_navigation_asks_the_service_first()
    {
        var navigation = Substitute.For<INavigationService>();
        navigation.CurrentPage.Returns(AppPage.Main);
        var transitions = Substitute.For<ITransitionService>();
        var steps = new StepNavigationService(navigation, transitions);
        steps.CurrentStep.ShouldBe(WorkflowStep.Drop);

        transitions.TryBegin(WorkflowStep.Drop, WorkflowStep.Target).Returns(true);
        steps.GoTo(WorkflowStep.Target);
        navigation.DidNotReceive().Navigate(Arg.Any<AppPage>(), Arg.Any<bool>());

        transitions.TryBegin(WorkflowStep.Drop, WorkflowStep.Target).Returns(false);
        steps.GoTo(WorkflowStep.Target);
        navigation.Received(1).Navigate(AppPage.Target, false);
    }
}
