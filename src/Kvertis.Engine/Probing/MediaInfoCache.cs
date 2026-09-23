using System.Globalization;
using Kvertis.Engine.Abstractions;
using Kvertis.Engine.Ffmpeg;

namespace Kvertis.Engine.Probing;

/// <summary>
/// ffprobe results keyed by full path + size + last write time, so converters can read codec names
/// (HEVC, H.264, VFR) without running ffprobe a second time. A changed file gets a new key.
/// Bounded LRU: when full, only the least recently used entry is evicted (never the whole cache), so the
/// routing data of queued jobs survives long sessions. Register as singleton and share between
/// <see cref="MediaProber"/>, the ffmpeg converters and the system transcoder.
/// </summary>
public sealed class MediaInfoCache
{
    public const int DefaultCapacity = 2048;

    private readonly object _gate = new();
    private readonly Dictionary<string, LinkedListNode<Entry>> _entries = new(StringComparer.Ordinal);
    private readonly LinkedList<Entry> _order = new();
    private readonly int _capacity;
    private Func<InputInfo, CancellationToken, Task<MediaInfo?>>? _prober;

    public MediaInfoCache()
        : this(DefaultCapacity)
    {
    }

    public MediaInfoCache(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _capacity = capacity;
    }

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _entries.Count;
            }
        }
    }

    public bool TryGet(string path, out MediaInfo info)
    {
        if (KeyFor(path) is { } key)
        {
            lock (_gate)
            {
                if (_entries.TryGetValue(key, out var node))
                {
                    _order.Remove(node);
                    _order.AddFirst(node);
                    info = node.Value.Info;
                    return true;
                }
            }
        }
        info = null!;
        return false;
    }

    public void Set(string path, MediaInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        if (KeyFor(path) is not { } key)
        {
            return;
        }
        lock (_gate)
        {
            if (_entries.TryGetValue(key, out var existing))
            {
                _order.Remove(existing);
                _entries.Remove(key);
            }
            while (_entries.Count >= _capacity && _order.Last is { } oldest)
            {
                _order.RemoveLast();
                _entries.Remove(oldest.Value.Key);
            }
            _entries[key] = _order.AddFirst(new Entry(key, info));
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _entries.Clear();
            _order.Clear();
        }
    }

    /// <summary>
    /// Registers the ffprobe fallback used by <see cref="GetOrProbeAsync"/> (the <see cref="FfmpegToolset"/> does
    /// this when it is created). The first registration wins.
    /// </summary>
    public void AttachProber(Func<InputInfo, CancellationToken, Task<MediaInfo?>> prober)
    {
        ArgumentNullException.ThrowIfNull(prober);
        Interlocked.CompareExchange(ref _prober, prober, null);
    }

    /// <summary>
    /// Cached probe data, or a fresh ffprobe run through the attached prober; null when neither is available or
    /// ffprobe cannot read the file. ProtectedFile and Cancelled propagate.
    /// </summary>
    public async Task<MediaInfo?> GetOrProbeAsync(InputInfo input, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (TryGet(input.Path, out var cached))
        {
            return cached;
        }
        var prober = Volatile.Read(ref _prober);
        return prober is null ? null : await prober(input, ct).ConfigureAwait(false);
    }

    internal static string? KeyFor(string path)
    {
        try
        {
            var file = new FileInfo(path);
            if (!file.Exists)
            {
                return null;
            }
            return string.Create(CultureInfo.InvariantCulture, $"{file.FullName}|{file.Length}|{file.LastWriteTimeUtc.Ticks}");
        }
        catch (Exception)
        {
            return null;
        }
    }

    private sealed record Entry(string Key, MediaInfo Info);
}
