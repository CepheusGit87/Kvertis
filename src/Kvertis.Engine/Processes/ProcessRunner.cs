using System.Diagnostics;
using System.Text;
using Kvertis.Engine.Abstractions;

namespace Kvertis.Engine.Processes;

/// <summary>
/// Default IProcessRunner. Reads stdout and stderr concurrently so neither pipe can block the child,
/// enforces the timeout, and kills the whole process tree on cancellation or timeout.
/// </summary>
public sealed class ProcessRunner : IProcessRunner
{
    private const int StderrTailLines = 60;

    /// <summary>Raised with the process id right after start, so a suspender can pause it.</summary>
    public event EventHandler<int>? ProcessStarted;

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

        ProcessStarted?.Invoke(this, process.Id);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(request.Timeout);
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
