using Kvertis.App.Services;
using Kvertis.Engine.Abstractions;
using Kvertis.Engine.Formats;
using Kvertis.Engine.Naming;
using Kvertis.Queue;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Kvertis.App.Tests;

/// <summary>
/// The coordinator of one round (ADR-021). It runs against a real <see cref="JobQueue"/> with a scripted
/// converter, because <see cref="ConversionJob"/> lets only the queue change its state; that also proves the
/// filtering, the state machine and the report against the real transitions.
/// </summary>
public sealed class ConversionCoordinatorTests : IAsyncLifetime, IAsyncDisposable
{
    private static readonly FormatRegistry Registry = new();

    private readonly string _root = Path.Combine(Path.GetTempPath(), "kvertis-tests", Guid.NewGuid().ToString("N"));
    private readonly ScriptedConverter _converter = new();
    private readonly JobQueue _queue;

    public ConversionCoordinatorTests()
    {
        Directory.CreateDirectory(_root);
        var resolver = Substitute.For<IConverterResolver>();
        resolver.Resolve(Arg.Any<InputInfo>(), Arg.Any<FormatId>()).Returns(_converter);
        var validator = Substitute.For<IInputValidator>();
        validator.ValidateAsync(Arg.Any<InputInfo>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ValidationOutcome.Ok));
        var estimator = Substitute.For<IEstimator>();
        estimator.Estimate(Arg.Any<InputInfo>(), Arg.Any<ConversionSettings>())
            .Returns(new Estimate(TimeSpan.FromSeconds(1), 500, 1));

        _queue = new JobQueue(resolver, validator, estimator, new JobQueueOptions
        {
            MaxParallel = 1,
            MaxParallelVideo = 1,
            Registry = Registry,
            ProgressThrottle = TimeSpan.Zero,
        });
    }

    public Task InitializeAsync() => Task.CompletedTask;

    /// <summary>xUnit calls the Task-based overload; this one only exists so the queue counts as owned.</summary>
    ValueTask IAsyncDisposable.DisposeAsync() => new(DisposeAsync());

    public async Task DisposeAsync()
    {
        _converter.ReleaseAll();
        await _queue.DisposeAsync();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Temp folder; Windows cleans it up.
        }
    }

    // ---- helpers ---------------------------------------------------------------------------------

    private static InputInfo Input(string path) =>
        new(path, FormatRegistry.Png, MediaKind.Image, 1000, null, 100, 100, null, []);

    private static PlannedConversion Item(string path) =>
        new(Input(path), new ConversionSettings(FormatRegistry.Jpg), OutputNamePattern.Default);

    private IReadOnlyList<TargetPathPreview> Previews(string directory, params string[] names)
    {
        var plan = new TargetPlan(names.Select(n => Item(Path.Combine(_root, n))).ToList(), []);
        return TargetPathPlanner.PreviewAll(
            plan, OutputLocation.Custom(directory), null, Registry, DateTimeOffset.Now, File.Exists, null);
    }

    private ConversionCoordinator Coordinator(IUiDispatcher? ui = null) =>
        new(_queue, ui ?? TestDoubles.Dispatcher(), TimeProvider.System);

    private static async Task WaitUntil(Func<bool> condition, string what)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }
            await Task.Delay(20);
        }
        throw new TimeoutException("Timed out waiting for: " + what);
    }

    // ---- tests -----------------------------------------------------------------------------------

    [Fact]
    public async Task Start_enqueues_one_job_per_preview_with_a_running_batch_index()
    {
        _converter.Block();
        using var coordinator = Coordinator();

        coordinator.Start(Previews(_root, "a.png", "b.png", "c.png"));

        coordinator.Jobs.Count.ShouldBe(3);
        coordinator.Jobs.Select(j => j.BatchIndex).ShouldBe([1, 2, 3]);
        coordinator.Jobs.ShouldAllBe(j => j.OutputDirectory == _root);
        await WaitUntil(() => coordinator.State == RoundState.Running, "the round to run");
    }

    [Fact]
    public void Start_is_refused_while_a_file_still_needs_a_folder()
    {
        using var coordinator = Coordinator();
        var plan = new TargetPlan([Item(Path.Combine(_root, "a.png"))], []);
        var previews = TargetPathPlanner.PreviewAll(
            plan, OutputLocation.Custom(string.Empty), null, Registry, DateTimeOffset.Now, File.Exists, null);

        Should.Throw<InvalidOperationException>(() => coordinator.Start(previews));
        coordinator.Jobs.ShouldBeEmpty();
        coordinator.State.ShouldBe(RoundState.Ready);
    }

    [Fact]
    public async Task Jobs_of_another_round_are_ignored()
    {
        _converter.Block();
        var foreign = new ConversionJob(Input(Path.Combine(_root, "fremd.png")),
            new ConversionSettings(FormatRegistry.Jpg), _root);
        using var coordinator = Coordinator();
        var seen = new List<Guid>();
        coordinator.Changed += (_, e) =>
        {
            if (e.Job is { } job)
            {
                seen.Add(job.Id);
            }
        };

        coordinator.Start(Previews(_root, "a.png"));
        _queue.Enqueue(foreign);
        await WaitUntil(() => coordinator.State == RoundState.Running, "the round to run");

        coordinator.Jobs.Count.ShouldBe(1);
        seen.ShouldNotContain(foreign.Id);
        _queue.Jobs.Count.ShouldBe(2);
    }

    [Fact]
    public async Task RelocateWaiting_replaces_only_the_waiting_jobs_and_keeps_the_batch_index()
    {
        _converter.Block();
        using var coordinator = Coordinator();
        coordinator.Start(Previews(_root, "a.png", "b.png"));
        await WaitUntil(() => coordinator.Jobs.Any(j => j.State == JobState.Running), "the first job to start");

        var other = Path.Combine(_root, "anderswo");
        Directory.CreateDirectory(other);
        var replaced = coordinator.RelocateWaiting(Previews(other, "a.png", "b.png"));

        replaced.ShouldBe(1);
        coordinator.Jobs[0].OutputDirectory.ShouldBe(_root);
        coordinator.Jobs[1].OutputDirectory.ShouldBe(other);
        coordinator.Jobs[1].BatchIndex.ShouldBe(2);
    }

    [Fact]
    public void RelocateWaiting_before_the_start_changes_nothing()
    {
        using var coordinator = Coordinator();

        coordinator.RelocateWaiting(Previews(_root, "a.png")).ShouldBe(0);
    }

    [Fact]
    public async Task The_report_counts_what_happened_and_the_bytes()
    {
        using var coordinator = Coordinator();

        coordinator.Start(Previews(_root, "a.png", "b.png"));
        await WaitUntil(() => coordinator.State == RoundState.Finished, "the round to finish");

        var report = coordinator.Report.ShouldNotBeNull();
        report.Total.ShouldBe(2);
        report.Completed.ShouldBe(2);
        report.Failed.ShouldBe(0);
        report.Cancelled.ShouldBe(0);
        report.BytesIn.ShouldBe(2000);
        report.BytesOut.ShouldBe(400);
        report.Failures.ShouldBeEmpty();
        coordinator.Overall.Fraction.ShouldBe(1, 0.001);
    }

    [Fact]
    public async Task A_failure_lands_in_the_report_with_its_code()
    {
        _converter.FailWith(ConversionErrorCode.OutputNotWritable);
        using var coordinator = Coordinator();

        coordinator.Start(Previews(_root, "a.png"));
        await WaitUntil(() => coordinator.State == RoundState.Finished, "the round to finish");

        var report = coordinator.Report.ShouldNotBeNull();
        report.Failed.ShouldBe(1);
        report.Failures.Single().Error.ShouldBe(ConversionErrorCode.OutputNotWritable);
    }

    [Fact]
    public async Task Changed_is_raised_through_the_dispatcher()
    {
        var posts = 0;
        var ui = Substitute.For<IUiDispatcher>();
        ui.HasThreadAccess.Returns(true);
        ui.When(u => u.Post(Arg.Any<Action>())).Do(c =>
        {
            posts++;
            c.Arg<Action>()();
        });
        using var coordinator = Coordinator(ui);

        coordinator.Start(Previews(_root, "a.png"));
        await WaitUntil(() => coordinator.State == RoundState.Finished, "the round to finish");

        posts.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task Reset_removes_only_its_own_jobs()
    {
        var foreign = new ConversionJob(Input(Path.Combine(_root, "fremd.png")),
            new ConversionSettings(FormatRegistry.Jpg), _root);
        using var coordinator = Coordinator();
        coordinator.Start(Previews(_root, "a.png"));
        await WaitUntil(() => coordinator.State == RoundState.Finished, "the round to finish");
        _converter.Block();
        _queue.Enqueue(foreign);
        _queue.Pause(foreign.Id);

        coordinator.Reset();

        coordinator.Jobs.ShouldBeEmpty();
        coordinator.State.ShouldBe(RoundState.Ready);
        coordinator.Report.ShouldBeNull();
        _queue.Jobs.Select(j => j.Id).ShouldBe([foreign.Id]);
    }

    [Fact]
    public async Task PauseAll_and_ResumeAll_only_touch_the_round()
    {
        _converter.Block();
        using var coordinator = Coordinator();
        coordinator.Start(Previews(_root, "a.png", "b.png"));
        await WaitUntil(() => coordinator.Jobs.Any(j => j.State == JobState.Running), "the first job to start");

        coordinator.PauseAll();
        await WaitUntil(() => coordinator.State == RoundState.Paused, "the round to pause");
        coordinator.ResumeAll();
        await WaitUntil(() => coordinator.State == RoundState.Running, "the round to run again");
    }

    [Fact]
    public void Reset_is_refused_while_the_round_runs()
    {
        _converter.Block();
        using var coordinator = Coordinator();
        coordinator.Start(Previews(_root, "a.png"));

        Should.Throw<InvalidOperationException>(() => coordinator.Reset());
        coordinator.Jobs.Count.ShouldBe(1);
    }

    [Fact]
    public void A_refused_EnqueueRange_leaves_the_round_ready()
    {
        var queue = new RefusingQueue(1);
        using var coordinator = new ConversionCoordinator(queue, TestDoubles.Dispatcher(), TimeProvider.System);

        Should.Throw<InvalidOperationException>(() => coordinator.Start(Previews(_root, "a.png", "b.png")));

        coordinator.State.ShouldBe(RoundState.Ready);
        coordinator.Jobs.ShouldBeEmpty();
        coordinator.Report.ShouldBeNull();
    }

    [Fact]
    public void A_refused_relocation_puts_the_files_back_into_their_old_folder()
    {
        var queue = new RefusingQueue(2);
        using var coordinator = new ConversionCoordinator(queue, TestDoubles.Dispatcher(), TimeProvider.System);
        coordinator.Start(Previews(_root, "a.png"));
        var other = Path.Combine(_root, "anderswo");

        Should.Throw<InvalidOperationException>(() => coordinator.RelocateWaiting(Previews(other, "a.png")));

        coordinator.Jobs.Count.ShouldBe(1);
        coordinator.Jobs[0].OutputDirectory.ShouldBe(_root);
        coordinator.State.ShouldBe(RoundState.Running);
    }

    /// <summary>A queue that refuses exactly the numbered <c>EnqueueRange</c> calls and accepts the rest.</summary>
    private sealed class RefusingQueue : IJobQueue
    {
        private readonly List<ConversionJob> _jobs = [];
        private readonly HashSet<int> _refused;
        private int _calls;

        public RefusingQueue(params int[] refusedCalls)
        {
            _refused = [.. refusedCalls];
        }

        public int MaxParallel { get; set; } = 1;

        public int MaxParallelVideo { get; set; } = 1;

        public IReadOnlyList<ConversionJob> Jobs => _jobs.ToList();

        public OverallProgress Overall => OverallProgress.Compute(_jobs);

        public event EventHandler<JobChangedEventArgs>? JobChanged;

        public void Enqueue(ConversionJob job) => EnqueueRange([job]);

        public void EnqueueRange(IEnumerable<ConversionJob> jobs)
        {
            _calls++;
            var list = jobs.ToList();
            if (_refused.Contains(_calls))
            {
                throw new JobAdmissionException(AdmissionResult.Denied("test"));
            }
            _jobs.AddRange(list);
            foreach (var job in list)
            {
                JobChanged?.Invoke(this, new JobChangedEventArgs(job, JobChangeKind.Added));
            }
        }

        public bool Pause(Guid jobId) => false;

        public bool Resume(Guid jobId) => false;

        public bool Cancel(Guid jobId) => false;

        public void PauseAll()
        {
        }

        public void ResumeAll()
        {
        }

        public void CancelAll()
        {
        }

        public bool Remove(Guid jobId) => _jobs.RemoveAll(j => j.Id == jobId) > 0;

        public ConversionJob? Retry(Guid jobId) => null;

        public void ClearFinished()
        {
        }
    }

    /// <summary>A converter whose result and timing the test decides.</summary>
    private sealed class ScriptedConverter : IConverter
    {
        private readonly object _gate = new();
        private TaskCompletionSource? _gateSource;
        private ConversionErrorCode _error = ConversionErrorCode.None;

        public string Name => "scripted";

        public void Block()
        {
            lock (_gate)
            {
                _gateSource ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            }
        }

        public void ReleaseAll()
        {
            lock (_gate)
            {
                _gateSource?.TrySetResult();
                _gateSource = null;
            }
        }

        public void FailWith(ConversionErrorCode error) => _error = error;

        public bool Supports(InputInfo input, FormatId output) => true;

        public async Task<ConversionResult> ConvertAsync(
            InputInfo input, string outputPath, ConversionSettings settings,
            IProgress<ConversionProgress> progress, CancellationToken ct)
        {
            Task? wait;
            lock (_gate)
            {
                wait = _gateSource?.Task;
            }
            if (wait is not null)
            {
                await wait.WaitAsync(ct).ConfigureAwait(false);
            }
            if (_error != ConversionErrorCode.None)
            {
                throw new ConversionException(_error, input.Path, "scripted", "scripted failure");
            }
            await File.WriteAllBytesAsync(outputPath, new byte[200], ct).ConfigureAwait(false);
            progress.Report(new ConversionProgress(1, ConversionPhase.Converting));
            return new ConversionResult(outputPath, input.SizeBytes, 200, TimeSpan.FromMilliseconds(1));
        }

        public Task<PreviewResult?> PreviewAsync(InputInfo input, ConversionSettings settings, CancellationToken ct) =>
            Task.FromResult<PreviewResult?>(null);
    }
}
