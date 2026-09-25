using Kvertis.App.ViewModels.Drop;
using Kvertis.Engine.Abstractions;
using Kvertis.Engine.Formats;
using Shouldly;
using Xunit;

namespace Kvertis.App.Tests.Drop;

public sealed class PathsViewModelTests
{
    /// <summary>A clock that only moves when the test says so; the overlay's automation reads it.</summary>
    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }

    private static PathsViewModel Paths(out ManualClock time)
    {
        time = new ManualClock();
        return new PathsViewModel(new FormatRegistry(), null, TestDoubles.Localizer(), time);
    }

    [Fact]
    public void Opening_a_kind_lists_its_readable_and_its_writable_formats()
    {
        var registry = new FormatRegistry();
        var paths = Paths(out _);

        paths.Open(MediaKind.Image, []);

        paths.Inputs.Count.ShouldBe(registry.All.Count(d => d.Kind == MediaKind.Image && d.CanRead));
        paths.Outputs.Count.ShouldBe(registry.All.Count(
            d => d.Kind == MediaKind.Image && d.CanWrite && registry.IsProducible(d.Id, null)));
        paths.Title.ShouldBe("Kind_Image_Name");
    }

    [Fact]
    public void A_staged_format_is_marked_and_preselected()
    {
        var paths = Paths(out _);

        paths.Open(MediaKind.Image, [FormatRegistry.Heic]);

        paths.SelectedInput.ShouldNotBeNull();
        paths.SelectedInput!.Id.ShouldBe(FormatRegistry.Heic);
        paths.SelectedInput.IsStaged.ShouldBeTrue();
    }

    [Fact]
    public void The_reachable_targets_are_the_ones_the_registry_names()
    {
        var registry = new FormatRegistry();
        var paths = Paths(out _);
        paths.Open(MediaKind.Image, [FormatRegistry.Heic]);

        var expected = registry.OutputsFor(FormatRegistry.Heic)!.Select(o => o.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

        paths.Reachable.ShouldBe(expected, ignoreOrder: true);
        paths.RecommendedId.ShouldBe(FormatRegistry.Jpg.Id);
        paths.Outputs.Single(o => o.Id == FormatRegistry.Jpg).IsRecommended.ShouldBeTrue();
        paths.Outputs.Where(o => !expected.Contains(o.Id.Id)).ShouldAllBe(o => !o.IsReachable);
    }

    [Fact]
    public void An_unreachable_target_says_where_it_cannot_come_from()
    {
        var paths = Paths(out _);
        paths.Open(MediaKind.Image, [FormatRegistry.Ico]);

        var unreachable = paths.Outputs.First(o => !o.IsReachable);

        unreachable.HelpText.ShouldStartWith("Galaxy_NotReachable_Text(");
    }

    [Fact]
    public void Without_a_choice_the_left_selection_moves_on_every_2_6_seconds()
    {
        var paths = Paths(out var time);
        paths.Open(MediaKind.Audio, []);
        var first = paths.SelectedInput;

        time.Advance(TimeSpan.FromSeconds(2));
        paths.Tick().ShouldBeFalse();
        paths.SelectedInput.ShouldBe(first);

        time.Advance(TimeSpan.FromSeconds(1));
        paths.Tick().ShouldBeTrue();
        paths.SelectedInput.ShouldNotBe(first);
    }

    [Fact]
    public void A_choice_of_the_user_blocks_the_automatic_change()
    {
        var paths = Paths(out var time);
        paths.Open(MediaKind.Audio, []);

        paths.Choose(paths.Inputs[2], byClick: true);
        var chosen = paths.SelectedInput;

        time.Advance(TimeSpan.FromSeconds(7));
        paths.Tick().ShouldBeFalse();
        paths.SelectedInput.ShouldBe(chosen);

        time.Advance(TimeSpan.FromSeconds(2));
        paths.Tick().ShouldBeTrue();
    }

    [Fact]
    public void Closing_empties_both_lists()
    {
        var paths = Paths(out _);
        paths.Open(MediaKind.Video, []);

        paths.Close();

        paths.Inputs.ShouldBeEmpty();
        paths.Outputs.ShouldBeEmpty();
        paths.Reachable.ShouldBeEmpty();
        paths.RecommendedId.ShouldBeNull();
    }

    [Fact]
    public void A_staged_input_hands_its_recommendation_on_as_the_preferred_output()
    {
        var paths = Paths(out _);

        paths.Open(MediaKind.Image, [FormatRegistry.Heic]);

        paths.PreferredOutput.ShouldBe(FormatRegistry.Jpg);
    }

    [Fact]
    public void The_automatic_change_alone_names_no_preferred_output()
    {
        var paths = Paths(out var time);
        paths.Open(MediaKind.Image, []);
        paths.SelectedInput!.IsStaged.ShouldBeFalse();
        paths.PreferredOutput.ShouldBeNull();

        time.Advance(PathsViewModel.AutoInterval);
        paths.Tick().ShouldBeTrue();

        paths.PreferredOutput.ShouldBeNull();
    }

    [Fact]
    public void A_choice_of_the_user_names_the_preferred_output()
    {
        var paths = Paths(out _);
        paths.Open(MediaKind.Image, []);

        paths.Choose(paths.Inputs.Single(i => i.Id == FormatRegistry.Heic), byClick: true);

        paths.PreferredOutput.ShouldBe(FormatRegistry.Jpg);
    }
}
