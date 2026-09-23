using Kvertis.Engine.Abstractions;

namespace Kvertis.Queue.Tests;

public sealed class JobQueueTests
{
    [Fact]
    public async Task Never_runs_more_than_MaxParallel_jobs()
    {
        await using var h = new QueueHarness(maxParallel: 2);
        var jobs = Enumerable.Range(0, 5).Select(i => h.NewJob($"p{i}.jpg")).ToList();
        h.Queue.EnqueueRange(jobs);

        var running = new List<ConvertCall> { await h.NextCallAsync(), await h.NextCallAsync() };
        for (var completed = 0; completed < 5; completed++)
        {
            running[0].Complete();
            running.RemoveAt(0);
            if (completed < 3)
            {
                running.Add(await h.NextCallAsync());
            }
        }

        await h.WaitForAsync(() => jobs.All(j => j.State == JobState.Completed));
        h.MaxConcurrent.ShouldBe(2);
        h.Queue.Overall.Completed.ShouldBe(5);
    }

    [Fact]
    public async Task Video_jobs_respect_their_own_limit_without_blocking_other_jobs()
    {
        await using var h = new QueueHarness(maxParallel: 4, maxParallelVideo: 1);
        var video1 = h.NewJob("a.mp4", MediaKind.Video, "webm");
        var video2 = h.NewJob("b.mp4", MediaKind.Video, "webm");
        var image = h.NewJob("c.jpg");
        h.Queue.EnqueueRange([video1, video2, image]);

        var first = await h.NextCallAsync();
        var second = await h.NextCallAsync();
        new[] { first.Input.Path, second.Input.Path }.ShouldBe([video1.Input.Path, image.Input.Path], ignoreOrder: true);
        video2.State.ShouldBe(JobState.Queued);

        (first.Input.Path == video1.Input.Path ? first : second).Complete();
        var third = await h.NextCallAsync();
        third.Input.Path.ShouldBe(video2.Input.Path);
    }

    [Fact]
    public async Task Raising_MaxParallel_starts_waiting_jobs()
    {
        await using var h = new QueueHarness(maxParallel: 1);
        h.Queue.EnqueueRange([h.NewJob("a.jpg"), h.NewJob("b.jpg"), h.NewJob("c.jpg")]);
        await h.NextCallAsync();

        h.Queue.MaxParallel = 3;

        await h.NextCallAsync();
        await h.NextCallAsync();
        h.MaxConcurrent.ShouldBe(3);
    }

    [Fact]
    public async Task Lowering_MaxParallel_applies_to_new_starts_only()
    {
        await using var h = new QueueHarness(maxParallel: 2);
        var jobs = new[] { h.NewJob("a.jpg"), h.NewJob("b.jpg"), h.NewJob("c.jpg") };
        h.Queue.EnqueueRange(jobs);
        var first = await h.NextCallAsync();
        var second = await h.NextCallAsync();
        // Both start concurrently, so the call order is not the enqueue order.
        var a = first.Input.Path == jobs[0].Input.Path ? first : second;
        var b = a == first ? second : first;

        h.Queue.MaxParallel = 1;
        a.Complete();
        await h.WaitForStateAsync(jobs[0], JobState.Completed);
        h.NoPendingCall().ShouldBeTrue(); // one job still running, limit 1 → c waits
        jobs[2].State.ShouldBe(JobState.Queued);

        b.Complete();
        (await h.NextCallAsync()).Input.Path.ShouldBe(jobs[2].Input.Path);
    }

    [Fact]
    public async Task Paused_queued_job_is_skipped_and_starts_after_resume()
    {
        await using var h = new QueueHarness(maxParallel: 1);
        var a = h.NewJob("a.jpg");
        var b = h.NewJob("b.jpg");
        var c = h.NewJob("c.jpg");
        h.Queue.EnqueueRange([a, b, c]);
        var callA = await h.NextCallAsync();

        h.Queue.Pause(b.Id).ShouldBeTrue();
        b.State.ShouldBe(JobState.Paused);
        callA.Complete();

        var callC = await h.NextCallAsync();
        callC.Input.Path.ShouldBe(c.Input.Path);
        b.State.ShouldBe(JobState.Paused);

        h.Queue.Resume(b.Id).ShouldBeTrue();
        b.State.ShouldBe(JobState.Queued);
        callC.Complete();
        (await h.NextCallAsync()).Input.Path.ShouldBe(b.Input.Path);
    }

    [Fact]
    public async Task Cancel_running_job_cancels_converter_token()
    {
        await using var h = new QueueHarness();
        var job = h.NewJob();
        h.Queue.Enqueue(job);
        var call = await h.NextCallAsync();

        h.Queue.Cancel(job.Id).ShouldBeTrue();

        await h.WaitForStateAsync(job, JobState.Cancelled);
        call.Token.IsCancellationRequested.ShouldBeTrue();
        job.Error.ShouldBe(ConversionErrorCode.Cancelled);
        job.FinishedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task CancelAll_cancels_running_and_queued_jobs()
    {
        await using var h = new QueueHarness(maxParallel: 2);
        var jobs = Enumerable.Range(0, 4).Select(i => h.NewJob($"p{i}.jpg")).ToList();
        h.Queue.EnqueueRange(jobs);
        var c1 = await h.NextCallAsync();
        var c2 = await h.NextCallAsync();

        h.Queue.CancelAll();

        await h.WaitForAsync(() => jobs.All(j => j.State == JobState.Cancelled));
        c1.Token.IsCancellationRequested.ShouldBeTrue();
        c2.Token.IsCancellationRequested.ShouldBeTrue();
        h.NoPendingCall().ShouldBeTrue();
    }

    [Fact]
    public async Task ConversionException_marks_job_failed_with_code_and_detail()
    {
        await using var h = new QueueHarness();
        var job = h.NewJob();
        h.Queue.Enqueue(job);
        var call = await h.NextCallAsync();

        call.Fail(new ConversionException(ConversionErrorCode.CorruptFile, job.Input.Path, "decode", "bad header"));

        await h.WaitForStateAsync(job, JobState.Failed);
        job.Error.ShouldBe(ConversionErrorCode.CorruptFile);
        job.ErrorDetail.ShouldBe("bad header");
    }

    [Fact]
    public async Task Unknown_exception_fails_job_and_queue_keeps_running()
    {
        await using var h = new QueueHarness(maxParallel: 1);
        var bad = h.NewJob("bad.jpg");
        var good = h.NewJob("good.jpg");
        h.Queue.EnqueueRange([bad, good]);

        (await h.NextCallAsync()).Fail(new InvalidOperationException("boom"));
        await h.WaitForStateAsync(bad, JobState.Failed);
        bad.Error.ShouldBe(ConversionErrorCode.Unknown);
        bad.ErrorDetail!.ShouldContain("boom");

        (await h.NextCallAsync()).Complete();
        await h.WaitForStateAsync(good, JobState.Completed);
    }

    [Fact]
    public async Task Rejected_input_fails_with_validator_code_without_calling_converter()
    {
        await using var h = new QueueHarness();
        var job = h.NewJob("secret.pdf", MediaKind.Document, "txt");
        h.Validator.ValidateAsync(Arg.Is<InputInfo>(i => i.Path == job.Input.Path), Arg.Any<CancellationToken>())
            .Returns(ValidationOutcome.Rejected(ConversionErrorCode.ProtectedFile, "encrypted"));

        h.Queue.Enqueue(job);

        await h.WaitForStateAsync(job, JobState.Failed);
        job.Error.ShouldBe(ConversionErrorCode.ProtectedFile);
        job.ErrorDetail.ShouldBe("encrypted");
        h.NoPendingCall().ShouldBeTrue();
    }

    [Fact]
    public async Task Missing_converter_fails_with_UnsupportedFormat()
    {
        await using var h = new QueueHarness();
        h.Resolver.Resolve(default!, default).ReturnsForAnyArgs((IConverter?)null);
        var job = h.NewJob();

        h.Queue.Enqueue(job);

        await h.WaitForStateAsync(job, JobState.Failed);
        job.Error.ShouldBe(ConversionErrorCode.UnsupportedFormat);
    }

    [Fact]
    public async Task Output_name_collisions_get_numbered_suffixes()
    {
        await using var h = new QueueHarness(maxParallel: 2);
        var outDir = h.Temp.Combine("out");
        Directory.CreateDirectory(outDir);
        await File.WriteAllTextAsync(Path.Combine(outDir, "photo.png"), "existing");
        var a = h.NewJob("photo.jpg");
        var b = h.NewJob("photo.jpg");

        h.Queue.EnqueueRange([a, b]);
        var c1 = await h.NextCallAsync();
        var c2 = await h.NextCallAsync();

        new[] { c1.OutputPath, c2.OutputPath }.ShouldBe(
            [Path.Combine(outDir, "photo_1.png"), Path.Combine(outDir, "photo_2.png")], ignoreOrder: true);
        a.OutputPath.ShouldNotBe(b.OutputPath);
    }

    [Fact]
    public async Task Progress_events_are_throttled_but_phase_changes_always_reported()
    {
        var time = new ManualTimeProvider();
        await using var h = new QueueHarness(time: time);
        var job = h.NewJob();
        h.Queue.Enqueue(job);
        var call = await h.NextCallAsync();
        int ProgressEvents() => h.Events.Count(e => e.Job == job && e.ChangeKind == JobChangeKind.Progress);
        var afterAnalyzing = ProgressEvents();
        afterAnalyzing.ShouldBe(1); // Analyzing

        for (var i = 1; i <= 1000; i++)
        {
            call.Progress.Report(new ConversionProgress(i / 2000.0, ConversionPhase.Converting));
        }
        ProgressEvents().ShouldBe(afterAnalyzing + 1); // only the phase change
        job.Progress.Fraction.ShouldBe(0.5);

        time.Advance(TimeSpan.FromMilliseconds(250));
        call.Progress.Report(new ConversionProgress(0.6, ConversionPhase.Converting));
        ProgressEvents().ShouldBe(afterAnalyzing + 2);

        call.Progress.Report(new ConversionProgress(0.9, ConversionPhase.Finalizing));
        ProgressEvents().ShouldBe(afterAnalyzing + 3);
    }

    [Fact]
    public async Task Retry_enqueues_a_new_job_with_the_same_settings()
    {
        await using var h = new QueueHarness();
        var job = h.NewJob();
        h.Queue.Enqueue(job);
        (await h.NextCallAsync()).Fail(new ConversionException(ConversionErrorCode.ToolFailed));
        await h.WaitForStateAsync(job, JobState.Failed);

        var retry = h.Queue.Retry(job.Id);

        retry.ShouldNotBeNull();
        retry.Id.ShouldNotBe(job.Id);
        retry.Settings.ShouldBe(job.Settings);
        retry.Input.ShouldBe(job.Input);
        h.Queue.Jobs.ShouldNotContain(job);
        (await h.NextCallAsync()).Complete();
        await h.WaitForStateAsync(retry, JobState.Completed);
        h.Queue.Retry(retry.Id).ShouldBeNull(); // completed jobs cannot be retried
    }

    [Fact]
    public async Task Admission_policy_rejection_throws_and_enqueues_nothing()
    {
        var policy = Substitute.For<IJobAdmissionPolicy>();
        policy.Check(default!).ReturnsForAnyArgs(AdmissionResult.Denied("video-requires-pro"));
        await using var h = new QueueHarness(policy: policy);

        var ex = Should.Throw<InvalidOperationException>(() => h.Queue.EnqueueRange([h.NewJob("a.jpg"), h.NewJob("b.jpg")]));

        ex.ShouldBeOfType<JobAdmissionException>().Result.Reason.ShouldBe("video-requires-pro");
        h.Queue.Jobs.ShouldBeEmpty();
        policy.Received(1).Check(Arg.Is<IReadOnlyList<ConversionJob>>(l => l.Count == 2));
    }

    [Fact]
    public async Task StopAsync_cancels_running_jobs_and_completes()
    {
        var h = new QueueHarness(maxParallel: 1);
        var running = h.NewJob("a.jpg");
        var waiting = h.NewJob("b.jpg");
        h.Queue.EnqueueRange([running, waiting]);
        var call = await h.NextCallAsync();

        await h.Queue.StopAsync().WaitAsync(QueueHarness.Timeout);

        call.Token.IsCancellationRequested.ShouldBeTrue();
        running.State.ShouldBe(JobState.Cancelled);
        waiting.State.ShouldBe(JobState.Queued);
        h.NoPendingCall().ShouldBeTrue();
        await h.DisposeAsync();
    }

    [Fact]
    public async Task Pause_running_job_suspends_in_place_when_pauser_can()
    {
        var pauser = Substitute.For<IJobPauser>();
        pauser.TryPause(default!).ReturnsForAnyArgs(true);
        pauser.TryResume(default!).ReturnsForAnyArgs(true);
        await using var h = new QueueHarness(pauser: pauser);
        var job = h.NewJob();
        h.Queue.Enqueue(job);
        var call = await h.NextCallAsync();

        h.Queue.Pause(job.Id).ShouldBeTrue();
        job.State.ShouldBe(JobState.Paused);
        call.Token.IsCancellationRequested.ShouldBeFalse();

        h.Queue.Resume(job.Id).ShouldBeTrue();
        job.State.ShouldBe(JobState.Running);
        pauser.Received(1).TryResume(job);

        call.Complete();
        await h.WaitForStateAsync(job, JobState.Completed);
    }

    [Fact]
    public async Task Pause_running_job_falls_back_to_cancel_and_requeue_at_front()
    {
        await using var h = new QueueHarness(maxParallel: 1); // default pauser cannot suspend
        var a = h.NewJob("a.jpg");
        var b = h.NewJob("b.jpg");
        h.Queue.EnqueueRange([a, b]);
        var callA = await h.NextCallAsync();
        callA.Progress.Report(new ConversionProgress(0.5, ConversionPhase.Converting));

        h.Queue.Pause(a.Id).ShouldBeTrue();

        a.State.ShouldBe(JobState.Paused);
        callA.Token.IsCancellationRequested.ShouldBeTrue();
        var callB = await h.NextCallAsync(); // slot freed for b
        callB.Input.Path.ShouldBe(b.Input.Path);
        a.State.ShouldBe(JobState.Paused);
        a.Progress.Fraction.ShouldBe(0.5);
        a.OutputPath.ShouldBeNull();

        h.Queue.Resume(a.Id).ShouldBeTrue();
        callB.Complete();
        var restart = await h.NextCallAsync();
        restart.Input.Path.ShouldBe(a.Input.Path);
        a.State.ShouldBe(JobState.Running);
        restart.Complete();
        await h.WaitForStateAsync(a, JobState.Completed);
    }

    [Fact]
    public async Task Paused_job_goes_before_older_queued_jobs_after_fallback()
    {
        await using var h = new QueueHarness(maxParallel: 1);
        var a = h.NewJob("a.jpg");
        var b = h.NewJob("b.jpg");
        var c = h.NewJob("c.jpg");
        h.Queue.EnqueueRange([a, b, c]);
        await h.NextCallAsync();
        h.Queue.Pause(a.Id);
        var callB = await h.NextCallAsync();

        h.Queue.Resume(a.Id);
        callB.Complete();

        (await h.NextCallAsync()).Input.Path.ShouldBe(a.Input.Path);
    }

    [Fact]
    public async Task ProcessSuspendJobPauser_suspends_the_process_the_job_started()
    {
        var suspender = Substitute.For<IProcessSuspender>();
        suspender.TrySuspend(4242).Returns(true);
        suspender.TryResume(4242).Returns(true);
        using var pauser = new ProcessSuspendJobPauser(suspender, processRunner: null);
        await using var h = new QueueHarness(pauser: pauser);
        h.OnConvert = _ => pauser.OnProcessStarted(4242);
        var job = h.NewJob("clip.mp4", MediaKind.Video, "webm");
        h.Queue.Enqueue(job);
        var call = await h.NextCallAsync();

        h.Queue.Pause(job.Id).ShouldBeTrue();
        h.Queue.Resume(job.Id).ShouldBeTrue();

        suspender.Received(1).TrySuspend(4242);
        suspender.Received(1).TryResume(4242);
        call.Token.IsCancellationRequested.ShouldBeFalse();
        call.Complete();
        await h.WaitForStateAsync(job, JobState.Completed);
        pauser.TryPause(job).ShouldBeFalse(); // forgotten after the job finished
    }

    [Fact]
    public async Task Successful_job_records_speed_and_history()
    {
        var store = Substitute.For<IHistoryStore>();
        var speed = Substitute.For<ISpeedProfileStore>();
        using var history = new JobHistory(store);
        await using var h = new QueueHarness(history: history, speedStore: speed);
        var job = h.NewJob();
        h.Queue.Enqueue(job);

        (await h.NextCallAsync()).Complete(inBytes: 1000, outBytes: 300);
        await h.WaitForStateAsync(job, JobState.Completed);
        await h.WaitForAsync(() => history.Entries.Count == 1);

        h.Estimator.Received(1).Record(job.Input, job.Settings, Arg.Any<ConversionResult>());
        speed.Received(1).RequestSave();
        var entry = history.Entries[0];
        entry.Id.ShouldBe(job.Id);
        entry.Success.ShouldBeTrue();
        entry.BytesOut.ShouldBe(300);
        await store.Received(1).SaveAsync(Arg.Any<IReadOnlyList<HistoryEntry>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Remove_refuses_running_jobs_and_ClearFinished_drops_finished_ones()
    {
        await using var h = new QueueHarness(maxParallel: 1);
        var a = h.NewJob("a.jpg");
        var b = h.NewJob("b.jpg");
        var c = h.NewJob("c.jpg");
        h.Queue.EnqueueRange([a, b, c]);
        var callA = await h.NextCallAsync();

        h.Queue.Remove(a.Id).ShouldBeFalse();
        h.Queue.Remove(b.Id).ShouldBeTrue();
        callA.Complete();
        var callC = await h.NextCallAsync();
        callC.Input.Path.ShouldBe(c.Input.Path);
        callC.Complete();
        await h.WaitForStateAsync(c, JobState.Completed);

        h.Queue.ClearFinished();
        h.Queue.Jobs.ShouldBeEmpty();
    }

    [Fact]
    public async Task Enqueue_estimates_jobs_for_overall_progress()
    {
        await using var h = new QueueHarness(maxParallel: 1);
        h.Estimator.Estimate(default!, default!).ReturnsForAnyArgs(new Estimate(TimeSpan.FromSeconds(10), 100, 0.5));
        var a = h.NewJob("a.jpg");
        var b = h.NewJob("b.jpg");
        h.Queue.EnqueueRange([a, b]);
        var call = await h.NextCallAsync();

        call.Progress.Report(new ConversionProgress(0.5, ConversionPhase.Converting));
        var overall = h.Queue.Overall;

        overall.Fraction.ShouldBe(0.25, 1e-9);
        overall.Remaining.ShouldBe(TimeSpan.FromSeconds(15));
        overall.Running.ShouldBe(1);
        overall.Pending.ShouldBe(1);
    }
}
