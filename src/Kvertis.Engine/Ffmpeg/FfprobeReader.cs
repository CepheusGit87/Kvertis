using System.Globalization;
using System.Text.Json;
using Kvertis.Engine.Abstractions;
using Kvertis.Engine.Validation;

namespace Kvertis.Engine.Ffmpeg;

/// <summary>What ffprobe told us about an audio/video file. Only the first video and first audio stream count.</summary>
public sealed record MediaInfo(
    TimeSpan? Duration,
    int? Width,
    int? Height,
    string? VideoCodec,
    string? AudioCodec,
    bool HasVideo,
    bool HasAudio,
    int AudioTrackCount,
    bool IsVariableFrameRate,
    bool IsInterlaced,
    bool IsEncrypted,
    double? FrameRate = null,
    int? VideoBitrateKbps = null,
    int? AudioBitrateKbps = null,
    int? SampleRateHz = null,
    string? ContainerName = null)
{
    public bool IsHevc => string.Equals(VideoCodec, "hevc", StringComparison.OrdinalIgnoreCase);
    public bool IsH264 => string.Equals(VideoCodec, "h264", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Runs <c>ffprobe -v error -print_format json -show_format -show_streams &lt;file&gt;</c> through
/// <see cref="IProcessRunner"/> and parses the JSON. Never loads any FFmpeg library.
/// </summary>
public sealed class FfprobeReader
{
    /// <summary>Codec tags used by protected MP4/MOV tracks (CENC and legacy FairPlay).</summary>
    private static readonly HashSet<string> EncryptedCodecTags = new(StringComparer.OrdinalIgnoreCase) { "encv", "enca", "drms", "drmi" };

    private readonly IFfmpegLocator _locator;
    private readonly IProcessRunner _runner;

    public FfprobeReader(IFfmpegLocator locator, IProcessRunner runner)
    {
        _locator = locator;
        _runner = runner;
    }

    /// <summary>
    /// The ffprobe argument list. <c>-protocol_whitelist file</c> keeps crafted playlists from making
    /// ffprobe open network URLs (Kvertis has no network code, docs/02 §4).
    /// </summary>
    public static IReadOnlyList<string> BuildArguments(string path) =>
        ["-v", "error", "-print_format", "json", "-show_format", "-show_streams", "-protocol_whitelist", "file", Path.GetFullPath(path)];

    public async Task<MediaInfo> ReadAsync(string path, MediaKind kind, CancellationToken ct)
    {
        var ffprobe = _locator.FfprobePath
                      ?? throw new ConversionException(ConversionErrorCode.ToolMissing, path, "probe", "ffprobe not found");
        var request = new ProcessRequest(ffprobe, BuildArguments(path), InputLimits.AnalysisTimeoutFor(kind)) { CaptureStdout = true };
        var outcome = await _runner.RunAsync(request, null, ct).ConfigureAwait(false);
        if (!outcome.Succeeded)
        {
            throw FfmpegErrorMapper.Map(outcome, path, "probe");
        }
        return Parse(outcome.StandardOutput, path);
    }

    /// <summary>Parses ffprobe JSON. Throws CorruptFile when the output is not valid JSON.</summary>
    public static MediaInfo Parse(string json, string? path = null)
    {
        ArgumentNullException.ThrowIfNull(json);
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new ConversionException(ConversionErrorCode.CorruptFile, path, "probe", "ffprobe returned invalid JSON", ex);
        }

        using (doc)
        {
            var root = doc.RootElement;
            var format = root.TryGetProperty("format", out var f) && f.ValueKind == JsonValueKind.Object ? f : default;
            var streams = root.TryGetProperty("streams", out var s) && s.ValueKind == JsonValueKind.Array
                ? s.EnumerateArray().ToList()
                : [];

            JsonElement? video = null;
            JsonElement? audio = null;
            var audioCount = 0;
            var encrypted = HasEncryptionTags(format);
            double? maxStreamDuration = null;

            foreach (var stream in streams)
            {
                var type = GetString(stream, "codec_type");
                encrypted |= HasEncryptionTags(stream) || IsEncryptedStream(stream);
                if (GetDouble(stream, "duration") is { } sd)
                {
                    maxStreamDuration = Math.Max(maxStreamDuration ?? 0, sd);
                }

                if (type == "video" && !IsAttachedPicture(stream))
                {
                    video ??= stream;
                }
                else if (type == "audio")
                {
                    audioCount++;
                    audio ??= stream;
                }
            }

            var durationSeconds = GetDouble(format, "duration") ?? maxStreamDuration;
            TimeSpan? duration = durationSeconds is > 0 ? TimeSpan.FromSeconds(durationSeconds.Value) : null;

            var realRate = video is { } v1 ? ParseRational(GetString(v1, "r_frame_rate")) : null;
            var avgRate = video is { } v2 ? ParseRational(GetString(v2, "avg_frame_rate")) : null;
            var vfr = realRate is > 0 && avgRate is > 0 && Math.Abs(realRate.Value - avgRate.Value) / realRate.Value > 0.01;

            var fieldOrder = video is { } v3 ? GetString(v3, "field_order") : null;
            var interlaced = fieldOrder is not null
                             && !string.Equals(fieldOrder, "progressive", StringComparison.OrdinalIgnoreCase)
                             && !string.Equals(fieldOrder, "unknown", StringComparison.OrdinalIgnoreCase);

            return new MediaInfo(
                duration,
                video is { } vw ? GetInt(vw, "width") : null,
                video is { } vh ? GetInt(vh, "height") : null,
                video is { } vc ? GetString(vc, "codec_name") : null,
                audio is { } ac ? GetString(ac, "codec_name") : null,
                video is not null,
                audio is not null,
                audioCount,
                vfr,
                interlaced,
                encrypted,
                avgRate is > 0 ? avgRate : realRate,
                video is { } vb ? Kbps(GetDouble(vb, "bit_rate")) : null,
                audio is { } ab ? Kbps(GetDouble(ab, "bit_rate")) : null,
                audio is { } ar ? GetInt(ar, "sample_rate") : null,
                GetString(format, "format_name"));
        }
    }

    private static int? Kbps(double? bitsPerSecond) => bitsPerSecond is > 0 ? (int)Math.Round(bitsPerSecond.Value / 1000) : null;

    private static bool IsAttachedPicture(JsonElement stream) =>
        stream.TryGetProperty("disposition", out var d) && d.ValueKind == JsonValueKind.Object
        && GetInt(d, "attached_pic") == 1;

    private static bool IsEncryptedStream(JsonElement stream)
    {
        if (GetString(stream, "codec_tag_string") is { } tag && EncryptedCodecTags.Contains(tag))
        {
            return true;
        }
        if (stream.TryGetProperty("side_data_list", out var list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in list.EnumerateArray())
            {
                if (GetString(entry, "side_data_type") is { } t && t.Contains("encryption", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }
        return false;
    }

    private static bool HasEncryptionTags(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty("tags", out var tags) || tags.ValueKind != JsonValueKind.Object)
        {
            return false;
        }
        foreach (var tag in tags.EnumerateObject())
        {
            if (tag.Name.Contains("encrypt", StringComparison.OrdinalIgnoreCase)
                || string.Equals(tag.Name, "drm", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    private static string? GetString(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value))
        {
            return null;
        }
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null,
        };
    }

    private static double? GetDouble(JsonElement element, string name) =>
        double.TryParse(GetString(element, name), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && double.IsFinite(d) ? d : null;

    private static int? GetInt(JsonElement element, string name) =>
        GetDouble(element, name) is { } d && d is >= int.MinValue and <= int.MaxValue ? (int)d : null;

    /// <summary>Parses "30000/1001" or "25". Returns null for "0/0" and garbage.</summary>
    internal static double? ParseRational(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }
        var parts = value.Split('/');
        if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var num))
        {
            return null;
        }
        var den = 1.0;
        if (parts.Length > 1 && !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out den))
        {
            return null;
        }
        return den == 0 ? null : num / den;
    }
}
