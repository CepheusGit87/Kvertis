using System.Diagnostics;
using System.Globalization;
using Kvertis.Engine.Abstractions;

namespace Kvertis.Engine.Ffmpeg;

/// <summary>
/// Consumes ffmpeg stderr lines produced by <c>-progress pipe:2 -nostats</c> and reports a job-wide
/// fraction. The converting phase spans <see cref="ConvertStart"/>..<see cref="ConvertEnd"/>; the rest is
/// left for analysis and finalizing. When the duration is unknown an indeterminate 0.5 is reported.
/// </summary>
public sealed class FfmpegProgressParser : IProgress<string>
{
    public const double ConvertStart = 0.05;
    public const double ConvertEnd = 0.95;
    public const double Indeterminate = 0.5;

    private readonly TimeSpan? _duration;
    private readonly IProgress<ConversionProgress> _target;
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    private double _lastFraction = -1;

    public FfmpegProgressParser(TimeSpan? duration, IProgress<ConversionProgress> target)
    {
        _duration = duration is { } d && d > TimeSpan.Zero ? d : null;
        _target = target;
    }

    public void Report(string value)
    {
        if (value is null)
        {
            return;
        }
        var outTime = ParseOutTime(value);
        if (outTime is null)
        {
            return;
        }

        double fraction;
        TimeSpan? remaining = null;
        if (_duration is { } duration)
        {
            var local = FractionOf(outTime.Value, duration);
            fraction = ConvertStart + (local * (ConvertEnd - ConvertStart));
            if (local > 0.01)
            {
                var elapsed = _stopwatch.Elapsed;
                remaining = TimeSpan.FromTicks((long)(elapsed.Ticks * ((1 - local) / local)));
            }
        }
        else
        {
            fraction = Indeterminate;
        }

        // Only report forward movement; ffmpeg repeats blocks with identical timestamps.
        if (fraction <= _lastFraction)
        {
            return;
        }
        _lastFraction = fraction;
        _target.Report(new ConversionProgress(fraction, ConversionPhase.Converting, remaining));
    }

    /// <summary>
    /// Reads <c>out_time_us=</c> / <c>out_time_ms=</c> lines. Note: ffmpeg writes microseconds under both
    /// keys (out_time_ms is misnamed upstream), so both are interpreted as microseconds. Returns null for
    /// other lines and for "N/A".
    /// </summary>
    public static TimeSpan? ParseOutTime(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        var span = line.AsSpan().Trim();
        var eq = span.IndexOf('=');
        if (eq <= 0)
        {
            return null;
        }
        var key = span[..eq];
        if (!key.SequenceEqual("out_time_us") && !key.SequenceEqual("out_time_ms"))
        {
            return null;
        }
        if (!long.TryParse(span[(eq + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var micros) || micros < 0)
        {
            return null;
        }
        return TimeSpan.FromTicks(micros * (TimeSpan.TicksPerMillisecond / 1000));
    }

    /// <summary>Position within the file, clamped to 0..1.</summary>
    public static double FractionOf(TimeSpan outTime, TimeSpan duration) =>
        duration <= TimeSpan.Zero ? Indeterminate : Math.Clamp(outTime.TotalSeconds / duration.TotalSeconds, 0, 1);
}
