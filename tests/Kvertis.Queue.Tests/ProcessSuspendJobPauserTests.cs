using Kvertis.Engine.Abstractions;
using Kvertis.Engine.Processes;

namespace Kvertis.Queue.Tests;

public sealed class ProcessSuspendJobPauserTests
{
    private static ConversionJob NewJob()
    {
        var input = new InputInfo(Path.Combine(Path.GetTempPath(), "clip.mp4"), new FormatId("mp4"), MediaKind.Video, 1000, null, null, null, null, []);
        return new ConversionJob(input, new ConversionSettings(new FormatId("webm")), Path.GetTempPath());
    }

    [Fact]
    public void Exited_process_id_is_forgotten_and_never_suspended()
    {
        var suspender = Substitute.For<IProcessSuspender>();
        suspender.TrySuspend(Arg.Any<int>()).Returns(true);
        using var pauser = new ProcessSuspendJobPauser(suspender, processRunner: null);
        var job = NewJob();
        JobExecutionContext.Set(job);
        try
        {
            pauser.OnProcessStarted(4242);
        }
        finally
        {
            JobExecutionContext.Set(null);
        }

        pauser.OnProcessExited(4242);

        pauser.TryPause(job).ShouldBeFalse();
        suspender.DidNotReceiveWithAnyArgs().TrySuspend(default);
    }

    [Fact]
    public void Exit_of_another_process_keeps_the_job_process()
    {
        var suspender = Substitute.For<IProcessSuspender>();
        suspender.TrySuspend(4242).Returns(true);
        using var pauser = new ProcessSuspendJobPauser(suspender, processRunner: null);
        var job = NewJob();
        JobExecutionContext.Set(job);
        try
        {
            pauser.OnProcessStarted(4242);
        }
        finally
        {
            JobExecutionContext.Set(null);
        }

        pauser.OnProcessExited(1111);

        pauser.TryPause(job).ShouldBeTrue();
    }

    [Fact]
    public async Task Real_runner_reports_exit_so_the_pid_is_dropped()
    {
        var runner = new ProcessRunner();
        var suspender = Substitute.For<IProcessSuspender>();
        suspender.TrySuspend(Arg.Any<int>()).Returns(true);
        using var pauser = new ProcessSuspendJobPauser(suspender, runner);
        int? started = null;
        int? exited = null;
        runner.ProcessStarted += (_, pid) => started = pid;
        runner.ProcessExited += (_, pid) => exited = pid;
        var job = NewJob();

        JobExecutionContext.Set(job);
        try
        {
            var outcome = await runner.RunAsync(new ProcessRequest("dotnet", ["--version"], TimeSpan.FromSeconds(60)), null, CancellationToken.None);
            outcome.Succeeded.ShouldBeTrue();
        }
        finally
        {
            JobExecutionContext.Set(null);
        }

        started.ShouldNotBeNull();
        exited.ShouldBe(started);
        pauser.TryPause(job).ShouldBeFalse();
        suspender.DidNotReceiveWithAnyArgs().TrySuspend(default);
    }
}
