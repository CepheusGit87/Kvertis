using Kvertis.Engine.Abstractions;
using Kvertis.Engine.Validation;

namespace Kvertis.Engine.Ffmpeg;

/// <summary>
/// What the installed ffmpeg build offers: encoder names from <c>ffmpeg -encoders</c> and hardware
/// decoders from <c>ffmpeg -hwaccels</c>. A snapshot; build arguments against it, never assume.
/// </summary>
public sealed class FfmpegFeatures
{
    public const string D3D11Va = "d3d11va";

    private readonly HashSet<string> _encoders;
    private readonly HashSet<string> _hwaccels;

    public FfmpegFeatures(IEnumerable<string> encoders, IEnumerable<string>? hwaccels = null)
    {
        _encoders = new HashSet<string>(encoders, StringComparer.OrdinalIgnoreCase);
        _hwaccels = new HashSet<string>(hwaccels ?? [], StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyCollection<string> Encoders => _encoders;
    public IReadOnlyCollection<string> HardwareAccelerations => _hwaccels;

    public bool HasEncoder(string name) => _encoders.Contains(name);

    public bool HasHardwareAcceleration(string name) => _hwaccels.Contains(name);

    /// <summary>True when ffmpeg can decode through Direct3D 11 (only ever listed by Windows builds).</summary>
    public bool CanUseD3D11Va => HasHardwareAcceleration(D3D11Va);

    /// <summary>Parses the table printed by <c>ffmpeg -hide_banner -encoders</c>.</summary>
    public static IReadOnlyList<string> ParseEncoders(string output)
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

    /// <summary>Parses the list printed by <c>ffmpeg -hide_banner -hwaccels</c>.</summary>
    public static IReadOnlyList<string> ParseHardwareAccelerations(string output)
    {
        ArgumentNullException.ThrowIfNull(output);
        return output.Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0 && !l.Contains(':', StringComparison.Ordinal) && !l.Contains(' ', StringComparison.Ordinal))
            .ToList();
    }
}

/// <summary>Provides the (cached) feature set of the located ffmpeg build.</summary>
public interface IFfmpegFeatures
{
    Task<FfmpegFeatures> GetAsync(CancellationToken ct);
}

/// <summary>
/// Default <see cref="IFfmpegFeatures"/>: runs <c>ffmpeg -hide_banner -encoders</c> and <c>-hwaccels</c> once
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

        var encoders = await RunAsync(ffmpeg, "-encoders").ConfigureAwait(false);
        var hwaccels = await RunAsync(ffmpeg, "-hwaccels").ConfigureAwait(false);
        return new FfmpegFeatures(ParseOrEmpty(encoders, FfmpegFeatures.ParseEncoders), ParseOrEmpty(hwaccels, FfmpegFeatures.ParseHardwareAccelerations));
    }

    private static IReadOnlyList<string> ParseOrEmpty(ProcessOutcome? outcome, Func<string, IReadOnlyList<string>> parse) =>
        outcome is null ? [] : parse(outcome.StandardOutput);

    private async Task<ProcessOutcome?> RunAsync(string ffmpeg, string listOption)
    {
        var request = new ProcessRequest(ffmpeg, ["-hide_banner", listOption], ProbeTimeout) { CaptureStdout = true };
        var outcome = await _runner.RunAsync(request, null, CancellationToken.None).ConfigureAwait(false);
        if (listOption == "-encoders" && !outcome.Succeeded)
        {
            throw FfmpegErrorMapper.Map(outcome, null, "ffmpeg-features");
        }
        // -hwaccels is optional information; a failure only means no hardware decoding.
        return outcome.Succeeded ? outcome : null;
    }
}
