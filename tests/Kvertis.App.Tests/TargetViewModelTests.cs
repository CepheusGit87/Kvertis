using Kvertis.App.Services;
using Kvertis.App.ViewModels;
using Kvertis.App.ViewModels.Target;
using Kvertis.Engine.Abstractions;
using Kvertis.Engine.Formats;
using Kvertis.Engine.Tuning;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace Kvertis.App.Tests;

/// <summary>
/// The view models of step 2 without WinUI: what the panel produces as settings, the mode switch and the
/// takeover of a history entry.
/// </summary>
public sealed class TargetViewModelTests : IDisposable
{
    private readonly FormatRegistry _registry = new();
    private readonly List<KindGroupViewModel> _groups = [];

    private static StagedFile Png(string path, long size = 4_000_000) =>
        new(new InputInfo(path, FormatRegistry.Png, MediaKind.Image, size, null, 4000, 3000, null, []), path);

    private KindGroupViewModel Group(params StagedFile[] files)
    {
        var estimator = new TestDoubles.SizeModelEstimator(_registry);
        var resolver = TestDoubles.Resolver();
        var formats = TargetPlanner.SharedFormats(files.Select(f => f.Input).ToList(), resolver, _registry, null);
        var group = new KindGroupViewModel(
            MediaKind.Image,
            files,
            formats,
            TestDoubles.Localizer(),
            TestDoubles.Dispatcher(),
            estimator,
            _registry,
            resolver,
            null,
            NullLogger.Instance,
            "{name}",
            stripMetadata: true,
            isLocked: false,
            lockTitle: string.Empty,
            lockText: string.Empty);
        _groups.Add(group);
        WaitForTables(group);
        return group;
    }

    /// <summary>The grade tables are built off the UI thread; the tests wait for them instead of sleeping.</summary>
    private static void WaitForTables(KindGroupViewModel group)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!group.Tuning.SizeBar.HasScale && DateTime.UtcNow < deadline)
        {
            Thread.Sleep(10);
        }
        group.Tuning.SizeBar.HasScale.ShouldBeTrue("the grade tables were never built");
    }

    /// <summary>
    /// <see cref="ConversionSettings"/> is a record whose Advanced dictionary compares by reference, so the
    /// tests compare the fields plus the contents of the dictionary.
    /// </summary>
    private static void ShouldMatch(ConversionSettings actual, ConversionSettings expected)
    {
        actual.Output.ShouldBe(expected.Output);
        actual.Quality.ShouldBe(expected.Quality);
        actual.Metadata.ShouldBe(expected.Metadata);
        actual.TargetSizeBytes.ShouldBe(expected.TargetSizeBytes);
        Advanced(actual).ShouldBe(Advanced(expected));
    }

    private static IReadOnlyList<string> Advanced(ConversionSettings settings) =>
        (settings.Advanced ?? new Dictionary<string, string>())
            .OrderBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => p.Key + "=" + p.Value)
            .ToList();

    private static FormatOption Pick(KindGroupViewModel group, FormatId id) =>
        group.SharedFormats.Single(f => f.Id == id);

    // ---- SettingsFor ------------------------------------------------------------------------------

    [Fact]
    public void SettingsFor_is_the_grade_applied_to_the_chosen_output()
    {
        var group = Group(Png("a.png"));
        group.SharedFormat = Pick(group, FormatRegistry.Jpg);
        group.Tuning.Grade = 45;

        var file = group.Files[0];
        var expected = GradeMapper.Apply(
            new QualityGrade(45),
            new ConversionSettings(FormatRegistry.Jpg, Metadata: MetadataPolicy.Strip),
            file.Input,
            _registry);

        ShouldMatch(group.Tuning.SettingsFor(file), expected);
    }

    [Fact]
    public void SettingsFor_carries_the_metadata_switch()
    {
        var group = Group(Png("a.png"));
        group.Tuning.StripMetadata = false;

        group.Tuning.SettingsFor(group.Files[0]).Metadata.ShouldBe(MetadataPolicy.Keep);
    }

    [Fact]
    public void SettingsFor_carries_the_exact_target_size()
    {
        var group = Group(Png("a.png"));
        group.Tuning.ExactTargetMegabytes = 2;
        group.Tuning.ExactTargetEnabled = true;

        group.Tuning.SettingsFor(group.Files[0]).TargetSizeBytes.ShouldBe(2L * 1024 * 1024);
    }

    [Fact]
    public void SettingsFor_keeps_a_zone_that_was_moved_by_hand()
    {
        var group = Group(Png("a.png"));
        group.SharedFormat = Pick(group, FormatRegistry.Jpg);
        group.Tuning.Grade = 80;
        var sharpness = group.Tuning.Zones.Single(z => z.Aspect == TuningAspect.Sharpness);

        sharpness.Value = 20;

        var settings = group.Tuning.SettingsFor(group.Files[0]);
        var levels = GradeMapper.Aspects(settings, group.Files[0].Input, _registry);
        levels.Single(l => l.Aspect == TuningAspect.Sharpness).Value.ShouldBeLessThan(40);
        // The ring follows the mix of the zones, so it drops below the 80 it was dragged to.
        group.Tuning.Grade.ShouldBeLessThan(80);
        // The moved zone survives the rebuild that the change triggers.
        group.Tuning.Zones.Single(z => z.Aspect == TuningAspect.Sharpness).Value.ShouldBeLessThan(40);
    }

    [Fact]
    public void Moving_the_ring_couples_the_zones_again()
    {
        var group = Group(Png("a.png"));
        group.SharedFormat = Pick(group, FormatRegistry.Jpg);
        group.Tuning.Grade = 80;
        group.Tuning.Zones.Single(z => z.Aspect == TuningAspect.Sharpness).Value = 10;

        group.Tuning.Grade = 60;

        var expected = GradeMapper.Apply(
            new QualityGrade(60),
            new ConversionSettings(FormatRegistry.Jpg, Metadata: MetadataPolicy.Strip),
            group.Files[0].Input,
            _registry);
        ShouldMatch(group.Tuning.SettingsFor(group.Files[0]), expected);
    }

    [Fact]
    public void Drafts_use_the_name_pattern_of_the_panel()
    {
        var group = Group(Png("a.png"));
        group.Tuning.NamePatternText = "{name}-klein";

        group.Drafts().Single().NamePattern.ShouldBe("{name}-klein");
    }

    // ---- Mode switch ------------------------------------------------------------------------------

    [Fact]
    public void All_the_same_gives_every_file_the_group_format()
    {
        var group = Group(Png("a.png"), Png("b.png"));

        group.SharedFormat = Pick(group, FormatRegistry.WebP);

        group.Files.ShouldAllBe(f => f.EffectiveOutput == FormatRegistry.WebP);
    }

    [Fact]
    public void Each_on_its_own_lets_one_file_differ()
    {
        var group = Group(Png("a.png"), Png("b.png"));
        group.SharedFormat = Pick(group, FormatRegistry.Jpg);

        group.Mode = TargetMode.Individual;
        group.Files[1].OwnFormat = group.Files[1].Formats.Single(f => f.Id == FormatRegistry.WebP);

        group.Files[0].EffectiveOutput.ShouldBe(FormatRegistry.Jpg);
        group.Files[1].EffectiveOutput.ShouldBe(FormatRegistry.WebP);
        group.Drafts().Select(d => d.Settings.Output).ShouldBe([FormatRegistry.Jpg, FormatRegistry.WebP]);
    }

    [Fact]
    public void Back_to_all_the_same_drops_the_own_choice()
    {
        var group = Group(Png("a.png"), Png("b.png"));
        group.SharedFormat = Pick(group, FormatRegistry.Jpg);
        group.Mode = TargetMode.Individual;
        group.Files[1].OwnFormat = group.Files[1].Formats.Single(f => f.Id == FormatRegistry.WebP);

        group.Mode = TargetMode.AllSame;

        group.Files[1].EffectiveOutput.ShouldBe(FormatRegistry.Jpg);
    }

    [Fact]
    public void The_mode_switch_is_only_offered_from_two_files_on()
    {
        Group(Png("a.png")).CanChooseMode.ShouldBeFalse();
        Group(Png("a.png"), Png("b.png")).CanChooseMode.ShouldBeTrue();
    }

    // ---- "damals" ---------------------------------------------------------------------------------

    [Fact]
    public void ApplyPrevious_takes_over_format_grade_metadata_and_size()
    {
        var group = Group(Png("a.png"));
        group.SharedFormat = Pick(group, FormatRegistry.Jpg);
        group.Tuning.Grade = 90;

        var old = GradeMapper.Apply(
            new QualityGrade(35),
            new ConversionSettings(FormatRegistry.WebP, Metadata: MetadataPolicy.Keep, TargetSizeBytes: 1_048_576),
            group.Files[0].Input,
            _registry);

        group.ApplyPrevious(old);

        group.SharedFormat!.Id.ShouldBe(FormatRegistry.WebP);
        group.Tuning.StripMetadata.ShouldBeFalse();
        group.Tuning.ExactTargetEnabled.ShouldBeTrue();
        group.Tuning.ExactTargetBytes.ShouldBe(1_048_576);
        group.Tuning.Grade.ShouldBe(GradeMapper.GradeOf(old, group.Files[0].Input, _registry).Clamped);
    }

    [Fact]
    public void ApplyPrevious_keeps_the_zones_of_the_old_entry()
    {
        var group = Group(Png("a.png"));
        group.SharedFormat = Pick(group, FormatRegistry.Jpg);
        var old = GradeMapper.WithAspect(
            GradeMapper.Apply(
                new QualityGrade(80),
                new ConversionSettings(FormatRegistry.Jpg),
                group.Files[0].Input,
                _registry),
            TuningAspect.Sharpness,
            10,
            group.Files[0].Input,
            _registry);

        group.ApplyPrevious(old);

        var levels = GradeMapper.Aspects(group.Tuning.SettingsFor(group.Files[0]), group.Files[0].Input, _registry);
        levels.Single(l => l.Aspect == TuningAspect.Sharpness).Detail.Pixels
            .ShouldBe(GradeMapper.Aspects(old, group.Files[0].Input, _registry)
                .Single(l => l.Aspect == TuningAspect.Sharpness).Detail.Pixels);
    }

    [Fact]
    public void ShowPrevious_only_marks_and_changes_nothing()
    {
        var group = Group(Png("a.png"));
        group.Tuning.Grade = 70;
        var old = GradeMapper.Apply(
            new QualityGrade(20),
            new ConversionSettings(FormatRegistry.Jpg),
            group.Files[0].Input,
            _registry);

        group.ShowPrevious(old);

        group.Tuning.HasPrevious.ShouldBeTrue();
        group.Tuning.Grade.ShouldBe(70);
    }

    // ---- Zones and files --------------------------------------------------------------------------

    [Fact]
    public void A_zone_taken_over_from_the_engine_reports_nothing_back()
    {
        var reported = 0;
        var zone = new ZoneViewModel(
            TestDoubles.Localizer(),
            new AspectLevel(TuningAspect.Detail, 70, true, new AspectDetail(Quality: 79)),
            (_, _) => reported++);

        zone.Apply(new AspectLevel(TuningAspect.Detail, 40, true, new AspectDetail(Quality: 58)));

        zone.Value.ShouldBe(40);
        reported.ShouldBe(0);
    }

    [Fact]
    public void A_zone_the_user_moves_reports_once()
    {
        var reported = new List<int>();
        var zone = new ZoneViewModel(
            TestDoubles.Localizer(),
            new AspectLevel(TuningAspect.Detail, 70, true, new AspectDetail(Quality: 79)),
            (_, v) => reported.Add(v));

        zone.Value = 55;

        reported.ShouldBe([55]);
    }

    [Fact]
    public void The_target_list_mirrors_the_shared_format_both_ways()
    {
        var group = Group(Png("a.png"));

        group.TargetOptions.Count.ShouldBe(group.SharedFormats.Count);
        group.TargetOptions.Count(t => t.IsRecommended).ShouldBe(1);
        group.SelectedTarget.ShouldNotBeNull();
        group.SelectedTarget!.Option.ShouldBeSameAs(group.SharedFormat);
        group.SelectedTarget.IsSelected.ShouldBeTrue();
        group.RecommendedId.ShouldBe(group.SharedFormats.Single(f => f.IsSuggested).Id.Id);

        // Choosing a row (keyboard or click) sets the group's format.
        var other = group.TargetOptions.First(t => !t.IsSelected);
        group.SelectedTarget = other;
        group.SharedFormat.ShouldBeSameAs(other.Option);
        group.TargetOptions.Count(t => t.IsSelected).ShouldBe(1);
        other.IsSelected.ShouldBeTrue();

        // Taking over a history entry sets the format; the row follows.
        var previous = group.SharedFormats.First(f => f.IsSuggested);
        group.SharedFormat = previous;
        group.SelectedTarget!.Option.ShouldBeSameAs(previous);
    }

    [Fact]
    public void Every_row_of_the_target_list_gets_a_size_estimate()
    {
        var group = Group(Png("a.png"), Png("b.png", 2_000_000));

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (group.TargetOptions.Any(t => t.EstimatedBytes == 0) && DateTime.UtcNow < deadline)
        {
            Thread.Sleep(10);
        }

        group.TargetOptions.ShouldAllBe(t => t.EstimatedBytes > 0);
        group.TargetOptions.ShouldAllBe(t => t.SizeText.Length > 0);
        group.SelectedTarget!.EstimatedBytes.ShouldBe(group.Tuning.TotalBytes);
        group.SelectedTarget.AutomationName.ShouldContain(group.SelectedTarget.Label);
    }

    [Fact]
    public void A_fixed_zone_is_marked_as_not_adjustable()
    {
        var zone = new ZoneViewModel(
            TestDoubles.Localizer(),
            new AspectLevel(TuningAspect.Sound, 100, false, new AspectDetail(Lossless: true)),
            (_, _) => { });

        zone.Adjustable.ShouldBeFalse();
    }

    public void Dispose()
    {
        foreach (var group in _groups)
        {
            group.Dispose();
        }
    }
}
