using Kvertis.App.Scenes;
using Kvertis.Engine.Abstractions;
using Shouldly;
using Xunit;

namespace Kvertis.App.Tests.Scenes;

public class GalaxyLayoutTests
{
    [Fact]
    public void Orbits_run_from_documents_inside_to_images_outside()
    {
        GalaxyLayout.OrbitOrder.ShouldBe(
        [
            MediaKind.Document,
            MediaKind.Model3D,
            MediaKind.Video,
            MediaKind.Audio,
            MediaKind.Image,
        ]);

        GalaxyLayout.OrbitOf(MediaKind.Document).ShouldBe(0);
        GalaxyLayout.OrbitOf(MediaKind.Model3D).ShouldBe(1);
        GalaxyLayout.OrbitOf(MediaKind.Video).ShouldBe(2);
        GalaxyLayout.OrbitOf(MediaKind.Audio).ShouldBe(3);
        GalaxyLayout.OrbitOf(MediaKind.Image).ShouldBe(4);
        GalaxyLayout.KindOf(4).ShouldBe(MediaKind.Image);
    }

    [Fact]
    public void Unknown_kind_has_no_orbit()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => _ = GalaxyLayout.OrbitOf(MediaKind.Unknown));
    }

    [Fact]
    public void Radii_are_92_to_316_at_the_reference_size()
    {
        var layout = new GalaxyLayout(700f, 280f);

        layout.Scale.ShouldBe(1f, 0.0001f);
        layout.BaseRadius(0).ShouldBe(92f, 0.0001f);
        layout.BaseRadius(1).ShouldBe(148f, 0.0001f);
        layout.BaseRadius(2).ShouldBe(204f, 0.0001f);
        layout.BaseRadius(3).ShouldBe(260f, 0.0001f);
        layout.BaseRadius(4).ShouldBe(316f, 0.0001f);
    }

    [Fact]
    public void Centre_entry_and_scale_follow_the_surface()
    {
        var small = new GalaxyLayout(350f, 140f);
        small.Scale.ShouldBe(0.6f, 0.0001f);

        var large = new GalaxyLayout(2800f, 1400f);
        large.Scale.ShouldBe(1.5f, 0.0001f);

        var layout = new GalaxyLayout(700f, 280f);
        layout.Center.X.ShouldBe(350f, 0.0001f);
        layout.Center.Y.ShouldBe(150f, 0.0001f);
        layout.Entry.X.ShouldBe(350f, 0.0001f);
        layout.Entry.Y.ShouldBe(24f, 0.0001f);
    }

    [Fact]
    public void Zoom_radius_never_falls_below_120()
    {
        // Scale stays 1 for all three, so only the width decides.
        new GalaxyLayout(700f, 280f).ZoomRadius.ShouldBe(120f, 0.0001f);
        new GalaxyLayout(1000f, 280f).ZoomRadius.ShouldBe(200f, 0.0001f);
        new GalaxyLayout(1500f, 280f).ZoomRadius.ShouldBe(240f, 0.0001f);
    }
}
