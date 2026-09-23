using Kvertis.Engine.Estimation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kvertis.Queue;

/// <summary>Persists the estimator's <see cref="SpeedProfile"/>. The queue calls <see cref="RequestSave"/> after every recorded job.</summary>
public interface ISpeedProfileStore
{
    /// <summary>Asks for a save. Implementations debounce; this never blocks and never throws.</summary>
    void RequestSave();
}

/// <summary>
/// Loads a <see cref="SpeedProfile"/> from a JSON file and writes it back debounced (default 2 s after the
/// last request), so a batch of 500 images causes one write instead of 500.
/// </summary>
public sealed class JsonFileSpeedProfileStore : ISpeedProfileStore, IAsyncDisposable
{
    public static readonly TimeSpan DefaultDebounce = TimeSpan.FromSeconds(2);

    private readonly string _path;
    private readonly TimeSpan _debounce;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly object _gate = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private CancellationTokenSource? _pendingDelay;
    private Task _pendingSave = Task.CompletedTask;
    private bool _dirty;

    public JsonFileSpeedProfileStore(string path, TimeSpan? debounce = null, TimeProvider? timeProvider = null, ILogger? logger = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = Path.GetFullPath(path);
        _debounce = debounce ?? DefaultDebounce;
        _time = timeProvider ?? TimeProvider.System;
        _logger = logger ?? NullLogger.Instance;
        Profile = new SpeedProfile();
    }

    /// <summary>The profile being persisted. Replaced by <see cref="LoadAsync"/>; hand it to <see cref="Estimator"/> after loading.</summary>
    public SpeedProfile Profile { get; private set; }

    public async Task<SpeedProfile> LoadAsync(CancellationToken ct = default)
    {
        string? json = null;
        try
        {
            if (File.Exists(_path))
            {
                json = await File.ReadAllTextAsync(_path, ct).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Reading the speed profile failed; starting with defaults");
        }
        Profile = SpeedProfile.FromJson(json);
        return Profile;
    }

    public void RequestSave()
    {
        lock (_gate)
        {
            _dirty = true;
            _pendingDelay?.Cancel();
            _pendingDelay?.Dispose();
            var cts = new CancellationTokenSource();
            _pendingDelay = cts;
            _pendingSave = SaveAfterDelayAsync(cts.Token);
        }
    }

    /// <summary>Writes pending changes now (app shutdown, tests).</summary>
    public async Task FlushAsync(CancellationToken ct = default)
    {
        lock (_gate)
        {
            _pendingDelay?.Cancel();
        }
        await WriteIfDirtyAsync(ct).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        await FlushAsync().ConfigureAwait(false);
        lock (_gate)
        {
            _pendingDelay?.Dispose();
            _pendingDelay = null;
        }
        _writeLock.Dispose();
    }

    /// <summary>Completes when the currently scheduled debounced save has run or was superseded.</summary>
    internal Task PendingSave
    {
        get
        {
            lock (_gate)
            {
                return _pendingSave;
            }
        }
    }

    private async Task SaveAfterDelayAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(_debounce, _time, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return; // Superseded by a newer request or flushed.
        }
        await WriteIfDirtyAsync(CancellationToken.None).ConfigureAwait(false);
    }

    private async Task WriteIfDirtyAsync(CancellationToken ct)
    {
        await _writeLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            lock (_gate)
            {
                if (!_dirty)
                {
                    return;
                }
                _dirty = false;
            }
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
            var temp = _path + ".tmp";
            await File.WriteAllTextAsync(temp, Profile.ToJson(), ct).ConfigureAwait(false);
            File.Move(temp, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Saving the speed profile failed");
            lock (_gate)
            {
                _dirty = true; // Try again with the next request or flush.
            }
        }
        finally
        {
            _writeLock.Release();
        }
    }
}
