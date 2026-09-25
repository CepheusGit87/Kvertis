using Kvertis.App.Services;
using Kvertis.Engine.Abstractions;
using Kvertis.Engine.Formats;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Kvertis.App.Tests;

/// <summary>The logic of step 2 that carries decisions: kind order, shared formats, plan and bar scale.</summary>
public sealed class TargetPlannerTests
{
    private readonly FormatRegistry _registry = new();

    private static InputInfo Image(string path, FormatId format, int width = 4000, int height = 3000) =>
        new(path, format, MediaKind.Image, 4_000_000, null, width, height, null, []);

    private static InputInfo Video(string path) =>
        new(path, FormatRegistry.Mp4, MediaKind.Video, 40_000_000, TimeSpan.FromMinutes(1), 1920, 1080, null, []);

    private IConverterResolver Everything()
    {
        var resolver = Substitute.For<IConverterResolver>();
        resolver.CanConvert(Arg.Any<InputInfo>(), Arg.Any<FormatId>()).Returns(true);
        return resolver;
    }

    [Fact]
    public void OrderKinds_follows_the_display_order_of_step_two()
    {
        var order = TargetPlanner.OrderKinds([MediaKind.Document, MediaKind.Video, MediaKind.Image]);

        order.ShouldBe([MediaKind.Image, MediaKind.Video, MediaKind.Document]);
    }

    [Fact]
    public void OrderKinds_lists_every_kind_once()
    {
        var order = TargetPlanner.OrderKinds([MediaKind.Image, MediaKind.Image, MediaKind.Audio]);

        order.ShouldBe([MediaKind.Image, MediaKind.Audio]);
    }

    [Fact]
    public void SharedFormats_marks_the_suggestion_of_the_first_file()
    {
        var formats = TargetPlanner.SharedFormats([Image("a.png", FormatRegistry.Png)], Everything(), _registry, null);

        formats.ShouldNotBeEmpty();
        formats.Count(f => f.IsSuggested).ShouldBe(1);
    }

    [Fact]
    public void SharedFormats_is_the_intersection_over_all_files_of_the_kind()
    {
        var png = Image("a.png", FormatRegistry.Png);
        var webp = Image("b.webp", FormatRegistry.WebP);

        var both = TargetPlanner.SharedFormats([png, webp], Everything(), _registry, null).Select(f => f.Id).ToList();
        var onlyPng = TargetPlanner.SharedFormats([png], Everything(), _registry, null).Select(f => f.Id).ToList();

        // PNG can become an ICO, WebP cannot; the shared list must not offer it.
        onlyPng.ShouldContain(FormatRegistry.Ico);
        both.ShouldNotContain(FormatRegistry.Ico);
        both.ShouldAllBe(id => onlyPng.Contains(id));
    }

    [Fact]
    public void SharedFormats_drops_what_the_resolver_refuses_for_one_file()
    {
        var a = Image("a.png", FormatRegistry.Png);
        var b = Image("b.png", FormatRegistry.Png);
        var resolver = Substitute.For<IConverterResolver>();
        resolver.CanConvert(Arg.Any<InputInfo>(), Arg.Any<FormatId>()).Returns(true);
        resolver.CanConvert(b, FormatRegistry.Jpg).Returns(false);

        var formats = TargetPlanner.SharedFormats([a, b], resolver, _registry, null).Select(f => f.Id).ToList();

        formats.ShouldNotContain(FormatRegistry.Jpg);
    }

    [Fact]
    public void SharedFormats_is_empty_without_a_common_output()
    {
        var a = Image("a.png", FormatRegistry.Png);
        var resolver = Substitute.For<IConverterResolver>();
        resolver.CanConvert(Arg.Any<InputInfo>(), Arg.Any<FormatId>()).Returns(false);

        TargetPlanner.SharedFormats([a], resolver, _registry, null).ShouldBeEmpty();
    }

    [Fact]
    public void BuildPlan_keeps_every_file_without_limits()
    {
        var drafts = Drafts(3);

        var plan = TargetPlanner.BuildPlan(drafts, TargetLimits.None);

        plan.Items.Count.ShouldBe(3);
        plan.Skipped.ShouldBeEmpty();
    }

    [Fact]
    public void BuildPlan_skips_video_while_the_kind_is_locked()
    {
        var drafts = new List<PlanDraft>
        {
            new(Image("a.png", FormatRegistry.Png), new ConversionSettings(FormatRegistry.Jpg), "{name}"),
            new(Video("v.mp4"), new ConversionSettings(FormatRegistry.Mp4), "{name}"),
        };

        var plan = TargetPlanner.BuildPlan(drafts, new TargetLimits(VideoLocked: true, BatchLimit: null));

        plan.Items.Count.ShouldBe(1);
        plan.Skipped.Single().Reason.ShouldBe(TargetPlanner.ReasonVideo);
    }

    [Fact]
    public void BuildPlan_takes_the_first_files_up_to_the_batch_limit()
    {
        var plan = TargetPlanner.BuildPlan(Drafts(8), new TargetLimits(VideoLocked: false, BatchLimit: 5));

        plan.Items.Count.ShouldBe(5);
        plan.Items[0].Input.Path.ShouldBe("f0.png");
        plan.Skipped.Count.ShouldBe(3);
        plan.Skipped.ShouldAllBe(s => s.Reason == TargetPlanner.ReasonBatchSize);
    }

    [Fact]
    public void BuildPlan_does_not_let_locked_files_use_up_the_batch_limit()
    {
        var drafts = new List<PlanDraft> { new(Video("v.mp4"), new ConversionSettings(FormatRegistry.Mp4), "{name}") };
        drafts.AddRange(Drafts(5));

        var plan = TargetPlanner.BuildPlan(drafts, new TargetLimits(VideoLocked: true, BatchLimit: 5));

        plan.Items.Count.ShouldBe(5);
        plan.Skipped.Single().Reason.ShouldBe(TargetPlanner.ReasonVideo);
    }

    [Fact]
    public void PreviousFits_recognises_a_format_the_new_files_cannot_produce()
    {
        var formats = TargetPlanner.SharedFormats([Image("a.png", FormatRegistry.Png)], Everything(), _registry, null);

        TargetPlanner.PreviousFits(formats, FormatRegistry.Jpg).ShouldBeTrue();
        TargetPlanner.PreviousFits(formats, FormatRegistry.Mp4).ShouldBeFalse();
    }

    [Theory]
    [InlineData(100_000L)]
    [InlineData(1_000_000L)]
    [InlineData(9_000_000L)]
    public void Bar_scale_is_its_own_inverse(long bytes)
    {
        const long min = 50_000;
        const long max = 10_000_000;

        var back = TargetPlanner.BytesAt(TargetPlanner.PositionOf(bytes, min, max), min, max);

        Math.Abs(back - bytes).ShouldBeLessThanOrEqualTo(bytes / 100);
    }

    [Fact]
    public void Bar_scale_clamps_outside_the_range()
    {
        TargetPlanner.PositionOf(10, 1000, 100_000).ShouldBe(0);
        TargetPlanner.PositionOf(long.MaxValue, 1000, 100_000).ShouldBe(TargetPlanner.BarSteps);
    }

    [Fact]
    public void Bar_scale_survives_a_degenerate_table()
    {
        TargetPlanner.BytesAt(500, 0, 0).ShouldBeGreaterThan(0);
    }

    [Fact]
    public void ColorKey_gives_every_kind_its_own_token()
    {
        var keys = TargetPlanner.KindOrder.Select(TargetPlanner.ColorKey).ToList();

        keys.Distinct().Count().ShouldBe(keys.Count);
    }

    private static List<PlanDraft> Drafts(int count) =>
        Enumerable.Range(0, count)
            .Select(i => new PlanDraft(
                Image($"f{i}.png", FormatRegistry.Png),
                new ConversionSettings(FormatRegistry.Jpg),
                "{name}"))
            .ToList();
}
