namespace Kvertis.App.Services;

/// <summary>
/// Coalesces a burst of requests into one call. The target page uses it while the ring or the size bar is
/// dragged: the numbers follow every frame, the expensive parts (effects, bar colours) follow every 50 ms.
/// <see cref="TimeProvider"/> is injectable so the behaviour can be tested without a dispatcher.
/// </summary>
public sealed class Debouncer : IDisposable
{
    /// <summary>Interval of the design sketch (docs/03-architektur.md, "Ablauf beim Ziehen").</summary>
    public static readonly TimeSpan DefaultDelay = TimeSpan.FromMilliseconds(50);

    private readonly TimeSpan _delay;
    private readonly Action _action;
    private readonly Action<Action> _post;
    private readonly ITimer _timer;
    private readonly object _gate = new();
    private bool _pending;
    private bool _disposed;

    /// <param name="post">Marshals the call onto the UI thread; normally IUiDispatcher.Post.</param>
    public Debouncer(Action action, Action<Action>? post = null, TimeSpan? delay = null, TimeProvider? time = null)
    {
        _action = action ?? throw new ArgumentNullException(nameof(action));
        _post = post ?? (a => a());
        _delay = delay ?? DefaultDelay;
        _timer = (time ?? TimeProvider.System).CreateTimer(_ => Fire(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    /// <summary>Asks for one call after the delay. Further requests inside the window are folded into it.</summary>
    public void Request()
    {
        lock (_gate)
        {
            if (_disposed || _pending)
            {
                return;
            }
            _pending = true;
            _timer.Change(_delay, Timeout.InfiniteTimeSpan);
        }
    }

    /// <summary>Runs a pending call right away (the user let go of the slider).</summary>
    public void Flush()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }
            _pending = false;
            _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }
        _post(_action);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
        }
        _timer.Dispose();
    }

    private void Fire()
    {
        lock (_gate)
        {
            if (_disposed || !_pending)
            {
                return;
            }
            _pending = false;
        }
        _post(_action);
    }
}
