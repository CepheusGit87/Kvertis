using Kvertis.Engine.Abstractions;
using Kvertis.Engine.Estimation;
using Kvertis.Engine.Formats;
using Shouldly;
using Xunit;

namespace Kvertis.Engine.Tests.Estimation;

public sealed class EstimatorTests
{
    private static readonly InputInfo Audio =
        new("/in/song.wav", FormatRegistry.Wav, MediaKind.Audio, 10_000_000, TimeSpan.FromSeconds(60), null, null, null, []);

    private static readonly ConversionSettings ToMp3 = new(FormatRegistry.Mp3);

    [Fact]
    public void Invalid_entries_are_skipped_on_load()
    {
        const string json = """
            {
              "zero": { "UnitsPerSecond": 0, "SizeRatio": 1, "Samples": 3 },
              "negative": { "UnitsPerSecond": -5, "SizeRatio": 1, "Samples": 3 },
              "badRatio": { "UnitsPerSecond": 10, "SizeRatio": -1, "Samples": 3 },
              "noSamples": { "UnitsPerSecond": 10, "SizeRatio": 1, "Samples": 0 },
              "good": { "UnitsPerSecond": 10, "SizeRatio": 0.5, "Samples": 2 }
            }
            """;

        var profile = SpeedProfile.FromJson(json);

        profile.Get("zero").ShouldBeNull();
        profile.Get("negative").ShouldBeNull();
        profile.Get("badRatio").ShouldBeNull();
        profile.Get("noSamples").ShouldBeNull();
        profile.Get("good").ShouldNotBeNull();
    }

    [Fact]
    public void Loaded_zero_speed_falls_back_to_defaults()
    {
        var key = Estimator.KeyFor(Audio, ToMp3);
        var profile = SpeedProfile.FromJson($$"""{ "{{key}}": { "UnitsPerSecond": 0, "SizeRatio": 1, "Samples": 5 } }""");
        var fresh = new Estimator(new SpeedProfile(), new FormatRegistry()).Estimate(Audio, ToMp3);

        var estimate = new Estimator(profile, new FormatRegistry()).Estimate(Audio, ToMp3);

        estimate.ShouldBe(fresh);
        double.IsFinite(estimate.Duration.TotalSeconds).ShouldBeTrue();
    }

    [Fact]
    public void Record_ignores_nonsense_size_ratio()
    {
        var profile = new SpeedProfile();

        profile.Record("k", 10, double.NaN);
        profile.Record("k", 10, 0);

        profile.Get("k").ShouldBeNull();
    }
}
