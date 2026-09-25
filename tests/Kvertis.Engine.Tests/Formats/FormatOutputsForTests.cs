using Kvertis.Engine.Abstractions;
using Kvertis.Engine.Formats;
using Kvertis.Engine.Platform;
using Shouldly;
using Xunit;

namespace Kvertis.Engine.Tests.Formats;

/// <summary>
/// <see cref="FormatRegistry.OutputsFor"/>: the same matrix as <c>Suggest</c> without an
/// <see cref="InputInfo"/>, for views that only know a format id (ADR-022).
/// </summary>
public class FormatOutputsForTests
{
    private static readonly FormatRegistry Registry = new();

    [Fact]
    public void EveryReadableFormatEitherHasOutputsOrNone()
    {
        foreach (var descriptor in Registry.All.Where(d => d.CanRead))
        {
            var outputs = Registry.OutputsFor(descriptor.Id, AllSystemCodecCapabilities.Instance);
            if (outputs is not null)
            {
                outputs.ShouldNotBeEmpty();
                outputs.ShouldAllBe(o => Registry.IsProducible(o, AllSystemCodecCapabilities.Instance));
            }
        }
    }

    [Fact]
    public void ItReturnsTheSameOptionsAsSuggest()
    {
        var input = new InputInfo("/in/photo.heic", FormatRegistry.Heic, MediaKind.Image, 1000, null, 100, 100, null, []);

        var suggestion = Registry.Suggest(input, AllSystemCodecCapabilities.Instance);
        var outputs = Registry.OutputsFor(FormatRegistry.Heic, AllSystemCodecCapabilities.Instance);

        suggestion.ShouldNotBeNull();
        outputs.ShouldNotBeNull();
        outputs.ShouldBe(suggestion.Options);
    }

    [Fact]
    public void AnUnknownFormatHasNoOutputs()
    {
        Registry.OutputsFor(new FormatId("nonesuch")).ShouldBeNull();
    }

    [Fact]
    public void MissingSystemCodecsRemoveTheirOutputs()
    {
        var withCodecs = Registry.OutputsFor(FormatRegistry.Mov, AllSystemCodecCapabilities.Instance);
        var withoutCodecs = Registry.OutputsFor(FormatRegistry.Mov, NullSystemCodecCapabilities.Instance);

        withCodecs.ShouldNotBeNull();
        withCodecs.ShouldContain(FormatRegistry.Mp4);
        withoutCodecs?.ShouldNotContain(FormatRegistry.Mp4);
    }
}
