using Kvertis.App.Services;
using Kvertis.Engine.Abstractions;
using Kvertis.Engine.Formats;
using Kvertis.Engine.Naming;
using Kvertis.Queue;
using Shouldly;
using Xunit;

namespace Kvertis.App.Tests;

/// <summary>Target path preview of step 3 (ADR-021): same arithmetic as the queue, numbered, never overwriting.</summary>
public sealed class TargetPathPlannerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);
    private static readonly FormatRegistry Registry = new();

    private static InputInfo Image(string path, MediaKind kind = MediaKind.Image) =>
        new(path, FormatRegistry.Png, kind, 1000, null, 100, 100, null, []);

    private static PlannedConversion Item(string path, FormatId? output = null, string pattern = OutputNamePattern.Default) =>
        new(Image(path), new ConversionSettings(output ?? FormatRegistry.Jpg), pattern);

    private static TargetPlan Plan(params PlannedConversion[] items) => new(items, []);

    private static IReadOnlyList<TargetPathPreview> Preview(
        TargetPlan plan,
        OutputLocation shared,
        IReadOnlyDictionary<string, OutputLocation>? own = null,
        Func<string, bool>? exists = null,
        string? temporaryRoot = null) =>
        TargetPathPlanner.PreviewAll(plan, shared, own, Registry, Now, exists ?? (_ => false), temporaryRoot);

    [Fact]
    public void Two_sources_with_the_same_stem_are_numbered_inside_the_round()
    {
        var plan = Plan(Item(@"C:\bilder\a.png"), Item(@"C:\bilder\a.bmp"));

        var previews = Preview(plan, OutputLocation.SameFolder);

        previews[0].FullPath.ShouldBe(@"C:\bilder\a.jpg");
        previews[1].FullPath.ShouldBe(@"C:\bilder\a_1.jpg");
    }

    [Fact]
    public void An_existing_file_on_disk_pushes_the_number_further()
    {
        var plan = Plan(Item(@"C:\bilder\a.png"));

        var previews = Preview(plan, OutputLocation.SameFolder,
            exists: p => p.Equals(@"C:\bilder\a.jpg", StringComparison.OrdinalIgnoreCase));

        previews[0].FullPath.ShouldBe(@"C:\bilder\a_1.jpg");
    }

    [Fact]
    public void The_own_location_of_a_file_beats_the_shared_one()
    {
        var plan = Plan(Item(@"C:\bilder\a.png"), Item(@"C:\bilder\b.png"));
        var own = new Dictionary<string, OutputLocation> { [@"C:\bilder\b.png"] = OutputLocation.Custom(@"D:\ziel") };

        var previews = Preview(plan, OutputLocation.SameFolder, own);

        previews[0].IsOwnLocation.ShouldBeFalse();
        previews[0].Directory.ShouldBe(@"C:\bilder");
        previews[1].IsOwnLocation.ShouldBeTrue();
        previews[1].Directory.ShouldBe(@"D:\ziel");
    }

    [Fact]
    public void The_sub_folder_lands_next_to_the_original()
    {
        var previews = Preview(Plan(Item(@"C:\bilder\a.png")), OutputLocation.SubFolder());

        previews[0].Directory.ShouldBe(@"C:\bilder\Kvertis");
        previews[0].FileName.ShouldBe("a.jpg");
    }

    [Fact]
    public void A_clipboard_image_needs_a_folder_when_the_target_is_relative()
    {
        var root = @"C:\cache\clipboard";
        var plan = Plan(Item(root + @"\einfuegen-1.png"));

        Preview(plan, OutputLocation.SameFolder, temporaryRoot: root)[0].NeedsFolder.ShouldBeTrue();
        Preview(plan, OutputLocation.SubFolder(), temporaryRoot: root)[0].NeedsFolder.ShouldBeTrue();
        Preview(plan, OutputLocation.Custom(@"D:\ziel"), temporaryRoot: root)[0].NeedsFolder.ShouldBeFalse();
    }

    [Fact]
    public void A_custom_location_without_a_path_needs_a_folder()
    {
        var previews = Preview(Plan(Item(@"C:\bilder\a.png")), OutputLocation.Custom(string.Empty));

        previews[0].NeedsFolder.ShouldBeTrue();
        previews[0].FullPath.ShouldBeNull();
    }

    [Fact]
    public void Special_characters_in_the_name_survive_and_stay_legal()
    {
        var plan = Plan(Item(@"C:\bilder\Grüße & Co (2) #1.png"));

        var previews = Preview(plan, OutputLocation.SameFolder);

        previews[0].FileName.ShouldBe("Grüße & Co (2) #1.jpg");
        Path.GetFileName(previews[0].FullPath!).IndexOfAny(Path.GetInvalidFileNameChars()).ShouldBeLessThan(0);
    }

    [Fact]
    public void A_name_pattern_with_tokens_uses_the_batch_index_only_for_several_files()
    {
        var single = Preview(Plan(Item(@"C:\bilder\a.png", pattern: "{name}_{n}")), OutputLocation.SameFolder);
        var many = Preview(
            Plan(Item(@"C:\bilder\a.png", pattern: "{name}_{n}"), Item(@"C:\bilder\b.png", pattern: "{name}_{n}")),
            OutputLocation.SameFolder);

        single[0].BatchIndex.ShouldBeNull();
        single[0].FileName.ShouldBe("a_.jpg");
        many[0].BatchIndex.ShouldBe(1);
        many[0].FileName.ShouldBe("a_1.jpg");
        many[1].FileName.ShouldBe("b_2.jpg");
    }

    [Fact]
    public void The_effective_location_falls_back_to_the_shared_one()
    {
        var item = Item(@"C:\bilder\a.png");

        TargetPathPlanner.EffectiveLocation(item, OutputLocation.SameFolder, null)
            .ShouldBe(OutputLocation.SameFolder);
        TargetPathPlanner
            .EffectiveLocation(item, OutputLocation.SameFolder,
                new Dictionary<string, OutputLocation> { [@"C:\bilder\a.png"] = OutputLocation.SubFolder() })
            .ShouldBe(OutputLocation.SubFolder());
    }
}
