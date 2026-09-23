global using NSubstitute;
global using Shouldly;
global using Xunit;

using System.Collections.Concurrent;
using Kvertis.Engine.Abstractions;

namespace Kvertis.Queue.Tests;

/// <summary>A temp directory that is deleted on dispose.</summary>
internal sealed class TempDir : IDisposable
{
    public TempDir()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "kvertis-queue-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string Combine(params string[] parts) => System.IO.Path.Combine([Path, .. parts]);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

/// <summary>Time that only moves when the test says so.</summary>
internal sealed class ManualTimeProvider : TimeProvider
{
    private readonly object _gate = new();
    private DateTimeOffset _now = new(2026, 9, 23, 12, 0, 0, TimeSpan.Zero);
    private long _ticks = 1_000_000;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override DateTimeOffset GetUtcNow()
    {
        lock (_gate)
        {
            return _now;
        }
    }

    public override long GetTimestamp()
    {
        lock (_gate)
        {
            return _ticks;
        }
    }

    public void Advance(TimeSpan by)
    {
        lock (_gate)
        {
            _now += by;
            _ticks += by.Ticks;
        }
    }
}

/// <summary>One call into the fake converter. The test decides when and how it ends.</summary>
internal sealed class ConvertCall
{
    private readonly Action _onEnd;
    private int _ended;

    public ConvertCall(InputInfo input, string outputPath, IProgress<ConversionProgress> progress, Action onEnd, CancellationToken token)
    {
        Input = input;
        OutputPath = outputPath;
        Progress = progress;
        Token = token;
        _onEnd = onEnd;
        token.Register(() =>
        {
            End();
            Tcs.TrySetCanceled(token);
        });
    }

    public InputInfo Input { get; }

    public string OutputPath { get; }

    public IProgress<ConversionProgress> Progress { get; }

    public CancellationToken Token { get; }

    public TaskCompletionSource<ConversionResult> Tcs { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public void Complete(long inBytes = 1000, long outBytes = 400)
    {
        End();
        Tcs.TrySetResult(new ConversionResult(OutputPath, inBytes, outBytes, TimeSpan.FromSeconds(1)));
    }

    public void Fail(Exception ex)
    {
        End();
        Tcs.TrySetException(ex);
    }

    private void End()
    {
        if (Interlocked.Exchange(ref _ended, 1) == 0)
        {
            _onEnd();
        }
    }
}

/// <summary>Queue plus NSubstitute fakes. The converter never finishes on its own.</summary>
internal sealed class QueueHarness : IAsyncDisposable
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private readonly ConcurrentQueue<ConvertCall> _calls = new();
    private readonly SemaphoreSlim _started = new(0);
    private readonly List<JobChangedEventArgs> _events = [];
    private int _concurrent;
    private int _maxConcurrent;

    public QueueHarness(
        int maxParallel = 4,
        int maxParallelVideo = 2,
        IJobPauser? pauser = null,
        IJobAdmissionPolicy? policy = null,
        JobHistory? history = null,
        ISpeedProfileStore? speedStore = null,
        TimeProvider? time = null,
        TimeSpan? throttle = null)
    {
        Converter.Name.Returns("fake");
        Converter.ConvertAsync(default!, default!, default!, default!, default)
            .ReturnsForAnyArgs(ci =>
            {
                var now = Interlocked.Increment(ref _concurrent);
                InterlockedMax(ref _maxConcurrent, now);
                var call = new ConvertCall(
                    ci.ArgAt<InputInfo>(0), ci.ArgAt<string>(1), ci.ArgAt<IProgress<ConversionProgress>>(3),
                    () => Interlocked.Decrement(ref _concurrent), ci.ArgAt<CancellationToken>(4));
                OnConvert?.Invoke(call);
                _calls.Enqueue(call);
                _started.Release();
                return call.Tcs.Task;
            });
        Resolver.Resolve(default!, default).ReturnsForAnyArgs(Converter);
        Validator.ValidateAsync(default!, default).ReturnsForAnyArgs(Task.FromResult(ValidationOutcome.Ok));
        Estimator.Estimate(default!, default!).ReturnsForAnyArgs(Estimate.Unknown);

        Queue = new JobQueue(Resolver, Validator, Estimator, new JobQueueOptions
        {
            MaxParallel = maxParallel,
            MaxParallelVideo = maxParallelVideo,
            Pauser = pauser,
            AdmissionPolicy = policy,
            History = history,
            SpeedProfileStore = speedStore,
            TimeProvider = time,
            ProgressThrottle = throttle ?? JobQueueOptions.DefaultProgressThrottle,
        });
        Queue.JobChanged += (_, e) =>
        {
            lock (_events)
            {
                _events.Add(e);
            }
        };
    }

    public IConverter Converter { get; } = Substitute.For<IConverter>();

    public IConverterResolver Resolver { get; } = Substitute.For<IConverterResolver>();

    public IInputValidator Validator { get; } = Substitute.For<IInputValidator>();

    public IEstimator Estimator { get; } = Substitute.For<IEstimator>();

    public TempDir Temp { get; } = new();

    public JobQueue Queue { get; }

    /// <summary>Runs synchronously inside ConvertAsync, on the job's async flow.</summary>
    public Action<ConvertCall>? OnConvert { get; set; }

    public int MaxConcurrent => Volatile.Read(ref _maxConcurrent);

    public IReadOnlyList<JobChangedEventArgs> Events
    {
        get
        {
            lock (_events)
            {
                return _events.ToArray();
            }
        }
    }

    public ConversionJob NewJob(string name = "photo.jpg", MediaKind kind = MediaKind.Image, string output = "png", TimeSpan? duration = null)
    {
        var input = new InputInfo(Temp.Combine("in", name), new FormatId(Path.GetExtension(name).TrimStart('.')), kind, 1000, duration, null, null, null, []);
        return new ConversionJob(input, new ConversionSettings(new FormatId(output)), Temp.Combine("out"));
    }

    /// <summary>Waits for the next converter call.</summary>
    public async Task<ConvertCall> NextCallAsync()
    {
        var ok = await _started.WaitAsync(Timeout);
        ok.ShouldBeTrue("the converter was not called in time");
        _calls.TryDequeue(out var call).ShouldBeTrue();
        return call!;
    }

    /// <summary>True when no further converter call has started (non-blocking).</summary>
    public bool NoPendingCall() => _started.CurrentCount == 0;

    /// <summary>Waits until <paramref name="condition"/> holds, re-checking on every queue event.</summary>
    public async Task WaitForAsync(Func<bool> condition)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Handler(object? s, JobChangedEventArgs e)
        {
            if (condition())
            {
                tcs.TrySetResult();
            }
        }

        Queue.JobChanged += Handler;
        try
        {
            if (condition())
            {
                return;
            }
            await tcs.Task.WaitAsync(Timeout);
        }
        finally
        {
            Queue.JobChanged -= Handler;
        }
    }

    public Task WaitForStateAsync(ConversionJob job, JobState state) => WaitForAsync(() => job.State == state);

    public async ValueTask DisposeAsync()
    {
        foreach (var call in _calls)
        {
            call.Tcs.TrySetCanceled();
        }
        await Queue.StopAsync().WaitAsync(Timeout);
        Temp.Dispose();
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int current;
        while ((current = Volatile.Read(ref target)) < value)
        {
            if (Interlocked.CompareExchange(ref target, value, current) == current)
            {
                return;
            }
        }
    }
}
