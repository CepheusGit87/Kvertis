using Kvertis.App.ViewModels.Drop;
using Kvertis.Engine.Abstractions;
using Kvertis.Engine.Formats;
using Shouldly;
using Xunit;

namespace Kvertis.App.Tests.Drop;

/// <summary>A staged file as the trays see it (ADR-022): no WinUI, only kind and state.</summary>
internal sealed record FakeTrayFile(MediaKind Kind, bool IsReady = true, bool IsRejected = false) : ITrayFile
{
    public Guid Id { get; } = Guid.NewGuid();
}

public sealed class TrayViewModelTests
{
    private static TrayViewModel Tray(MediaKind kind) =>
        new(kind, new FormatRegistry(), null, TestDoubles.Localizer());

    [Fact]
    public void Empty_tray_shows_up_to_five_example_formats()
    {
        var tray = Tray(MediaKind.Image);

        tray.IsEmpty.ShouldBeTrue();
        tray.Count.ShouldBe(0);
        tray.ExampleFormats.Count.ShouldBe(TrayViewModel.ExampleLimit);
        tray.ExampleText.ShouldContain("JPG");
    }

    [Fact]
    public void Foot_line_counts_readable_and_writable_formats_of_the_kind()
    {
        var registry = new FormatRegistry();
        var tray = Tray(MediaKind.Image);

        var readable = registry.All.Count(d => d.Kind == MediaKind.Image && d.CanRead);
        var writable = registry.All.Count(d => d.Kind == MediaKind.Image && d.CanWrite && registry.IsProducible(d.Id, null));

        tray.ReadableCount.ShouldBe(readable);
        tray.WritableCount.ShouldBe(writable);
        tray.FootText.ShouldBe($"Tray_Foot_Text({readable},{writable})");
    }

    [Fact]
    public void Sync_keeps_only_the_files_of_its_own_kind()
    {
        var tray = Tray(MediaKind.Audio);
        var image = new FakeTrayFile(MediaKind.Image);
        var audio = new FakeTrayFile(MediaKind.Audio);
        var otherAudio = new FakeTrayFile(MediaKind.Audio);

        tray.Sync([image, audio, otherAudio]);

        tray.Files.ShouldBe([audio, otherAudio]);
        tray.Count.ShouldBe(2);
        tray.IsEmpty.ShouldBeFalse();
        tray.AutomationName.ShouldBe("Tray_AutomationName(Kind_Audio_Name,2)");
    }

    [Fact]
    public void Rejected_files_never_enter_a_tray()
    {
        var tray = Tray(MediaKind.Image);

        tray.Sync([new FakeTrayFile(MediaKind.Image, IsReady: false, IsRejected: true)]);

        tray.Count.ShouldBe(0);
    }

    [Fact]
    public void Sync_removes_what_is_gone_and_keeps_the_order()
    {
        var tray = Tray(MediaKind.Document);
        var a = new FakeTrayFile(MediaKind.Document);
        var b = new FakeTrayFile(MediaKind.Document);
        var c = new FakeTrayFile(MediaKind.Document);
        tray.Sync([a, b, c]);

        tray.Sync([a, c]);

        tray.Files.ShouldBe([a, c]);
    }

    [Theory]
    [InlineData(MediaKind.Image, "KvImageBrush")]
    [InlineData(MediaKind.Audio, "KvAudioBrush")]
    [InlineData(MediaKind.Video, "KvVideoBrush")]
    [InlineData(MediaKind.Document, "KvDocumentBrush")]
    [InlineData(MediaKind.Model3D, "KvModelBrush")]
    [InlineData(MediaKind.Unknown, "KvErrorBrush")]
    public void Every_kind_has_its_own_colour_token(MediaKind kind, string expected) =>
        TrayViewModel.BrushKeyOf(kind).ShouldBe(expected);
}
