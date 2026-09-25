using Kvertis.App.Scenes;
using Kvertis.Queue;

namespace Kvertis.App.ViewModels.Convert;

/// <summary>What the feed needs to know about one job of the round, in plan order.</summary>
public readonly record struct SwirlJobView(JobState State, float Fraction);

/// <summary>
/// Translates the state of the round into commands for the <see cref="SwirlScene"/> (ADR-023, worksheet
/// "Anbindung an den Koordinator"): a job that starts running takes a swirl slot, its progress colours the
/// pixels, its end sends them to the white hole, the bag or back onto the stack, and 1.05 s after the last file
/// the finale begins. Free of WinUI so the wiring is tested without the app host; the scene ids are the feed's
/// own, so a job replaced by <c>RelocateWaiting</c> keeps its sheet.
/// </summary>
public sealed class SwirlFeed : IDisposable
{
    /// <summary>The last pixel stream needs this long to land before the finale starts.</summary>
    public static readonly TimeSpan FinaleDelay = TimeSpan.FromSeconds(1.05);

    private readonly TimeProvider _time;
    private readonly object _gate = new();
    private readonly List<Guid> _ids = [];
    private readonly List<bool> _own = [];
    private readonly List<JobState?> _known = [];
    private readonly List<float> _fraction = [];

    private ITimer? _finaleTimer;
    private bool _disposed;

    public SwirlFeed(SwirlScene scene, TimeProvider? time = null)
    {
        Scene = scene ?? throw new ArgumentNullException(nameof(scene));
        _time = time ?? TimeProvider.System;
    }

    public SwirlScene Scene { get; }

    /// <summary>True from <see cref="RoundFinished"/> with an animated finale until <see cref="Clear"/>.</summary>
    public bool IsFinalePending { get; private set; }

    /// <summary>True once <see cref="BeginFinale"/> was handed to the scene.</summary>
    public bool FinaleStarted { get; private set; }

    /// <summary>Replaces the plan: the stack and the capacity of the white hole. Allowed only while nothing runs.</summary>
    public void SetPlan(IReadOnlyList<SwirlFileSpec> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        CancelFinale();
        _ids.Clear();
        _own.Clear();
        _known.Clear();
        _fraction.Clear();
        foreach (var file in files)
        {
            _ids.Add(file.Id);
            _own.Add(file.HasOwnLocation);
            _known.Add(null);
            _fraction.Add(0f);
        }

        IsFinalePending = false;
        FinaleStarted = false;
        Scene.Enqueue(new SetPlan(files));
    }

    /// <summary>The ids the feed gave the files of the plan, in plan order.</summary>
    public IReadOnlyList<Guid> Ids => _ids;

    /// <summary>
    /// Compares the jobs (plan order) with what the scene already knows and sends only the differences:
    /// Running → <see cref="BeginFile"/> once, a moved fraction → <see cref="SetProgress"/>, a final state →
    /// <see cref="FinishFile"/> once.
    /// </summary>
    public void Apply(ReadOnlySpan<SwirlJobView> jobs)
    {
        var n = Math.Min(jobs.Length, _ids.Count);
        for (var i = 0; i < n; i++)
        {
            var job = jobs[i];
            var known = _known[i];
            if (known is JobState.Completed or JobState.Failed or JobState.Cancelled)
            {
                continue;
            }

            var id = _ids[i];
            switch (job.State)
            {
                case JobState.Running:
                    if (known is not (JobState.Running or JobState.Paused))
                    {
                        Scene.Enqueue(new BeginFile(id));
                    }

                    if (known != JobState.Running || MathF.Abs(job.Fraction - _fraction[i]) > 0.0005f)
                    {
                        Scene.Enqueue(new SetProgress(id, Math.Clamp(job.Fraction, 0f, 1f)));
                        _fraction[i] = job.Fraction;
                    }

                    _known[i] = JobState.Running;
                    break;

                case JobState.Paused:
                    // A paused file keeps its slot and its colour; the queue may also pause a job that never
                    // started, and that one stays on the stack.
                    if (known == JobState.Running)
                    {
                        _known[i] = JobState.Paused;
                    }

                    break;

                case JobState.Completed:
                    Scene.Enqueue(new FinishFile(id, FileOutcome.Completed, _own[i] ? PixelTarget.Bag : PixelTarget.WhiteHole));
                    _known[i] = JobState.Completed;
                    break;

                case JobState.Failed:
                    Scene.Enqueue(new FinishFile(id, FileOutcome.Failed, PixelTarget.Inbox));
                    _known[i] = JobState.Failed;
                    break;

                case JobState.Cancelled:
                    Scene.Enqueue(new FinishFile(id, FileOutcome.Cancelled, PixelTarget.Inbox));
                    _known[i] = JobState.Cancelled;
                    break;

                default:
                    _known[i] = job.State;
                    break;
            }
        }
    }

    /// <summary>
    /// The round is over. A cancelled round gets no finale (the files simply lie on the stack again); otherwise
    /// <see cref="BeginFinale"/> follows after <see cref="FinaleDelay"/>. Returns whether a finale is pending.
    /// </summary>
    public bool RoundFinished(int completed, int failed, int cancelled) => RoundFinished(completed, failed, cancelled, animated: true);

    /// <summary>
    /// As <see cref="RoundFinished(int, int, int)"/>; with <paramref name="animated"/> false (reduced motion,
    /// high contrast, drawing failed) no timer is started and nothing stays pending.
    /// </summary>
    public bool RoundFinished(int completed, int failed, int cancelled, bool animated)
    {
        CancelFinale();
        if (!animated || cancelled > 0 || _ids.Count == 0 || completed + failed == 0)
        {
            IsFinalePending = false;
            return false;
        }

        IsFinalePending = true;
        FinaleStarted = false;
        lock (_gate)
        {
            if (_disposed)
            {
                return false;
            }

            _finaleTimer = _time.CreateTimer(
                _ => StartFinale(completed, failed),
                null,
                FinaleDelay,
                Timeout.InfiniteTimeSpan);
        }

        return true;
    }

    /// <summary>"Neue Runde" or a new plan: files, white hole and finale are dropped.</summary>
    public void Clear()
    {
        CancelFinale();
        _ids.Clear();
        _own.Clear();
        _known.Clear();
        _fraction.Clear();
        IsFinalePending = false;
        FinaleStarted = false;
        Scene.Enqueue(new SwirlClear());
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
        }

        CancelFinale();
    }

    // Runs on a timer thread; Enqueue is thread safe and the scene applies the command on its own loop.
    private void StartFinale(int completed, int failed)
    {
        lock (_gate)
        {
            if (_disposed || _finaleTimer is null)
            {
                return;
            }

            _finaleTimer.Dispose();
            _finaleTimer = null;
        }

        FinaleStarted = true;
        Scene.Enqueue(new BeginFinale(completed, failed));
    }

    private void CancelFinale()
    {
        ITimer? timer;
        lock (_gate)
        {
            timer = _finaleTimer;
            _finaleTimer = null;
        }

        timer?.Dispose();
    }
}
