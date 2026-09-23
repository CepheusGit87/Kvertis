using Kvertis.Engine.Abstractions;
using Kvertis.Engine.Conversion;
using Kvertis.Engine.Formats;
using Shouldly;
using Xunit;

namespace Kvertis.Engine.Tests.Images;

public sealed class PresetCatalogTests
{
    private const long MiB = 1024L * 1024;
    private static readonly string MaxDim = ConversionSettings.AdvancedKeys.MaxDimension;
    private static readonly string Bitrate = ConversionSettings.AdvancedKeys.AudioBitrateKbps;

    private static ConversionSettings Apply(ConversionPreset preset, MediaKind kind, FormatId? output = null) =>
        PresetCatalog.Apply(new ConversionSettings(output ?? FormatRegistry.Png, Quality: 50, Preset: preset), kind);

    [Fact]
    public void None_leaves_settings_unchanged()
    {
        var settings = new ConversionSettings(FormatRegistry.Png, Quality: 42, Metadata: MetadataPolicy.Keep);

        PresetCatalog.Apply(settings, MediaKind.Image).ShouldBeSameAs(settings);
    }

    [Theory]
    [InlineData(ConversionPreset.Messenger, "jpg", 50, 1L * 1024 * 1024, 1600)]
    [InlineData(ConversionPreset.Email, "jpg", 50, 2L * 1024 * 1024, 2048)]
    [InlineData(ConversionPreset.SocialMedia, "jpg", 85, null, 2048)]
    [InlineData(ConversionPreset.Website, "webp", 80, null, 1920)]
    public void Image_presets_match_the_table(ConversionPreset preset, string output, int quality, long? target, int maxDimension)
    {
        var settings = Apply(preset, MediaKind.Image);

        settings.Output.ShouldBe(new FormatId(output));
        settings.Quality.ShouldBe(quality);
        settings.TargetSizeBytes.ShouldBe(target);
        settings.GetAdvancedInt(MaxDim).ShouldBe(maxDimension);
        settings.Metadata.ShouldBe(MetadataPolicy.Strip);
    }

    [Fact]
    public void Archive_image_is_lossless_original_size_and_keeps_metadata()
    {
        var settings = PresetCatalog.Apply(
            new ConversionSettings(FormatRegistry.Jpg, TargetSizeBytes: 5 * MiB, Preset: ConversionPreset.Archive),
            MediaKind.Image);

        settings.Output.ShouldBe(FormatRegistry.Png);
        settings.TargetSizeBytes.ShouldBeNull();
        settings.GetAdvancedInt(MaxDim).ShouldBe(0);
        settings.GetAdvancedBool(PresetCatalog.AdvancedKeys.Lossless).ShouldBeTrue();
        settings.Metadata.ShouldBe(MetadataPolicy.Keep);
    }

    [Fact]
    public void Archive_image_keeps_tiff_when_chosen()
    {
        Apply(ConversionPreset.Archive, MediaKind.Image, FormatRegistry.Tiff).Output.ShouldBe(FormatRegistry.Tiff);
    }

    [Theory]
    [InlineData(ConversionPreset.Messenger, "m4a", 96)]
    [InlineData(ConversionPreset.Email, "mp3", 128)]
    [InlineData(ConversionPreset.SocialMedia, "mp3", 192)]
    [InlineData(ConversionPreset.Website, "opus", 96)]
    public void Audio_presets_match_the_table(ConversionPreset preset, string output, int kbps)
    {
        var settings = Apply(preset, MediaKind.Audio);

        settings.Output.ShouldBe(new FormatId(output));
        settings.GetAdvancedInt(Bitrate).ShouldBe(kbps);
        settings.Metadata.ShouldBe(MetadataPolicy.Strip);
    }

    [Fact]
    public void Archive_audio_is_flac_and_keeps_metadata()
    {
        var settings = Apply(ConversionPreset.Archive, MediaKind.Audio);

        settings.Output.ShouldBe(FormatRegistry.Flac);
        settings.GetAdvanced(Bitrate).ShouldBeNull();
        settings.Metadata.ShouldBe(MetadataPolicy.Keep);
        settings.GetAdvancedBool(PresetCatalog.AdvancedKeys.Lossless).ShouldBeTrue();
    }

    [Theory]
    [InlineData(ConversionPreset.Messenger, "mp4", 720, 16L * 1024 * 1024)]
    [InlineData(ConversionPreset.Email, "mp4", 720, 20L * 1024 * 1024)]
    [InlineData(ConversionPreset.SocialMedia, "mp4", 1080, null)]
    [InlineData(ConversionPreset.Website, "webm", 1080, null)]
    public void Video_presets_match_the_table(ConversionPreset preset, string output, int height, long? target)
    {
        var settings = Apply(preset, MediaKind.Video);

        settings.Output.ShouldBe(new FormatId(output));
        settings.GetAdvancedInt(MaxDim).ShouldBe(height);
        settings.TargetSizeBytes.ShouldBe(target);
    }

    [Fact]
    public void Archive_video_is_mkv_stream_copy_and_keeps_metadata()
    {
        var settings = Apply(ConversionPreset.Archive, MediaKind.Video);

        settings.Output.ShouldBe(FormatRegistry.Mkv);
        settings.GetAdvancedBool(PresetCatalog.AdvancedKeys.StreamCopy).ShouldBeTrue();
        settings.Metadata.ShouldBe(MetadataPolicy.Keep);
        settings.TargetSizeBytes.ShouldBeNull();
    }

    [Fact]
    public void Existing_advanced_keys_are_preserved()
    {
        var settings = new ConversionSettings(
            FormatRegistry.Png,
            Preset: ConversionPreset.Website,
            Advanced: new Dictionary<string, string> { [ConversionSettings.AdvancedKeys.Dpi] = "150", [MaxDim] = "999" });

        var applied = PresetCatalog.Apply(settings, MediaKind.Image);

        applied.GetAdvanced(ConversionSettings.AdvancedKeys.Dpi).ShouldBe("150");
        applied.GetAdvancedInt(MaxDim).ShouldBe(1920);
        settings.GetAdvancedInt(MaxDim).ShouldBe(999); // input not mutated
    }

    [Fact]
    public void Documents_only_get_the_metadata_policy()
    {
        var settings = new ConversionSettings(FormatRegistry.Txt, Preset: ConversionPreset.Archive);

        var applied = PresetCatalog.Apply(settings, MediaKind.Document);

        applied.Output.ShouldBe(FormatRegistry.Txt);
        applied.Metadata.ShouldBe(MetadataPolicy.Keep);
        PresetCatalog.Get(ConversionPreset.Email, MediaKind.Document).ShouldBeNull();
    }

    [Fact]
    public void Every_preset_covers_image_audio_and_video()
    {
        foreach (var preset in Enum.GetValues<ConversionPreset>().Where(p => p != ConversionPreset.None))
        {
            foreach (var kind in new[] { MediaKind.Image, MediaKind.Audio, MediaKind.Video })
            {
                PresetCatalog.Get(preset, kind).ShouldNotBeNull($"{preset}/{kind}");
            }
        }
    }
}
