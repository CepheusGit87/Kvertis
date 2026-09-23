using Kvertis.Engine.Abstractions;
using Kvertis.Engine.Formats;

namespace Kvertis.Engine.Conversion;

/// <summary>What a preset prescribes for one media kind. Null members leave the user's value untouched.</summary>
public sealed record PresetDefinition(
    FormatId? Output,
    int? Quality = null,
    long? TargetSizeBytes = null,
    int? MaxDimension = null,
    int? AudioBitrateKbps = null,
    MetadataPolicy Metadata = MetadataPolicy.Strip,
    bool Lossless = false,
    bool StreamCopy = false);

/// <summary>
/// The preset table from docs/05-formate.md (section "Presets") as pure data. <see cref="Apply"/> fills
/// output format, quality, target size, metadata policy and the advanced keys converters read
/// (maxDimension = longest edge for images / height for video, audioBitrateKbps, lossless, streamCopy).
/// </summary>
public static class PresetCatalog
{
    /// <summary>Advanced keys that only presets set. Converters may read them as hints.</summary>
    public static class AdvancedKeys
    {
        /// <summary>"true": prefer a lossless encoding (PNG compression level 9, WebP lossless).</summary>
        public const string Lossless = "lossless";
        /// <summary>"true": copy audio/video streams without re-encoding when the container allows it.</summary>
        public const string StreamCopy = "streamCopy";
    }

    private const long MiB = 1024L * 1024;

    private static readonly Dictionary<(ConversionPreset, MediaKind), PresetDefinition> Table = new()
    {
        [(ConversionPreset.Messenger, MediaKind.Image)] = new(FormatRegistry.Jpg, TargetSizeBytes: 1 * MiB, MaxDimension: 1600),
        [(ConversionPreset.Messenger, MediaKind.Audio)] = new(FormatRegistry.M4a, AudioBitrateKbps: 96),
        [(ConversionPreset.Messenger, MediaKind.Video)] = new(FormatRegistry.Mp4, TargetSizeBytes: 16 * MiB, MaxDimension: 720),

        [(ConversionPreset.Email, MediaKind.Image)] = new(FormatRegistry.Jpg, TargetSizeBytes: 2 * MiB, MaxDimension: 2048),
        [(ConversionPreset.Email, MediaKind.Audio)] = new(FormatRegistry.Mp3, AudioBitrateKbps: 128),
        [(ConversionPreset.Email, MediaKind.Video)] = new(FormatRegistry.Mp4, TargetSizeBytes: 20 * MiB, MaxDimension: 720),

        [(ConversionPreset.SocialMedia, MediaKind.Image)] = new(FormatRegistry.Jpg, Quality: 85, MaxDimension: 2048),
        [(ConversionPreset.SocialMedia, MediaKind.Audio)] = new(FormatRegistry.Mp3, AudioBitrateKbps: 192),
        [(ConversionPreset.SocialMedia, MediaKind.Video)] = new(FormatRegistry.Mp4, MaxDimension: 1080),

        [(ConversionPreset.Website, MediaKind.Image)] = new(FormatRegistry.WebP, Quality: 80, MaxDimension: 1920),
        [(ConversionPreset.Website, MediaKind.Audio)] = new(FormatRegistry.Opus, AudioBitrateKbps: 96),
        [(ConversionPreset.Website, MediaKind.Video)] = new(FormatRegistry.WebM, MaxDimension: 1080),

        [(ConversionPreset.Archive, MediaKind.Image)] = new(FormatRegistry.Png, Quality: 100, MaxDimension: 0, Metadata: MetadataPolicy.Keep, Lossless: true),
        [(ConversionPreset.Archive, MediaKind.Audio)] = new(FormatRegistry.Flac, Metadata: MetadataPolicy.Keep, Lossless: true),
        [(ConversionPreset.Archive, MediaKind.Video)] = new(FormatRegistry.Mkv, MaxDimension: 0, Metadata: MetadataPolicy.Keep, Lossless: true, StreamCopy: true),
    };

    /// <summary>The definition for a preset and media kind, or null (no preset, or kind not covered, e.g. documents).</summary>
    public static PresetDefinition? Get(ConversionPreset preset, MediaKind kind) =>
        Table.GetValueOrDefault((preset, kind));

    /// <summary>
    /// Returns <paramref name="settings"/> with the preset's values filled in. Without a preset, or for a
    /// kind the preset does not cover, only the metadata policy of the preset is applied (Archive keeps).
    /// Archive keeps an already chosen lossless image output (TIFF) instead of forcing PNG.
    /// </summary>
    public static ConversionSettings Apply(ConversionSettings settings, MediaKind kind)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.Preset == ConversionPreset.None)
        {
            return settings;
        }

        var definition = Get(settings.Preset, kind);
        if (definition is null)
        {
            return settings with
            {
                Metadata = settings.Preset == ConversionPreset.Archive ? MetadataPolicy.Keep : MetadataPolicy.Strip,
            };
        }

        var output = definition.Output ?? settings.Output;
        if (settings.Preset == ConversionPreset.Archive && kind == MediaKind.Image && settings.Output == FormatRegistry.Tiff)
        {
            output = FormatRegistry.Tiff;
        }

        var advanced = settings.Advanced is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(settings.Advanced, StringComparer.Ordinal);
        if (definition.MaxDimension is { } maxDimension)
        {
            advanced[ConversionSettings.AdvancedKeys.MaxDimension] = maxDimension.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        if (definition.AudioBitrateKbps is { } bitrate)
        {
            advanced[ConversionSettings.AdvancedKeys.AudioBitrateKbps] = bitrate.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        if (definition.Lossless)
        {
            advanced[AdvancedKeys.Lossless] = "true";
        }
        if (definition.StreamCopy)
        {
            advanced[AdvancedKeys.StreamCopy] = "true";
        }

        return settings with
        {
            Output = output,
            Quality = definition.Quality ?? settings.Quality,
            // Lossless presets never carry a target size; the others replace it only when they define one.
            TargetSizeBytes = definition.Lossless ? null : definition.TargetSizeBytes ?? settings.TargetSizeBytes,
            Metadata = definition.Metadata,
            Advanced = advanced,
        };
    }
}
