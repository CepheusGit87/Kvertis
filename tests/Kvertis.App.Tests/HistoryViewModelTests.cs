using Kvertis.App.ViewModels;
using Kvertis.Engine.Abstractions;
using Kvertis.Engine.Formats;
using Kvertis.Queue;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Kvertis.App.Tests;

/// <summary>
/// The history overlay (docs/06-design.md, Teil E Nachtrag): day groups, filter buttons, the 150 ms search by
/// file name, the chosen entry and the actions of the detail pane. No WinUI, the clock is a fake.
/// </summary>
public sealed class HistoryViewModelTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 25, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Entries_are_grouped_by_local_day_newest_first()
    {
        var (vm, _, _) = await CreateAsync(
            Entry("today.png", FormatRegistry.Png, hoursAgo: 1),
            Entry("today2.mp3", FormatRegistry.Mp3, hoursAgo: 2),
            Entry("old.pdf", FormatRegistry.Pdf, hoursAgo: 24 * 5));

        vm.HasItems.ShouldBeTrue();
        vm.IsEmpty.ShouldBeFalse();
        vm.Groups.Count.ShouldBe(2);
        vm.Groups[0].Select(i => i.FileName).ShouldBe(["today.png", "today2.mp3"]);
        vm.Groups[1].Single().FileName.ShouldBe("old.pdf");
        vm.Groups[0].Title.ShouldBe("History_Day_Today");
        vm.Groups[0].Subtitle.ShouldNotBeEmpty();
        vm.Groups[1].Subtitle.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_first_visible_entry_is_chosen_and_marked()
    {
        var (vm, _, _) = await CreateAsync(
            Entry("a.png", FormatRegistry.Png, hoursAgo: 1),
            Entry("b.mp3", FormatRegistry.Mp3, hoursAgo: 2));

        vm.Selected.ShouldNotBeNull().FileName.ShouldBe("a.png");
        vm.Selected.IsSelected.ShouldBeTrue();

        var other = vm.Items.Single(i => i.FileName == "b.mp3");
        vm.Selected = other;

        other.IsSelected.ShouldBeTrue();
        vm.Items.Single(i => i.FileName == "a.png").IsSelected.ShouldBeFalse();
    }

    [Theory]
    [InlineData(HistoryFilter.Image, "a.png")]
    [InlineData(HistoryFilter.Audio, "b.mp3")]
    [InlineData(HistoryFilter.Video, "c.mp4")]
    [InlineData(HistoryFilter.Document, "d.pdf")]
    [InlineData(HistoryFilter.Model, "e.stl")]
    public async Task A_kind_filter_keeps_only_that_kind(HistoryFilter filter, string expected)
    {
        var (vm, _, _) = await CreateAsync(
            Entry("a.png", FormatRegistry.Png, hoursAgo: 1),
            Entry("b.mp3", FormatRegistry.Mp3, hoursAgo: 2),
            Entry("c.mp4", FormatRegistry.Mp4, hoursAgo: 3),
            Entry("d.pdf", FormatRegistry.Pdf, hoursAgo: 4),
            Entry("e.stl", FormatRegistry.Stl, hoursAgo: 5));

        vm.Filter = filter;

        vm.Groups.SelectMany(g => g).Select(i => i.FileName).ShouldBe([expected]);
        vm.Filters.Single(f => f.IsChecked).Filter.ShouldBe(filter);
        vm.MatchCountText.ShouldBe("1");
        vm.MatchSuffixText.ShouldBe("History_Match_Of(5)");
    }

    [Fact]
    public async Task The_failed_filter_keeps_only_failed_jobs()
    {
        var (vm, _, _) = await CreateAsync(
            Entry("ok.png", FormatRegistry.Png, hoursAgo: 1),
            Entry("broken.png", FormatRegistry.Png, hoursAgo: 2, success: false));

        vm.Filters.Single(f => f.Filter == HistoryFilter.Failed).IsChecked = true;

        vm.Filter.ShouldBe(HistoryFilter.Failed);
        vm.Groups.SelectMany(g => g).Single().FileName.ShouldBe("broken.png");
        vm.Selected.ShouldNotBeNull().Failed.ShouldBeTrue();
        vm.Selected.Pane.ErrorTitle.ShouldBe("Error_CorruptFile_Title");
        vm.Selected.Pane.ErrorBody.ShouldBe("Error_CorruptFile_Body");
        vm.Filters.Count(f => f.IsChecked).ShouldBe(1);
    }

    [Fact]
    public async Task The_search_waits_for_the_pause_and_matches_the_file_name_ignoring_case()
    {
        var (vm, _, time) = await CreateAsync(
            Entry("IMG_1901.png", FormatRegistry.Png, hoursAgo: 1),
            Entry("Podcast.mp3", FormatRegistry.Mp3, hoursAgo: 2),
            Entry("img_1902.png", FormatRegistry.Png, hoursAgo: 3));

        vm.SearchText = "img_19";

        // Nothing moves while the user types.
        vm.Groups.SelectMany(g => g).Count().ShouldBe(3);
        time.PendingDelay.ShouldBe(HistoryViewModel.SearchDelay);

        time.FireDueTimers();

        vm.AppliedSearch.ShouldBe("img_19");
        vm.Groups.SelectMany(g => g).Select(i => i.FileName).ShouldBe(["IMG_1901.png", "img_1902.png"]);
        vm.MatchText.ShouldBe("2 History_Match_Of(3)");
    }

    [Fact]
    public async Task Every_key_restarts_the_wait()
    {
        var (vm, _, time) = await CreateAsync(Entry("a.png", FormatRegistry.Png, hoursAgo: 1));

        vm.SearchText = "x";
        vm.SearchText = "xy";

        time.ChangeCount.ShouldBe(2);
        vm.AppliedSearch.ShouldBeEmpty();

        time.FireDueTimers();

        vm.AppliedSearch.ShouldBe("xy");
        vm.NoMatches.ShouldBeTrue();
        vm.HasSelection.ShouldBeFalse();
    }

    [Fact]
    public async Task The_search_only_looks_at_the_file_name_not_the_folder()
    {
        var (vm, _, _) = await CreateAsync(Entry("a.png", FormatRegistry.Png, hoursAgo: 1, folder: @"C:\Podcast"));

        vm.SearchText = "podcast";
        vm.ApplySearchNow();

        vm.NoMatches.ShouldBeTrue();
    }

    [Fact]
    public async Task The_chosen_entry_stays_while_visible_and_moves_to_the_first_match_otherwise()
    {
        var (vm, _, _) = await CreateAsync(
            Entry("a.png", FormatRegistry.Png, hoursAgo: 1),
            Entry("b.png", FormatRegistry.Png, hoursAgo: 2),
            Entry("c.mp3", FormatRegistry.Mp3, hoursAgo: 3));
        vm.Selected = vm.Items.Single(i => i.FileName == "b.png");

        vm.Filter = HistoryFilter.Image;
        vm.Selected.ShouldNotBeNull().FileName.ShouldBe("b.png");

        vm.Filter = HistoryFilter.Audio;
        vm.Selected.ShouldNotBeNull().FileName.ShouldBe("c.mp3");
        vm.Items.Single(i => i.FileName == "b.png").IsSelected.ShouldBeFalse();
    }

    [Fact]
    public async Task Reset_brings_back_everything()
    {
        var (vm, _, _) = await CreateAsync(
            Entry("a.png", FormatRegistry.Png, hoursAgo: 1),
            Entry("b.mp3", FormatRegistry.Mp3, hoursAgo: 2));
        vm.Filter = HistoryFilter.Video;
        vm.SearchText = "zzz";
        vm.ApplySearchNow();
        vm.NoMatches.ShouldBeTrue();

        vm.ResetFiltersCommand.Execute(null);

        vm.Filter.ShouldBe(HistoryFilter.All);
        vm.SearchText.ShouldBeEmpty();
        vm.NoMatches.ShouldBeFalse();
        vm.MatchText.ShouldBe("2 History_Match_Many");
    }

    [Fact]
    public async Task Again_picks_files_stages_them_and_closes_before_step_2()
    {
        var (vm, actions, _) = await CreateAsync(Entry("a.png", FormatRegistry.Png, hoursAgo: 1));
        vm.IsOpen = true;
        var picked = new[] { @"C:\new\b.png" };
        actions.PickFilesAsync().Returns(picked);
        var openAtNavigation = true;
        actions.When(a => a.GoToTarget()).Do(_ => openAtNavigation = vm.IsOpen);

        await vm.AgainCommand.ExecuteAsync(null);

        await actions.Received(1).StageAgainAsync(vm.Selected!.Entry, picked);
        actions.Received(1).GoToTarget();
        openAtNavigation.ShouldBeFalse();
        vm.IsOpen.ShouldBeFalse();
    }

    [Fact]
    public async Task Again_does_nothing_when_the_picker_is_cancelled()
    {
        var (vm, actions, _) = await CreateAsync(Entry("a.png", FormatRegistry.Png, hoursAgo: 1));
        vm.IsOpen = true;
        actions.PickFilesAsync().Returns(Array.Empty<string>());

        await vm.AgainCommand.ExecuteAsync(null);

        await actions.DidNotReceiveWithAnyArgs().StageAgainAsync(default!, default!);
        actions.DidNotReceive().GoToTarget();
        vm.IsOpen.ShouldBeTrue();
    }

    [Fact]
    public async Task The_folder_buttons_use_the_output_and_are_off_for_failed_jobs()
    {
        var (vm, actions, _) = await CreateAsync(
            Entry("a.png", FormatRegistry.Png, hoursAgo: 1),
            Entry("broken.png", FormatRegistry.Png, hoursAgo: 2, success: false));

        vm.OpenFolderCommand.CanExecute(null).ShouldBeTrue();
        await vm.OpenFolderCommand.ExecuteAsync(null);
        await vm.ShowInFolderCommand.ExecuteAsync(null);
        await actions.Received(1).OpenFolderAsync(@"C:\out");
        await actions.Received(1).ShowInFolderAsync(@"C:\out\a.webp");

        vm.Selected = vm.Items.Single(i => i.FileName == "broken.png");
        vm.OpenFolderCommand.CanExecute(null).ShouldBeFalse();
        vm.ShowInFolderCommand.CanExecute(null).ShouldBeFalse();
    }

    [Fact]
    public async Task The_detail_pane_carries_sizes_settings_and_place()
    {
        var (vm, _, _) = await CreateAsync(Entry("a.png", FormatRegistry.Png, hoursAgo: 1));

        var pane = vm.Selected.ShouldNotBeNull().Pane;
        pane.OutRatio.ShouldBe(0.25, 0.0001);
        pane.QualityRatio.ShouldBe(0.7, 0.0001);
        pane.Quality.ShouldBe("70");
        pane.Location.ShouldBe(@"C:\out");
        pane.InputFolder.ShouldBe(@"C:\in");
        pane.Metadata.ShouldBe("History_Metadata_Strip");
        pane.Purpose.ShouldBe("Preset_None");
        vm.Selected.SavingText.ShouldBe("History_Saving_Text(75)");
    }

    [Fact]
    public async Task Clearing_asks_first()
    {
        var (vm, actions, _) = await CreateAsync(Entry("a.png", FormatRegistry.Png, hoursAgo: 1));

        actions.ConfirmClearAsync().Returns(false);
        await vm.ClearCommand.ExecuteAsync(null);
        vm.HasItems.ShouldBeTrue();

        actions.ConfirmClearAsync().Returns(true);
        await vm.ClearCommand.ExecuteAsync(null);
        vm.HasItems.ShouldBeFalse();
        vm.IsEmpty.ShouldBeTrue();
        vm.NoMatches.ShouldBeFalse();
        vm.HasSelection.ShouldBeFalse();
        vm.ClearCommand.CanExecute(null).ShouldBeFalse();
    }

    [Fact]
    public async Task A_new_conversion_shows_up_and_keeps_the_choice()
    {
        var (vm, _, _, history) = await CreateWithHistoryAsync(
            Entry("a.png", FormatRegistry.Png, hoursAgo: 2),
            Entry("b.png", FormatRegistry.Png, hoursAgo: 3));
        vm.Selected = vm.Items.Single(i => i.FileName == "b.png");

        await history.AddAsync(Entry("new.png", FormatRegistry.Png, hoursAgo: 0));

        vm.Items.Count.ShouldBe(3);
        vm.Items[0].FileName.ShouldBe("new.png");
        vm.Selected.ShouldNotBeNull().FileName.ShouldBe("b.png");
        vm.Selected.IsSelected.ShouldBeTrue();
    }

    // ---- helpers ------------------------------------------------------------------------------------

    private static HistoryEntry Entry(string name, FormatId format, int hoursAgo, bool success = true, string folder = @"C:\in") =>
        new(
            Guid.NewGuid(),
            Now.AddHours(-hoursAgo),
            Path.Combine(folder, name),
            format,
            success ? Path.Combine(@"C:\out", Path.GetFileNameWithoutExtension(name) + ".webp") : null,
            new ConversionSettings(FormatRegistry.WebP, Quality: 70),
            success,
            success ? ConversionErrorCode.None : ConversionErrorCode.CorruptFile,
            4000,
            success ? 1000 : 0,
            TimeSpan.FromSeconds(3));

    private static async Task<(HistoryViewModel Vm, IHistoryActions Actions, FakeTime Time)> CreateAsync(params HistoryEntry[] entries)
    {
        var (vm, actions, time, _) = await CreateWithHistoryAsync(entries);
        return (vm, actions, time);
    }

    private static async Task<(HistoryViewModel Vm, IHistoryActions Actions, FakeTime Time, JobHistory History)> CreateWithHistoryAsync(
        params HistoryEntry[] entries)
    {
        var store = Substitute.For<IHistoryStore>();
        store.LoadAsync(Arg.Any<CancellationToken>()).Returns(entries);
        var history = new JobHistory(store);
        await history.LoadAsync();
        var actions = Substitute.For<IHistoryActions>();
        var time = new FakeTime(Now);
        var vm = new HistoryViewModel(history, new FormatRegistry(), TestDoubles.Localizer(), TestDoubles.Dispatcher(), actions, time);
        return (vm, actions, time, history);
    }

    /// <summary>A clock that stands at <see cref="Now"/> (UTC as local time) and timers that fire on demand.</summary>
    private sealed class FakeTime : TimeProvider
    {
        private readonly DateTimeOffset _now;
        private readonly List<FakeTimer> _timers = [];

        public FakeTime(DateTimeOffset now) => _now = now;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Local;

        public override DateTimeOffset GetUtcNow() => _now;

        /// <summary>How often a timer was (re)started; each key of the search restarts it.</summary>
        public int ChangeCount => _timers.Sum(t => t.Starts);

        public TimeSpan? PendingDelay => _timers.Select(t => t.Due).FirstOrDefault(d => d is not null);

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new FakeTimer(callback, state);
            _timers.Add(timer);
            return timer;
        }

        public void FireDueTimers()
        {
            foreach (var timer in _timers.ToList())
            {
                timer.FireIfDue();
            }
        }

        private sealed class FakeTimer : ITimer
        {
            private readonly TimerCallback _callback;
            private readonly object? _state;
            private bool _disposed;

            public FakeTimer(TimerCallback callback, object? state)
            {
                _callback = callback;
                _state = state;
            }

            public TimeSpan? Due { get; private set; }

            public int Starts { get; private set; }

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                if (dueTime == Timeout.InfiniteTimeSpan)
                {
                    Due = null;
                }
                else
                {
                    Due = dueTime;
                    Starts++;
                }
                return true;
            }

            public void FireIfDue()
            {
                if (_disposed || Due is null)
                {
                    return;
                }
                Due = null;
                _callback(_state);
            }

            public void Dispose() => _disposed = true;

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }
}
