using Kvertis.App.Services;
using Shouldly;
using Xunit;

namespace Kvertis.App.Tests;

/// <summary>
/// The 50 ms coalescing of the target page: the numbers follow every frame, the expensive parts once per
/// window (docs/03-architektur.md, "Ablauf beim Ziehen").
/// </summary>
public sealed class DebouncerTests
{
    [Fact]
    public void A_burst_of_requests_becomes_one_call()
    {
        var time = new TestTimeProvider();
        var calls = 0;
        using var debouncer = new Debouncer(() => calls++, time: time);

        for (var i = 0; i < 20; i++)
        {
            debouncer.Request();
        }
        calls.ShouldBe(0);

        time.FireDueTimers();

        calls.ShouldBe(1);
    }

    [Fact]
    public void A_new_window_calls_again()
    {
        var time = new TestTimeProvider();
        var calls = 0;
        using var debouncer = new Debouncer(() => calls++, time: time);

        debouncer.Request();
        time.FireDueTimers();
        debouncer.Request();
        time.FireDueTimers();

        calls.ShouldBe(2);
    }

    [Fact]
    public void Flush_calls_right_away_and_cancels_the_pending_window()
    {
        var time = new TestTimeProvider();
        var calls = 0;
        using var debouncer = new Debouncer(() => calls++, time: time);

        debouncer.Request();
        debouncer.Flush();
        calls.ShouldBe(1);

        time.FireDueTimers();

        calls.ShouldBe(1);
    }

    [Fact]
    public void After_dispose_nothing_runs()
    {
        var time = new TestTimeProvider();
        var calls = 0;
        var debouncer = new Debouncer(() => calls++, time: time);

        debouncer.Request();
        debouncer.Dispose();
        time.FireDueTimers();
        debouncer.Flush();

        calls.ShouldBe(0);
    }

    /// <summary>A time provider whose timers only fire when the test says so.</summary>
    private sealed class TestTimeProvider : TimeProvider
    {
        private readonly List<TestTimer> _timers = [];

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new TestTimer(callback, state);
            _timers.Add(timer);
            return timer;
        }

        public void FireDueTimers()
        {
            foreach (var timer in _timers.ToList())
            {
                timer.FireIfDue();
            }
        }

        private sealed class TestTimer : ITimer
        {
            private readonly TimerCallback _callback;
            private readonly object? _state;
            private bool _due;
            private bool _disposed;

            public TestTimer(TimerCallback callback, object? state)
            {
                _callback = callback;
                _state = state;
            }

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                _due = dueTime != Timeout.InfiniteTimeSpan;
                return true;
            }

            public void FireIfDue()
            {
                if (_disposed || !_due)
                {
                    return;
                }
                _due = false;
                _callback(_state);
            }

            public void Dispose() => _disposed = true;

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }
}
