using Kvertis.Engine.Processes;
using Shouldly;
using Xunit;

namespace Kvertis.Engine.Tests.Processes;

public sealed class ProcessRunnerTimeoutTests
{
    private static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(10);

    [Fact]
    public async Task Suspended_time_does_not_count_towards_the_timeout()
    {
        using var expire = new CancellationTokenSource();
        using var stop = new CancellationTokenSource();
        var suspended = true;

        var watchdog = ProcessRunner.WatchTimeoutAsync(TimeSpan.FromMilliseconds(50), Tick, () => Volatile.Read(ref suspended), expire, stop.Token);

        // Far longer than the timeout, but all of it suspended: must not expire.
        await Task.Delay(TimeSpan.FromMilliseconds(400));
        expire.IsCancellationRequested.ShouldBeFalse();

        Volatile.Write(ref suspended, false);
        await watchdog.WaitAsync(TimeSpan.FromSeconds(10));
        expire.IsCancellationRequested.ShouldBeTrue();
    }

    [Fact]
    public async Task Stopping_the_watchdog_before_the_timeout_does_not_expire()
    {
        using var expire = new CancellationTokenSource();
        using var stop = new CancellationTokenSource();

        var watchdog = ProcessRunner.WatchTimeoutAsync(TimeSpan.FromHours(1), Tick, () => false, expire, stop.Token);
        await stop.CancelAsync();
        await watchdog.WaitAsync(TimeSpan.FromSeconds(10));

        expire.IsCancellationRequested.ShouldBeFalse();
    }

    [Theory]
    [InlineData(100, 25)]
    [InlineData(20, 10)]
    [InlineData(60_000, 1_000)]
    public void Tick_is_at_most_one_second(int timeoutMs, int expectedMs)
    {
        ProcessRunner.WatchdogTickFor(TimeSpan.FromMilliseconds(timeoutMs)).ShouldBe(TimeSpan.FromMilliseconds(expectedMs));
    }

}
