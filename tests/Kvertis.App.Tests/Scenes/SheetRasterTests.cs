using Kvertis.App.Scenes;
using Kvertis.Engine.Abstractions;
using Shouldly;
using Xunit;

namespace Kvertis.App.Tests.Scenes;

public class SheetRasterTests
{
    public static TheoryData<MediaKind> AllKinds => new() { MediaKind.Image, MediaKind.Audio, MediaKind.Video, MediaKind.Document, MediaKind.Model3D };

    [Theory]
    [MemberData(nameof(AllKinds))]
    public void Raster_has_24_by_31_cells_minus_the_empty_corners(MediaKind kind)
    {
        var cells = SheetRaster.Build(kind, SceneTestData.Palette, new Random(1));

        // Three rows above the paper; only the eleven tab cells of each are kept.
        cells.Length.ShouldBe((24 * 31) - (3 * (24 - 11)));
        cells.Length.ShouldBeLessThanOrEqualTo(744);
        cells.ShouldAllBe(c => c.Gx < SheetRaster.Columns && c.Gy < SheetRaster.Rows);
        cells.ShouldAllBe(c => c.Gy > 2 || SheetRaster.IsTab(c.Gx, c.Gy));
        cells.ShouldAllBe(c => c.A0 >= 0.35f || c.A1 >= 0.35f);
        cells.Select(c => (c.Gx, c.Gy)).Distinct().Count().ShouldBe(cells.Length);
    }

    [Theory]
    [MemberData(nameof(AllKinds))]
    public void Tab_is_in_the_kind_colour_on_the_old_sheet_and_mint_on_the_new_one(MediaKind kind)
    {
        var palette = SceneTestData.Palette;
        var tab = SheetRaster.Build(kind, palette, new Random(1)).Where(c => SheetRaster.IsTab(c.Gx, c.Gy)).ToList();

        tab.Count.ShouldBe(4 * 11);
        tab.ShouldAllBe(c => c.C0 == palette.For(kind) && c.C1 == palette.Mint);
    }

    [Fact]
    public void New_sheet_has_a_mint_frame_and_the_content_differs_per_kind()
    {
        var palette = SceneTestData.Palette;
        var image = SheetRaster.Build(MediaKind.Image, palette, new Random(1));
        var frame = image.Where(c => (c.Gx == 0 || c.Gx == 23 || c.Gy == 30) && c.Gy > 3).ToList();
        frame.ShouldAllBe(c => c.C0 == palette.Paper && c.C1 == palette.Mint);

        var video = SheetRaster.Build(MediaKind.Video, palette, new Random(1));
        var middle = video.Single(c => c.Gx == 5 && c.Gy == 10);
        middle.C0.ShouldBe(SceneColor.FromHex(0x1D2226));
        image.Single(c => c.Gx == 5 && c.Gy == 10).C0.ShouldNotBe(middle.C0);

        var document = SheetRaster.Build(MediaKind.Document, palette, new Random(1));
        document.ShouldContain(c => c.C0 == palette.Document && c.C1 == palette.Mint && c.Gy >= 5 && c.Gy <= 18);
    }

    [Fact]
    public void Raster_is_deterministic_per_seed()
    {
        var a = SheetRaster.Build(MediaKind.Audio, SceneTestData.Palette, new Random(99));
        var b = SheetRaster.Build(MediaKind.Audio, SceneTestData.Palette, new Random(99));
        var c = SheetRaster.Build(MediaKind.Audio, SceneTestData.Palette, new Random(100));

        a.ShouldBe(b);
        a.SequenceEqual(c).ShouldBeFalse();
        a.ShouldAllBe(x => x.R1 >= 0f && x.R1 < 1f && x.R2 >= 0f && x.R2 < 1f && x.R3 >= 0f && x.R3 < 1f);
    }
}
