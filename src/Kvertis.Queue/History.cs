using System.Text.Json;
using System.Text.Json.Serialization;
using Kvertis.Engine.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kvertis.Queue;

/// <summary>One line of the conversion history (ADR-008). Holds everything "convert again with the same settings" needs.</summary>
public sealed record HistoryEntry(
    Guid Id,
    DateTimeOffset When,
    string InputPath,
    FormatId InputFormat,
    string? OutputPath,
    ConversionSettings Settings,
    bool Success,
    ConversionErrorCode Error,
    long BytesIn,
    long BytesOut,
    TimeSpan Elapsed)
{
    /// <summary>Builds an entry from a finished job.</summary>
    public static HistoryEntry FromJob(ConversionJob job, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(job);
        var result = job.Result;
        var success = job.State == JobState.Completed && result is not null;
        var elapsed = result?.Elapsed
                      ?? (job.StartedAt is { } s && job.FinishedAt is { } f ? f - s : TimeSpan.Zero);
        return new HistoryEntry(
            job.Id,
            job.FinishedAt ?? now,
            job.Input.Path,
            job.Input.Format,
            result?.OutputPath ?? job.OutputPath,
            job.Settings,
            success,
            success ? ConversionErrorCode.None : job.Error,
            result?.InputBytes ?? job.Input.SizeBytes,
            result?.OutputBytes ?? 0,
            elapsed);
    }
}

/// <summary>Persistence for <see cref="JobHistory"/>.</summary>
public interface IHistoryStore
{
    /// <summary>Returns the stored entries, newest first. Missing or corrupt storage yields an empty list.</summary>
    Task<IReadOnlyList<HistoryEntry>> LoadAsync(CancellationToken ct);

    Task SaveAsync(IReadOnlyList<HistoryEntry> entries, CancellationToken ct);
}

/// <summary>
/// Stores the history as one JSON file, newest first, capped at <see cref="MaxEntries"/>.
/// Writes go to a temp file that replaces the target, so a crash never leaves a half-written file.
/// </summary>
public sealed class JsonFileHistoryStore : IHistoryStore
{
    public const int MaxEntries = 200;

    private readonly string _path;

    public JsonFileHistoryStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = Path.GetFullPath(path);
    }

    public async Task<IReadOnlyList<HistoryEntry>> LoadAsync(CancellationToken ct)
    {
        if (!File.Exists(_path))
        {
            return [];
        }
        try
        {
            await using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
            var entries = await JsonSerializer.DeserializeAsync<List<HistoryEntry>>(stream, QueueJson.Options, ct).ConfigureAwait(false);
            return Normalize(entries ?? []);
        }
        catch (JsonException)
        {
            return []; // Corrupt history is not worth blocking the app for.
        }
        catch (NotSupportedException)
        {
            return [];
        }
    }

    public async Task SaveAsync(IReadOnlyList<HistoryEntry> entries, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
        var temp = _path + ".tmp";
        await using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true))
        {
            await JsonSerializer.SerializeAsync(stream, Normalize(entries), QueueJson.Options, ct).ConfigureAwait(false);
        }
        File.Move(temp, _path, overwrite: true);
    }

    private static List<HistoryEntry> Normalize(IEnumerable<HistoryEntry> entries) =>
        entries.Where(e => e is not null)
               .OrderByDescending(e => e.When)
               .Take(MaxEntries)
               .ToList();
}

/// <summary>
/// In-memory history, newest first, persisted through an <see cref="IHistoryStore"/>. The queue calls
/// <see cref="RecordAsync"/> for every completed or failed job (cancelled jobs are not recorded).
/// Thread-safe. <see cref="Changed"/> is raised on the recording thread; the UI must marshal.
/// </summary>
public sealed class JobHistory : IDisposable
{
    private readonly IHistoryStore _store;
    private readonly int _maxEntries;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _saveLock = new(1, 1);
    private readonly object _gate = new();
    private List<HistoryEntry> _entries = [];

    public JobHistory(IHistoryStore store, int maxEntries = JsonFileHistoryStore.MaxEntries, TimeProvider? timeProvider = null, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxEntries, 1);
        _store = store;
        _maxEntries = maxEntries;
        _time = timeProvider ?? TimeProvider.System;
        _logger = logger ?? NullLogger.Instance;
    }

    public event EventHandler? Changed;

    public IReadOnlyList<HistoryEntry> Entries
    {
        get
        {
            lock (_gate)
            {
                return _entries.ToArray();
            }
        }
    }

    public async Task LoadAsync(CancellationToken ct = default)
    {
        var loaded = await _store.LoadAsync(ct).ConfigureAwait(false);
        lock (_gate)
        {
            _entries = loaded.OrderByDescending(e => e.When).Take(_maxEntries).ToList();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public Task RecordAsync(ConversionJob job, CancellationToken ct = default) =>
        AddAsync(HistoryEntry.FromJob(job, _time.GetUtcNow()), ct);

    public async Task AddAsync(HistoryEntry entry, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        lock (_gate)
        {
            _entries.RemoveAll(e => e.Id == entry.Id);
            _entries.Insert(0, entry);
            if (_entries.Count > _maxEntries)
            {
                _entries.RemoveRange(_maxEntries, _entries.Count - _maxEntries);
            }
        }
        Changed?.Invoke(this, EventArgs.Empty);
        await SaveAsync(ct).ConfigureAwait(false);
    }

    public async Task ClearAsync(CancellationToken ct = default)
    {
        lock (_gate)
        {
            _entries.Clear();
        }
        Changed?.Invoke(this, EventArgs.Empty);
        await SaveAsync(ct).ConfigureAwait(false);
    }

    public void Dispose() => _saveLock.Dispose();

    private async Task SaveAsync(CancellationToken ct)
    {
        await _saveLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Snapshot inside the save lock so the last writer always persists the newest state.
            await _store.SaveAsync(Entries, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Saving the conversion history failed");
        }
        finally
        {
            _saveLock.Release();
        }
    }
}

/// <summary>Shared JSON settings for queue persistence (reflection-based; the files are small).</summary>
internal static class QueueJson
{
    public static readonly JsonSerializerOptions Options = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = false,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
        options.Converters.Add(new FormatIdJsonConverter());
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    /// <summary>Writes <see cref="FormatId"/> as its plain string key ("png").</summary>
    private sealed class FormatIdJsonConverter : JsonConverter<FormatId>
    {
        public override FormatId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var value = reader.GetString();
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new JsonException("Empty format id.");
            }
            return new FormatId(value);
        }

        public override void Write(Utf8JsonWriter writer, FormatId value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.Id);
    }
}
