using Kvertis.Engine.Abstractions;
using Kvertis.Engine.Validation;

namespace Kvertis.Engine.Ffmpeg;

/// <summary>
/// What the installed ffmpeg build offers: encoder names from <c>ffmpeg -encoders</c>. A snapshot; build
/// arguments against it, never assume. (Hardware decoding is not used: encumbered inputs never reach
/// ffmpeg, ADR-015.)
/// </summary>
public sealed class FfmpegFeatures
{
    private readonly HashSet<string> _encoders;

    public FfmpegFeatures(IEnumerable<string> encoders)
    {
        _encoders = new HashSet<string>(encoders, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyCollection<string> Encoders => _encoders;

    public bool HasEncoder(string name) => _encoders.Contains(name);

    /// <summary>Parses the table printed by <c>ffmpeg -hide_banner -encoders</c>.</summary>
    public static IReadOnlyList<string> ParseEncoders(string output) => ParseCodecTable(output);

    /// <summary>Parses the table printed by <c>ffmpeg -hide_banner -decoders</c> (same layout as the encoder table).</summary>
    public static IReadOnlyList<string> ParseDecoders(string output) => ParseCodecTable(output);

    private static List<string> ParseCodecTable(string output)
    {
        ArgumentNullException.ThrowIfNull(output);
        var result = new List<string>();
        var inTable = false;
        foreach (var raw in output.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0)
            {
                continue;
            }
            if (!inTable)
            {
                // The legend ends with a line of dashes; the encoder table follows.
                inTable = line.StartsWith("---", StringComparison.Ordinal);
                continue;
            }
            var parts = line.Split((char[]?)null, 3, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2 && parts[0].Length == 6)
            {
                result.Add(parts[1]);
            }
        }
        return result;
    }
}

/// <summary>Provides the (cached) feature set of the located ffmpeg build.</summary>
public interface IFfmpegFeatures
{
    Task<FfmpegFeatures> GetAsync(CancellationToken ct);
}

/// <summary>
/// Default <see cref="IFfmpegFeatures"/>: runs <c>ffmpeg -hide_banner -encoders</c> once
/// per instance (register as singleton) and caches the result. A failed probe is not cached.
/// </summary>
public sealed class FfmpegFeatureProbe : IFfmpegFeatures
{
    private static readonly TimeSpan ProbeTimeout = InputLimits.AnalysisTimeoutFor(MediaKind.Audio);

    private readonly IFfmpegLocator _locator;
    private readonly IProcessRunner _runner;
    private readonly object _gate = new();
    private Task<FfmpegFeatures>? _cached;

    public FfmpegFeatureProbe(IFfmpegLocator locator, IProcessRunner runner)
    {
        _locator = locator;
        _runner = runner;
    }

    public Task<FfmpegFeatures> GetAsync(CancellationToken ct)
    {
        Task<FfmpegFeatures> task;
        lock (_gate)
        {
            if (_cached is null || _cached.IsFaulted || _cached.IsCanceled)
            {
                // The shared probe must not die with the first caller's token; callers wait with their own.
                _cached = ProbeAsync();
            }
            task = _cached;
        }
        return task.WaitAsync(ct);
    }

    private async Task<FfmpegFeatures> ProbeAsync()
    {
        var ffmpeg = _locator.FfmpegPath
                     ?? throw new ConversionException(ConversionErrorCode.ToolMissing, step: "ffmpeg-features", detail: "ffmpeg not found");

        var request = new ProcessRequest(ffmpeg, ["-hide_banner", "-encoders"], ProbeTimeout) { CaptureStdout = true };
        var outcome = await _runner.RunAsync(request, null, CancellationToken.None).ConfigureAwait(false);
        if (!outcome.Succeeded)
        {
            throw FfmpegErrorMapper.Map(outcome, null, "ffmpeg-features");
        }
        return new FfmpegFeatures(FfmpegFeatures.ParseEncoders(outcome.StandardOutput));
    }
}
