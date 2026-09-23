using Kvertis.Engine.Abstractions;
using Kvertis.Engine.Validation;

namespace Kvertis.Engine.Ffmpeg;

/// <summary>License- and patent-relevant facts about an ffmpeg build (docs/02-rechtssicherheit.md §2, ADR-015).</summary>
public sealed record ComplianceReport(
    bool IsGplBuild,
    bool HasNonFree,
    bool HasForbiddenEncoders,
    string Configuration,
    IReadOnlyList<string> ForbiddenEncodersFound)
{
    /// <summary>Decoders for patent-encumbered formats found in <c>ffmpeg -decoders</c>.</summary>
    public IReadOnlyList<string> ForbiddenDecodersFound { get; init; } = [];

    /// <summary>Network protocols found in <c>ffmpeg -protocols</c>.</summary>
    public IReadOnlyList<string> NetworkProtocolsFound { get; init; } = [];

    public bool HasForbiddenDecoders => ForbiddenDecodersFound.Count > 0;

    public bool HasNetworkProtocols => NetworkProtocolsFound.Count > 0;

    public bool IsCompliant => !IsGplBuild && !HasNonFree && !HasForbiddenEncoders && !HasForbiddenDecoders && !HasNetworkProtocols;

    /// <summary>Short machine-readable reasons ("gpl build", "forbidden decoders: …"); empty when compliant.</summary>
    public IReadOnlyList<string> Reasons
    {
        get
        {
            var reasons = new List<string>(5);
            if (IsGplBuild)
            {
                reasons.Add("gpl build");
            }
            if (HasNonFree)
            {
                reasons.Add("nonfree build");
            }
            if (HasForbiddenEncoders)
            {
                reasons.Add("forbidden encoders: " + string.Join(", ", ForbiddenEncodersFound));
            }
            if (HasForbiddenDecoders)
            {
                reasons.Add("forbidden decoders: " + string.Join(", ", ForbiddenDecodersFound));
            }
            if (HasNetworkProtocols)
            {
                reasons.Add("network protocols: " + string.Join(", ", NetworkProtocolsFound));
            }
            return reasons;
        }
    }
}

/// <summary>
/// Checks that the located ffmpeg is the Kvertis allowlist build (LGPL, no forbidden encoders, no decoders for
/// patent-encumbered formats per <see cref="EncumberedCodecs.IsForbiddenDecoder"/> including hardware variants, no
/// network protocols; same rules as <c>tools/ffmpeg/check-build.sh</c>) and
/// refuses to work with anything else. Register one instance per process (singleton); the result is cached.
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
        "lib" + "xvid",
    ];

    private static readonly string[] ForbiddenConfigureFlags =
    [
        "--enable-" + "lib" + "x264",
        "--enable-" + "lib" + "x265",
        "--enable-" + "lib" + "fdk-aac",
        "--enable-" + "lib" + "xvid",
    ];

    // Network protocols that must not be compiled in (--disable-network; only "file" and "pipe" exist).
    private static readonly string[] NetworkProtocolNames =
    [
        "http", "https", "tcp", "udp", "tls", "rtmp", "rtp", "srt", "ftp", "sftp",
        "rtmps", "rtmpt", "rtmpe", "rtsp", "udplite", "sctp", "mmsh", "mmst", "gopher", "icecast", "httpproxy",
    ];

    // Built from parts for the same reason: tools/compliance/check.sh greps for the literal flags.
    private static readonly string GplFlag = "--enable-" + "gpl";
    private static readonly string NonFreeFlag = "--enable-" + "nonfree";

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
    /// Throws <c>ConversionException(ToolMissing)</c> when ffmpeg is missing or not compliant (detail
    /// "ffmpeg build not compliant: &lt;reasons&gt;", see <see cref="ComplianceReport.Reasons"/>). Runs the check
    /// once; later calls reuse it.
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
        if (!report.IsCompliant)
        {
            throw new ConversionException(ConversionErrorCode.ToolMissing, step: "ffmpeg-compliance",
                detail: "ffmpeg build not compliant: " + string.Join("; ", report.Reasons));
        }
    }

    /// <summary>Runs <c>ffmpeg -version</c>, <c>-encoders</c>, <c>-decoders</c> and <c>-protocols</c> and evaluates them.</summary>
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
        var encoders = await RunListAsync(runner, ffmpeg, "-encoders", ct).ConfigureAwait(false);
        var decoders = await RunListAsync(runner, ffmpeg, "-decoders", ct).ConfigureAwait(false);
        var protocols = await RunListAsync(runner, ffmpeg, "-protocols", ct).ConfigureAwait(false);
        return Parse(version.StandardOutput, encoders, decoders, protocols);
    }

    private static async Task<string> RunListAsync(IProcessRunner runner, string ffmpeg, string option, CancellationToken ct)
    {
        var outcome = await runner.RunAsync(new ProcessRequest(ffmpeg, ["-hide_banner", option], CheckTimeout) { CaptureStdout = true }, null, ct).ConfigureAwait(false);
        if (!outcome.Succeeded)
        {
            throw FfmpegErrorMapper.Map(outcome, null, "ffmpeg-compliance");
        }
        return outcome.StandardOutput;
    }

    /// <summary>
    /// Evaluates the text of <c>ffmpeg -version</c>, <c>-encoders</c>, <c>-decoders</c> and <c>-protocols</c>.
    /// </summary>
    public static ComplianceReport Parse(string versionOutput, string encodersOutput, string decodersOutput, string protocolsOutput)
    {
        ArgumentNullException.ThrowIfNull(versionOutput);
        ArgumentNullException.ThrowIfNull(encodersOutput);
        ArgumentNullException.ThrowIfNull(decodersOutput);
        ArgumentNullException.ThrowIfNull(protocolsOutput);

        var configuration = versionOutput.Split('\n')
            .Select(l => l.Trim())
            .FirstOrDefault(l => l.StartsWith("configuration:", StringComparison.OrdinalIgnoreCase))?["configuration:".Length..].Trim()
            ?? string.Empty;
        var flags = configuration.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        var gpl = flags.Contains(GplFlag, StringComparer.OrdinalIgnoreCase);
        var nonFree = flags.Contains(NonFreeFlag, StringComparer.OrdinalIgnoreCase);

        var encoderNames = FfmpegFeatures.ParseEncoders(encodersOutput);
        var found = encoderNames
            .Where(e => ForbiddenEncoderNames.Contains(e, StringComparer.OrdinalIgnoreCase))
            .Concat(flags.Where(f => ForbiddenConfigureFlags.Contains(f, StringComparer.OrdinalIgnoreCase)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var decoders = FfmpegFeatures.ParseDecoders(decodersOutput)
            .Where(EncumberedCodecs.IsForbiddenDecoder)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var protocols = ParseProtocols(protocolsOutput)
            .Where(p => NetworkProtocolNames.Contains(p, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new ComplianceReport(gpl, nonFree, found.Count > 0, configuration, found)
        {
            ForbiddenDecodersFound = decoders,
            NetworkProtocolsFound = protocols,
        };
    }

    /// <summary>
    /// Parses <c>ffmpeg -hide_banner -protocols</c>: a "Supported file protocols:" header, then "Input:" and
    /// "Output:" sections with one protocol name per line.
    /// </summary>
    public static IReadOnlyList<string> ParseProtocols(string output)
    {
        ArgumentNullException.ThrowIfNull(output);
        return output.Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0 && !l.EndsWith(':') && !l.Contains(' ', StringComparison.Ordinal))
            .ToList();
    }
}
