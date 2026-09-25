using Kvertis.App.ViewModels.Drop;
using Kvertis.Engine.Abstractions;
using Shouldly;
using Xunit;

namespace Kvertis.App.Tests.Drop;

public sealed class RejectedListViewModelTests
{
    private static RejectedListViewModel List() => new(TestDoubles.Localizer());

    private static FakeTrayFile Rejected() => new(MediaKind.Unknown, IsReady: false, IsRejected: true);

    [Fact]
    public void Nothing_rejected_means_no_card()
    {
        var list = List();

        list.Sync([new FakeTrayFile(MediaKind.Image)]);

        list.HasAny.ShouldBeFalse();
        list.Visible.ShouldBeEmpty();
        list.HasMore.ShouldBeFalse();
    }

    [Fact]
    public void At_most_three_rows_are_shown_and_the_rest_is_counted()
    {
        var list = List();

        list.Sync([Rejected(), Rejected(), Rejected(), Rejected(), Rejected()]);

        list.HasAny.ShouldBeTrue();
        list.Visible.Count.ShouldBe(RejectedListViewModel.CollapsedLimit);
        list.HiddenCount.ShouldBe(2);
        list.MoreText.ShouldBe("Galaxy_Rejected_More_Text(2)");
    }

    [Fact]
    public void Unfolding_shows_every_row()
    {
        var list = List();
        list.Sync([Rejected(), Rejected(), Rejected(), Rejected()]);

        list.ToggleExpandCommand.Execute(null);

        list.Visible.Count.ShouldBe(4);
        list.HasMore.ShouldBeFalse();
    }

    [Fact]
    public void An_empty_list_folds_itself_again()
    {
        var list = List();
        list.Sync([Rejected(), Rejected(), Rejected(), Rejected()]);
        list.ToggleExpandCommand.Execute(null);

        list.Sync([]);

        list.IsExpanded.ShouldBeFalse();
        list.HasAny.ShouldBeFalse();
    }
}
