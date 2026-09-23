using Kvertis.Engine.Abstractions;

namespace Kvertis.Engine.Ffmpeg;

/// <summary>
/// Watches ffmpeg's stderr while an HEVC source is decoded through D3D11VA. When hardware setup fails,
/// ffmpeg silently falls back to its built-in software HEVC decoder, which ADR-003 forbids. On the first
/// fallback message the guard cancels the process through the linked token it was given.
/// </summary>
internal sealed class HevcFallbackGuard : IProgress<string>
{
    public const string Detail = "hevc software fallback refused";

    // Messages ffmpeg prints when the requested hwaccel cannot be used and it continues in software.
    private static readonly string[] FallbackMarkers =
    [
        "Failed setup for format d3d11va",
        "Failed to set up hardware decoding",
        "hwaccel initialisation returned error",
        "doesn't support hardware accelerated",
    ];

    private readonly IProgress<string>? _inner;
    private readonly CancellationTokenSource _cancel;
    private int _triggered;

    public HevcFallbackGuard(IProgress<string>? inner, CancellationTokenSource cancel)
    {
        _inner = inner;
        _cancel = cancel;
    }

    public bool Triggered => Volatile.Read(ref _triggered) != 0;

    public void Report(string value)
    {
        if (value is not null && IsFallbackLine(value) && Interlocked.Exchange(ref _triggered, 1) == 0)
        {
            try
            {
                _cancel.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // The run already finished.
            }
        }
        _inner?.Report(value!);
    }

    public static bool IsFallbackLine(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        foreach (var marker in FallbackMarkers)
        {
            if (line.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    public static ConversionException Refused(string? path, string step) =>
        new(ConversionErrorCode.MissingSystemCodec, path, step, Detail);
}
