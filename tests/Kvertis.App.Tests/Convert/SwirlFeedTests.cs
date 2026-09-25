using Kvertis.App.Scenes;
using Kvertis.App.Tests.Scenes;
using Kvertis.App.ViewModels.Convert;
using Kvertis.Queue;
using Shouldly;
using Xunit;

namespace Kvertis.App.Tests.Convert;

/// <summary>
/// The wiring between the round and the swirl (ADR-023, worksheet "Anbindung an den Koordinator"), tested
/// against the real scene model: every command the feed sends has to leave the scene in the state the
/// worksheet describes.
/// </summary>
public class SwirlFeedTests
{
    private static SwirlJobView Job(JobState state, float fraction = 0f) => new(state, fraction);

    private static (SwirlFeed Feed, SwirlScene Scene, ManualTimeProvider Time) Plan(int files, Func<int, bool>? own = null)
    {
        var scene = SceneTestTime.NewSwirl();
        var time = new ManualTimeProvider();
        var feed = new SwirlFeed(scene, time);
        feed.SetPlan(SceneTestTime.Files(files, own));
        scene.Update(TimeSpan.Zero);
        return (feed, scene, time);
    }

    [Fact]
    public void A_running_job_takes_a_swirl_slot_only_once()
    {
        var (feed, scene, _) = Plan(3);
        feed.Apply([Job(JobState.Running, 0.1f), Job(JobState.Queued), Job(JobState.Queued)]);
        feed.Apply([Job(JobState.Running, 0.2f), Job(JobState.Queued), Job(JobState.Queued)]);
        SceneTestTime.Run(scene, 0.5);

        scene.Snapshot.FilesInSwirl.ShouldBe(1);
        var file = scene.Files.Single(f => f.Id == feed.Ids[0]);
        file.State.ShouldBe(SwirlFileState.InSwirl);
        file.Progress.ShouldBe(0.2f, 1e-5f);
        // Entered exactly once: the first Apply, not the second.
        file.EnteredAt.ShouldBe(TimeSpan.Zero);
    }

    [Fact]
    public void Progress_reaches_the_scene_and_a_pause_keeps_the_slot()
    {
        var (feed, scene, _) = Plan(1);
        feed.Apply([Job(JobState.Running, 0.5f)]);
        scene.Update(TimeSpan.Zero);
        scene.Files[0].Progress.ShouldBe(0.5f, 1e-5f);

        feed.Apply([Job(JobState.Paused, 0.5f)]);
        feed.Apply([Job(JobState.Running, 0.7f)]);
        SceneTestTime.Run(scene, SwirlScene.Flight.TotalSeconds);

        scene.Files[0].State.ShouldBe(SwirlFileState.InSwirl);
        scene.Files[0].Progress.ShouldBe(0.7f, 1e-5f);
        scene.Files[0].ColourMix.ShouldBe(0.49f, 1e-4f);
    }

    [Fact]
    public void Completed_files_land_on_the_white_hole_and_own_targets_in_the_bag()
    {
        var (feed, scene, _) = Plan(2, own: i => i == 1);
        feed.Apply([Job(JobState.Running), Job(JobState.Running)]);
        SceneTestTime.Run(scene, 1.0);
        feed.Apply([Job(JobState.Completed, 1f), Job(JobState.Completed, 1f)]);
        SceneTestTime.Run(scene, SwirlScene.Flight.TotalSeconds + 0.1);

        scene.Files[0].Target.ShouldBe(PixelTarget.WhiteHole);
        scene.Files[0].State.ShouldBe(SwirlFileState.Landed);
        scene.Files[0].Slot.ShouldBe(0);
        scene.Files[1].Target.ShouldBe(PixelTarget.Bag);
        scene.Files[1].Slot.ShouldBe(-1);
        scene.WhiteHole.N.ShouldBe(1);
    }

    [Fact]
    public void Failed_and_cancelled_files_return_to_the_stack()
    {
        var (feed, scene, _) = Plan(2);
        feed.Apply([Job(JobState.Running), Job(JobState.Running)]);
        SceneTestTime.Run(scene, 1.0);
        feed.Apply([Job(JobState.Failed), Job(JobState.Cancelled)]);
        SceneTestTime.Run(scene, SwirlScene.Flight.TotalSeconds + 0.1);

        scene.Files[0].State.ShouldBe(SwirlFileState.BackOnStack);
        scene.Files[0].Target.ShouldBe(PixelTarget.Inbox);
        scene.WhiteHole.Slots.Count.ShouldBe(1);
        scene.WhiteHole.Slots[0].Failed.ShouldBeTrue();
        scene.Files[1].State.ShouldBe(SwirlFileState.BackOnStack);
        scene.StackSheets.Count.ShouldBe(2);
    }

    [Fact]
    public void A_finished_file_is_never_sent_twice()
    {
        var (feed, scene, _) = Plan(1);
        feed.Apply([Job(JobState.Running)]);
        feed.Apply([Job(JobState.Completed, 1f)]);
        feed.Apply([Job(JobState.Completed, 1f)]);
        feed.Apply([Job(JobState.Running)]);
        SceneTestTime.Run(scene, 2.0);

        scene.Files[0].State.ShouldBe(SwirlFileState.Landed);
        scene.WhiteHole.Slots.Count.ShouldBe(1);
    }

    [Fact]
    public void The_finale_starts_1_05_s_after_the_round_finished()
    {
        var (feed, scene, time) = Plan(1);
        feed.Apply([Job(JobState.Running)]);
        feed.Apply([Job(JobState.Completed, 1f)]);
        feed.RoundFinished(completed: 1, failed: 0, cancelled: 0).ShouldBeTrue();
        feed.IsFinalePending.ShouldBeTrue();
        feed.FinaleStarted.ShouldBeFalse();

        time.Advance(TimeSpan.FromSeconds(1.0));
        scene.Update(TimeSpan.Zero);
        scene.Finale.ShouldBeNull();

        time.Advance(TimeSpan.FromSeconds(0.05));
        feed.FinaleStarted.ShouldBeTrue();
        scene.Update(TimeSpan.Zero);
        scene.Finale.ShouldNotBeNull();
        scene.Finale!.Completed.ShouldBe(1);
        scene.Snapshot.FinalePhase.ShouldBe(FinalePhase.RunUp);
    }

    [Fact]
    public void A_cancelled_round_gets_no_finale()
    {
        var (feed, scene, time) = Plan(2);
        feed.Apply([Job(JobState.Running), Job(JobState.Queued)]);
        feed.Apply([Job(JobState.Completed, 1f), Job(JobState.Cancelled)]);
        feed.RoundFinished(completed: 1, failed: 0, cancelled: 1).ShouldBeFalse();
        feed.IsFinalePending.ShouldBeFalse();

        time.Advance(TimeSpan.FromSeconds(5));
        SceneTestTime.Run(scene, 2.0);
        scene.Finale.ShouldBeNull();
    }

    [Fact]
    public void Without_an_animated_finale_no_timer_runs_and_nothing_stays_pending()
    {
        var (feed, scene, time) = Plan(1);
        feed.Apply([Job(JobState.Completed, 1f)]);
        feed.RoundFinished(1, 0, 0, animated: false).ShouldBeFalse();
        feed.IsFinalePending.ShouldBeFalse();

        time.Advance(TimeSpan.FromSeconds(5));
        scene.Update(TimeSpan.Zero);
        feed.FinaleStarted.ShouldBeFalse();
        scene.Finale.ShouldBeNull();
    }

    [Fact]
    public void Clear_drops_the_pending_finale_and_the_files()
    {
        var (feed, scene, time) = Plan(1);
        feed.Apply([Job(JobState.Completed, 1f)]);
        feed.RoundFinished(1, 0, 0);
        feed.Clear();
        time.Advance(TimeSpan.FromSeconds(2));
        scene.Update(TimeSpan.Zero);

        feed.IsFinalePending.ShouldBeFalse();
        scene.Finale.ShouldBeNull();
        scene.Files.ShouldBeEmpty();
        scene.WhiteHole.N.ShouldBe(0);
    }

    [Fact]
    public void A_new_plan_replaces_the_old_one()
    {
        var (feed, scene, _) = Plan(3);
        feed.SetPlan(SceneTestTime.Files(5));
        scene.Update(TimeSpan.Zero);

        scene.Files.Count.ShouldBe(5);
        scene.WhiteHole.N.ShouldBe(5);
        feed.Ids.Count.ShouldBe(5);
    }

    /// <summary>A clock the test moves by hand; timers fire when their due time is reached.</summary>
    private sealed class ManualTimeProvider : TimeProvider
    {
        private readonly List<ManualTimer> _timers = [];
        private TimeSpan _now = TimeSpan.Zero;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ManualTimer(this, callback, state, dueTime == Timeout.InfiniteTimeSpan ? null : _now + dueTime);
            _timers.Add(timer);
            return timer;
        }

        public void Advance(TimeSpan by)
        {
            _now += by;
            foreach (var timer in _timers.ToList())
            {
                timer.Check(_now);
            }
        }

        private sealed class ManualTimer(ManualTimeProvider owner, TimerCallback callback, object? state, TimeSpan? due) : ITimer
        {
            private TimeSpan? _due = due;

            public void Check(TimeSpan now)
            {
                if (_due is { } d && now >= d)
                {
                    _due = null;
                    callback(state);
                }
            }

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                _due = dueTime == Timeout.InfiniteTimeSpan ? null : owner._now + dueTime;
                return true;
            }

            public void Dispose()
            {
                _due = null;
                owner._timers.Remove(this);
            }

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }
}
