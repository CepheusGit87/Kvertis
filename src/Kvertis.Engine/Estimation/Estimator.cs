using System.Text.Json;
using System.Text.Json.Serialization;
using Kvertis.Engine.Abstractions;
using Kvertis.Engine.Formats;

namespace Kvertis.Engine.Estimation;

/// <summary>
/// Measured throughput per (converter, output format) pair, refined with every completed job by an
/// exponential moving average. Persisted as JSON by the app; the engine only holds it in memory.
/// </summary>
public sealed partial class SpeedProfile
{
    private const double Alpha = 0.3;
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    public sealed record Entry(double UnitsPerSecond, double SizeRatio, int Samples);

    public Entry? Get(string key)
    {
        lock (_gate)
        {
            return _entries.GetValueOrDefault(key);
        }
    }

    public void Record(string key, double unitsPerSecond, double sizeRatio)
    {
        if (unitsPerSecond <= 0 || double.IsNaN(unitsPerSecond) || double.IsInfinity(unitsPerSecond))
        {
            return;
        }
        lock (_gate)
        {
            if (_entries.TryGetValue(key, out var e))
            {
                _entries[key] = new Entry(
                    e.UnitsPerSecond + Alpha * (unitsPerSecond - e.UnitsPerSecond),
                    e.SizeRatio + Alpha * (sizeRatio - e.SizeRatio),
                    e.Samples + 1);
            }
            else
            {
                _entries[key] = new Entry(unitsPerSecond, sizeRatio, 1);
            }
        }
    }

    public string ToJson()
    {
        lock (_gate)
        {
            return JsonSerializer.Serialize(_entries, JsonContext.Default.DictionaryStringEntry);
        }
    }

    public static SpeedProfile FromJson(string? json)
    {
        var profile = new SpeedProfile();
        if (string.IsNullOrWhiteSpace(json))
        {
            return profile;
        }
        try
        {
            var data = JsonSerializer.Deserialize(json, JsonContext.Default.DictionaryStringEntry);
            if (data is not null)
            {
                foreach (var (k, v) in data)
                {
                    profile._entries[k] = v;
                }
            }
        }
        catch (JsonException)
        {
            // A corrupt profile just means we start estimating from defaults again.
        }
        return profile;
    }

    [JsonSourceGenerationOptions(WriteIndented = false)]
    [JsonSerializable(typeof(Dictionary<string, Entry>))]
    private sealed partial class JsonContext : JsonSerializerContext
    {
    }
}

/// <summary>
/// Time and size estimation. Units: seconds of media for audio/video, megapixels for images,
/// megabytes for documents. Before any measurement, conservative defaults apply with low confidence.
/// </summary>
public sealed class Estimator : IEstimator
{
    private readonly SpeedProfile _profile;
    private readonly FormatRegistry _registry;

    public Estimator(SpeedProfile profile, FormatRegistry registry)
    {
        _profile = profile;
        _registry = registry;
    }

    public Estimate Estimate(InputInfo input, ConversionSettings settings)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(settings);

        var units = UnitsOf(input);
        var key = KeyFor(input, settings);
        var entry = _profile.Get(key);

        double unitsPerSecond;
        double sizeRatio;
        double confidence;
        if (entry is not null)
        {
            unitsPerSecond = entry.UnitsPerSecond;
            sizeRatio = entry.SizeRatio;
            confidence = Math.Min(0.95, 0.5 + 0.1 * entry.Samples);
        }
        else
        {
            (unitsPerSecond, sizeRatio) = Defaults(input, settings);
            confidence = 0.3;
        }

        var seconds = units / unitsPerSecond + 0.3; // process start overhead
        var outputBytes = settings.TargetSizeBytes is { } target
            ? Math.Min(target, (long)(input.SizeBytes * sizeRatio))
            : (long)(input.SizeBytes * sizeRatio);
        return new Estimate(TimeSpan.FromSeconds(seconds), Math.Max(outputBytes, 1), confidence);
    }

    public void Record(InputInfo input, ConversionSettings settings, ConversionResult result)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(result);

        var seconds = Math.Max(result.Elapsed.TotalSeconds - 0.3, 0.05);
        _profile.Record(KeyFor(input, settings), UnitsOf(input) / seconds, result.SizeRatio);
    }

    public static string KeyFor(InputInfo input, ConversionSettings settings) =>
        $"{input.Kind}:{input.Format}>{settings.Output}";

    private static double UnitsOf(InputInfo input) => input.Kind switch
    {
        MediaKind.Audio or MediaKind.Video when input.Duration is { } d => Math.Max(d.TotalSeconds, 0.1),
        MediaKind.Audio => input.SizeBytes / (16_000.0 * 8), // assume ~128 kbit/s when duration unknown
        MediaKind.Video => input.SizeBytes / (500_000.0), // assume ~4 Mbit/s
        MediaKind.Image when input is { Width: { } w, Height: { } h } => Math.Max(w * (double)h / 1_000_000, 0.05),
        MediaKind.Image => Math.Max(input.SizeBytes / 300_000.0, 0.05), // ~0.3 MB per megapixel for JPG
        _ => Math.Max(input.SizeBytes / 1_048_576.0, 0.05),
    };

    private (double UnitsPerSecond, double SizeRatio) Defaults(InputInfo input, ConversionSettings settings)
    {
        var lossless = _registry.Get(settings.Output)?.Lossless == true;
        return input.Kind switch
        {
            MediaKind.Image => (8.0, lossless ? 1.5 : 0.5),
            MediaKind.Audio => (60.0, lossless ? 6.0 : 1.0),
            MediaKind.Video => (1.0, 0.7),
            _ => (10.0, 0.8),
        };
    }
}
