using Kvertis.App.Services;
using Kvertis.Engine.Abstractions;
using Kvertis.Engine.Formats;
using Kvertis.Queue;
using Shouldly;
using Xunit;

namespace Kvertis.App.Tests;

/// <summary>The state that travels through the three steps (ADR-020).</summary>
public sealed class WorkflowSessionTests
{
    private static StagedFile File(string path, MediaKind kind = MediaKind.Image) =>
        new(new InputInfo(path, FormatRegistry.Png, kind, 1000, null, 100, 100, null, []), path);

    [Fact]
    public void A_fresh_session_is_empty_and_has_no_location_yet()
    {
        var session = new WorkflowSession();

        session.Staged.ShouldBeEmpty();
        session.Plan.ShouldBeNull();
        session.Previous.ShouldBeNull();
        session.Location.ShouldBeNull();
        session.OwnLocations.ShouldBeEmpty();
        session.FocusKind.ShouldBeNull();
        session.PreferredOutput.ShouldBeNull();
    }

    [Fact]
    public void Every_change_is_announced_once()
    {
        var session = new WorkflowSession();
        var changes = 0;
        session.Changed += (_, _) => changes++;

        session.SetStaged([File("a.png")]);
        session.Plan = TargetPlan.Empty;
        session.Previous = null;

        changes.ShouldBe(3);
    }

    [Fact]
    public void Rejected_files_are_kept_but_not_usable()
    {
        var rejected = File("b.png") with { RejectedWith = ConversionErrorCode.UnsupportedFormat };
        var session = new WorkflowSession();

        session.SetStaged([File("a.png"), rejected]);

        session.Staged.Count.ShouldBe(2);
        session.Staged.Count(s => s.IsUsable).ShouldBe(1);
    }

    [Fact]
    public void Unknown_files_are_not_usable_either()
    {
        File("a.bin", MediaKind.Unknown).IsUsable.ShouldBeFalse();
    }

    [Fact]
    public void Reset_clears_everything()
    {
        var session = new WorkflowSession();
        session.SetStaged([File("a.png")]);
        session.Plan = TargetPlan.Empty;
        session.Location = OutputLocation.SubFolder();
        session.SetOwnLocation("a.png", OutputLocation.Custom(@"C:\out"));
        session.FocusKind = MediaKind.Image;
        session.PreferredOutput = FormatRegistry.Jpg;

        session.Reset();

        session.Staged.ShouldBeEmpty();
        session.Plan.ShouldBeNull();
        session.Location.ShouldBeNull();
        session.OwnLocations.ShouldBeEmpty();
        session.FocusKind.ShouldBeNull();
        session.PreferredOutput.ShouldBeNull();
    }

    [Fact]
    public void An_own_location_can_be_set_and_taken_back()
    {
        var session = new WorkflowSession();

        session.SetOwnLocation("a.png", OutputLocation.SubFolder());
        session.OwnLocations["a.png"].ShouldBe(OutputLocation.SubFolder());

        session.SetOwnLocation("a.png", null);
        session.OwnLocations.ShouldBeEmpty();
        session.FocusKind.ShouldBeNull();
        session.PreferredOutput.ShouldBeNull();
    }
}
