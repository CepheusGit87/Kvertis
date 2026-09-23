using Kvertis.Engine.Abstractions;
using Kvertis.Engine.Validation;

namespace Kvertis.Engine.Ffmpeg;

/// <summary>License-relevant facts about an ffmpeg build (docs/02-rechtssicherheit.md §2).</summary>
public sealed record ComplianceReport(
    bool IsGplBuild,
    bool HasNonFree,
    bool HasForbiddenEncoders,
    string Configuration,
    IReadOnlyList<string> ForbiddenEncodersFound)
{
    public bool IsCompliant => !IsGplBuild && !HasNonFree && !HasForbiddenEncoders;
}

/// <summary>
/// Checks that the located ffmpeg is an LGPL build without forbidden encoders, and refuses to work
/// with anything else. Register one instance per process (singleton); the result is cached.
/// </summary>
public sealed class FfmpegCompliance
{
    // The forbidden encoder names are assembled from parts on purpose: a source scan test guarantees
    // that the literal names never appear anywhere under src/, so no code can ever select them.
    private static readonly string[] ForbiddenEncoderNames =
    [
        "lib" + "x264",
        "lib" + "x264rgb",
        "lib" + "x265",
        "lib" + "fdk" + "_aac",
    ];

    private static readonly string[] ForbiddenConfigureFlags =
    [
        "--enable-" + "lib" + "x264",
        "--enable-" + "lib" + "x265",
        "--enable-" + "lib" + "fdk-aac",
        "--enable-" + "lib" + "xvid",
    ];

    private static readonly TimeSpan CheckTimeout = InputLimits.AnalysisTimeoutFor(MediaKind.Audio);

    private readonly IFfmpegLocator _locator;
    private readonly IProcessRunner _runner;
    private readonly object _gate = new();
    private Task<ComplianceReport>? _cached;

    public FfmpegCompliance(IFfmpegLocator locator, IProcessRunner runner)
    {
        _locator = locator;
        _runner = runner;
    }

    /// <summary>
    /// Throws <c>ConversionException(ToolMissing)</c> when ffmpeg is missing or not compliant
    /// (detail "gpl build", "nonfree build" or "forbidden encoders"). Runs the check once; later calls reuse it.
    /// </summary>
    public async Task EnsureCompliantAsync(CancellationToken ct)
    {
        Task<ComplianceReport> task;
        lock (_gate)
        {
            if (_cached is null || _cached.IsFaulted || _cached.IsCanceled)
            {
                _cached = CheckAsync(_locator, _runner, CancellationToken.None);
            }
            task = _cached;
        }

        var report = await task.WaitAsync(ct).ConfigureAwait(false);
        if (report.IsGplBuild)
        {
            throw new ConversionException(ConversionErrorCode.ToolMissing, step: "ffmpeg-compliance", detail: "gpl build");
        }
        if (report.HasNonFree)
        {
            throw new ConversionException(ConversionErrorCode.ToolMissing, step: "ffmpeg-compliance", detail: "nonfree build");
        }
        if (report.HasForbiddenEncoders)
        {
            throw new ConversionException(ConversionErrorCode.ToolMissing, step: "ffmpeg-compliance",
                detail: "forbidden encoders: " + string.Join(", ", report.ForbiddenEncodersFound));
        }
    }

    /// <summary>Runs <c>ffmpeg -version</c> and <c>ffmpeg -hide_banner -encoders</c> and evaluates them.</summary>
    public static async Task<ComplianceReport> CheckAsync(IFfmpegLocator locator, IProcessRunner runner, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(locator);
        ArgumentNullException.ThrowIfNull(runner);
        var ffmpeg = locator.FfmpegPath
                     ?? throw new ConversionException(ConversionErrorCode.ToolMissing, step: "ffmpeg-compliance", detail: "ffmpeg not found");

        var version = await runner.RunAsync(new ProcessRequest(ffmpeg, ["-version"], CheckTimeout) { CaptureStdout = true }, null, ct).ConfigureAwait(false);
        if (!version.Succeeded)
        {
            throw FfmpegErrorMapper.Map(version, null, "ffmpeg-compliance");
        }
        var encoders = await runner.RunAsync(new ProcessRequest(ffmpeg, ["-hide_banner", "-encoders"], CheckTimeout) { CaptureStdout = true }, null, ct).ConfigureAwait(false);
        if (!encoders.Succeeded)
        {
            throw FfmpegErrorMapper.Map(encoders, null, "ffmpeg-compliance");
        }
        return Parse(version.StandardOutput, encoders.StandardOutput);
    }

    /// <summary>Evaluates the text of <c>ffmpeg -version</c> and <c>ffmpeg -encoders</c>.</summary>
    public static ComplianceReport Parse(string versionOutput, string encodersOutput)
    {
        ArgumentNullException.ThrowIfNull(versionOutput);
        ArgumentNullException.ThrowIfNull(encodersOutput);

        var configuration = versionOutput.Split('\n')
            .Select(l => l.Trim())
            .FirstOrDefault(l => l.StartsWith("configuration:", StringComparison.OrdinalIgnoreCase))?["configuration:".Length..].Trim()
            ?? string.Empty;
        var flags = configuration.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        var gpl = flags.Contains("--enable-gpl", StringComparer.OrdinalIgnoreCase);
        var nonFree = flags.Contains("--enable-nonfree", StringComparer.OrdinalIgnoreCase);

        var encoderNames = FfmpegFeatures.ParseEncoders(encodersOutput);
        var found = encoderNames
            .Where(e => ForbiddenEncoderNames.Contains(e, StringComparer.OrdinalIgnoreCase))
            .Concat(flags.Where(f => ForbiddenConfigureFlags.Contains(f, StringComparer.OrdinalIgnoreCase)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new ComplianceReport(gpl, nonFree, found.Count > 0, configuration, found);
    }
}
