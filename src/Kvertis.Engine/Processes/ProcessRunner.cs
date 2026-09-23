using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using Kvertis.Engine.Abstractions;

namespace Kvertis.Engine.Processes;

/// <summary>
/// Default IProcessRunner. Reads stdout and stderr concurrently so neither pipe can block the child,
/// enforces the timeout, and kills the whole process tree on cancellation or timeout.
/// The timeout only counts time in which the process is not suspended (see <see cref="NotifySuspended"/>).
/// </summary>
public sealed class ProcessRunner : IProcessRunner
{
    private const int StderrTailLines = 60;

    private static readonly TimeSpan MaxWatchdogTick = TimeSpan.FromSeconds(1);

    private readonly ConcurrentDictionary<int, byte> _suspended = new();

    /// <summary>Raised with the process id right after start, so a suspender can pause it.</summary>
    public event EventHandler<int>? ProcessStarted;

    /// <summary>
    /// Raised with the process id once the process has exited or was killed (timeout, cancellation). Raised
    /// while this runner still holds the process handle, so the id cannot have been reused yet; listeners
    /// must forget the id here and never suspend it again.
    /// </summary>
    public event EventHandler<int>? ProcessExited;

    /// <summary>The pauser suspended <paramref name="processId"/>: its timeout stops counting until <see cref="NotifyResumed"/>.</summary>
    public void NotifySuspended(int processId) => _suspended[processId] = 0;

    /// <summary>The pauser resumed <paramref name="processId"/>: its timeout counts again.</summary>
    public void NotifyResumed(int processId) => _suspended.TryRemove(processId, out _);

    public async Task<ProcessOutcome> RunAsync(ProcessRequest request, IProgress<string>? stderrLines, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var psi = new ProcessStartInfo
        {
            FileName = request.ExecutablePath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = false,
            CreateNoWindow = true,
            WorkingDirectory = request.WorkingDirectory ?? string.Empty,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var arg in request.Arguments)
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = new Process { StartInfo = psi };
        var stopwatch = Stopwatch.StartNew();
        try
        {
            if (!process.Start())
            {
                throw new ConversionException(ConversionErrorCode.ToolMissing, step: "start", detail: request.ExecutablePath);
            }
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            throw new ConversionException(ConversionErrorCode.ToolMissing, step: "start", detail: $"{request.ExecutablePath}: {ex.Message}", inner: ex);
        }

        var pid = process.Id;
        var exitRaised = false;
        try
        {
            ProcessStarted?.Invoke(this, pid);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            using var stopWatchdog = new CancellationTokenSource();
            var watchdog = WatchTimeoutAsync(request.Timeout, WatchdogTickFor(request.Timeout), () => _suspended.ContainsKey(pid), timeoutCts, stopWatchdog.Token);
            var token = timeoutCts.Token;

            var stdoutTask = request.CaptureStdout
                ? process.StandardOutput.ReadToEndAsync(token)
                : DrainAsync(process.StandardOutput, token);
            var stderrTail = new Queue<string>(StderrTailLines);
            var stderrTask = PumpStderrAsync(process.StandardError, stderrLines, stderrTail, token);

            var timedOut = false;
            try
            {
                await process.WaitForExitAsync(token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                timedOut = !ct.IsCancellationRequested;
                TryKill(process);
            }
            finally
            {
                await stopWatchdog.CancelAsync().ConfigureAwait(false);
                exitRaised = true;
                RaiseExited(pid);
            }
            await watchdog.ConfigureAwait(false);

            // Give the pumps a moment to flush after the process ended; they observe EOF once the pipes close.
            try
            {
                await Task.WhenAll(stdoutTask, stderrTask).WaitAsync(TimeSpan.FromSeconds(2), CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Pipes did not close cleanly after a kill; nothing more to collect.
            }

            stopwatch.Stop();

            if (ct.IsCancellationRequested)
            {
                throw new ConversionException(ConversionErrorCode.Cancelled, step: Path.GetFileName(request.ExecutablePath));
            }

            var stdout = stdoutTask.IsCompletedSuccessfully ? stdoutTask.Result : string.Empty;
            var exitCode = timedOut ? -1 : SafeExitCode(process);
            return new ProcessOutcome(exitCode, stdout, string.Join('\n', stderrTail), stopwatch.Elapsed, timedOut);
        }
        finally
        {
            if (!exitRaised)
            {
                TryKill(process);
                RaiseExited(pid);
            }
        }
    }

    /// <summary>
    /// Cancels <paramref name="expire"/> once the process has been running, not counting suspended time,
    /// for <paramref name="timeout"/>. Replaces CancelAfter, which would also count the time a paused job
    /// spends frozen. Ends quietly when <paramref name="stop"/> is cancelled.
    /// </summary>
    internal static async Task WatchTimeoutAsync(TimeSpan timeout, TimeSpan tick, Func<bool> isSuspended, CancellationTokenSource expire, CancellationToken stop)
    {
        if (timeout == Timeout.InfiniteTimeSpan)
        {
            return;
        }
        using var timer = new PeriodicTimer(tick);
        var clock = Stopwatch.StartNew();
        var last = TimeSpan.Zero;
        var active = TimeSpan.Zero;
        try
        {
            while (await timer.WaitForNextTickAsync(stop).ConfigureAwait(false))
            {
                var now = clock.Elapsed;
                var delta = now - last;
                last = now;
                if (!isSuspended())
                {
                    active += delta;
                }
                if (active >= timeout)
                {
                    await expire.CancelAsync().ConfigureAwait(false);
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Process ended before the timeout.
        }
        catch (ObjectDisposedException)
        {
            // The run finished while the timer fired.
        }
    }

    /// <summary>One second for normal timeouts; finer for very short ones so they are not overshot by much.</summary>
    internal static TimeSpan WatchdogTickFor(TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero || timeout == Timeout.InfiniteTimeSpan)
        {
            return MaxWatchdogTick;
        }
        var quarter = timeout / 4;
        return quarter < MaxWatchdogTick ? TimeSpan.FromTicks(Math.Max(quarter.Ticks, TimeSpan.FromMilliseconds(10).Ticks)) : MaxWatchdogTick;
    }

    private void RaiseExited(int pid)
    {
        _suspended.TryRemove(pid, out _);
        ProcessExited?.Invoke(this, pid);
    }

    private static int SafeExitCode(Process process)
    {
        try
        {
            return process.HasExited ? process.ExitCode : -1;
        }
        catch (InvalidOperationException)
        {
            return -1;
        }
    }

    private static async Task<string> DrainAsync(StreamReader reader, CancellationToken ct)
    {
        var buffer = new char[4096];
        while (await reader.ReadAsync(buffer, ct).ConfigureAwait(false) > 0)
        {
            // discard
        }
        return string.Empty;
    }

    private static async Task PumpStderrAsync(StreamReader reader, IProgress<string>? lines, Queue<string> tail, CancellationToken ct)
    {
        // ffmpeg writes progress with '\r' separators; treat both '\r' and '\n' as line ends.
        var sb = new StringBuilder();
        var buffer = new char[1024];
        int read;
        while ((read = await reader.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            for (var i = 0; i < read; i++)
            {
                var c = buffer[i];
                if (c is '\n' or '\r')
                {
                    if (sb.Length > 0)
                    {
                        Emit(sb.ToString(), lines, tail);
                        sb.Clear();
                    }
                }
                else
                {
                    sb.Append(c);
                }
            }
        }
        if (sb.Length > 0)
        {
            Emit(sb.ToString(), lines, tail);
        }
    }

    private static void Emit(string line, IProgress<string>? lines, Queue<string> tail)
    {
        lock (tail)
        {
            if (tail.Count >= StderrTailLines)
            {
                tail.Dequeue();
            }
            tail.Enqueue(line);
        }
        lines?.Report(line);
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception)
        {
            // Already gone or access denied; nothing else to do.
        }
    }
}
