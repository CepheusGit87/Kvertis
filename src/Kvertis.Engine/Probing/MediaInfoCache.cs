using System.Collections.Concurrent;
using System.Globalization;
using Kvertis.Engine.Ffmpeg;

namespace Kvertis.Engine.Probing;

/// <summary>
/// ffprobe results keyed by full path + size + last write time, so converters can read codec names
/// (HEVC, H.264, VFR) without running ffprobe a second time. A changed file gets a new key.
/// Register as singleton and share between <see cref="MediaProber"/> and the converters.
/// </summary>
public sealed class MediaInfoCache
{
    private const int MaxEntries = 2048;

    private readonly ConcurrentDictionary<string, MediaInfo> _entries = new(StringComparer.Ordinal);

    public int Count => _entries.Count;

    public bool TryGet(string path, out MediaInfo info)
    {
        if (KeyFor(path) is { } key && _entries.TryGetValue(key, out var found))
        {
            info = found;
            return true;
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
        if (_entries.Count >= MaxEntries)
        {
            _entries.Clear(); // Simple bound for very long sessions; entries are cheap to recompute.
        }
        _entries[key] = info;
    }

    public void Clear() => _entries.Clear();

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
}
