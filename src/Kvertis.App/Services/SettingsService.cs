using System.Text.Json;
using System.Text.Json.Serialization;
using Kvertis.Engine.Naming;

namespace Kvertis.App.Services;

public enum AppTheme
{
    System = 0,
    Light,
    Dark,
}

public enum OutputLocationKind
{
    SameFolder = 0,
    SubFolder,
    Custom,
}

/// <summary>
/// Last position, size and state of the main window, in physical pixels of the virtual desktop.
/// Null while the app has never been closed normally.
/// </summary>
public sealed record WindowPlacement(int X, int Y, int Width, int Height, bool IsMaximized);

/// <summary>User settings, stored as JSON in the local app data folder (ADR-008). Never leaves the device.</summary>
public sealed class AppSettings
{
    /// <summary>Empty = follow Windows; otherwise a BCP-47 tag ("de-DE", "en-US").</summary>
    public string Language { get; set; } = string.Empty;

    public AppTheme Theme { get; set; } = AppTheme.System;

    /// <summary>Null = automatic (number of logical processors).</summary>
    public int? MaxParallel { get; set; }

    public OutputLocationKind OutputLocation { get; set; } = OutputLocationKind.SameFolder;

    /// <summary>FutureAccessList token of the custom output folder chosen with the folder picker.</summary>
    public string? CustomOutputFolderToken { get; set; }

    /// <summary>Path of the custom output folder, for display and for the queue.</summary>
    public string? CustomOutputFolderPath { get; set; }

    public string NamePattern { get; set; } = OutputNamePattern.Default;

    /// <summary>False (default) removes EXIF/GPS data.</summary>
    public bool KeepMetadata { get; set; }

    /// <summary>User-provided ffmpeg folder (LGPL replacement right). Null = bundled build.</summary>
    public string? FfmpegDirectory { get; set; }

    /// <summary>Local diagnostic log file. Off by default; never sent anywhere.</summary>
    public bool LoggingEnabled { get; set; }

    /// <summary>Last license status seen from the Store; used when the Store cannot be reached.</summary>
    public bool LastKnownPro { get; set; }

    /// <summary>Where the main window stood when it was last closed. Null = open with the default size.</summary>
    public WindowPlacement? WindowPlacement { get; set; }

    public AppSettings Clone() => (AppSettings)MemberwiseClone();
}

public interface ISettingsService
{
    AppSettings Current { get; }

    /// <summary>Raised on the caller's thread after <see cref="UpdateAsync"/>.</summary>
    event EventHandler? Changed;

    /// <summary>Applies <paramref name="change"/> and saves the file.</summary>
    Task UpdateAsync(Action<AppSettings> change);
}

public sealed class JsonSettingsService : ISettingsService, IDisposable
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _path;
    private readonly SemaphoreSlim _saveLock = new(1, 1);

    private JsonSettingsService(string path, AppSettings settings)
    {
        _path = path;
        Current = settings;
    }

    public AppSettings Current { get; private set; }

    public event EventHandler? Changed;

    /// <summary>Loads the settings file. A missing or corrupt file yields defaults.</summary>
    public static async Task<JsonSettingsService> LoadAsync(string path)
    {
        var settings = new AppSettings();
        try
        {
            if (File.Exists(path))
            {
                var json = await File.ReadAllTextAsync(path).ConfigureAwait(false);
                settings = JsonSerializer.Deserialize<AppSettings>(json, Options) ?? new AppSettings();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            settings = new AppSettings();
        }
        if (string.IsNullOrWhiteSpace(settings.NamePattern))
        {
            settings.NamePattern = OutputNamePattern.Default;
        }
        return new JsonSettingsService(path, settings);
    }

    public async Task UpdateAsync(Action<AppSettings> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        var copy = Current.Clone();
        change(copy);
        Current = copy;
        Changed?.Invoke(this, EventArgs.Empty);
        await SaveAsync(copy).ConfigureAwait(false);
    }

    /// <summary>
    /// Applies a change and writes the file right away on the calling thread. Used while the window is closing,
    /// where an awaited save is not guaranteed to run to the end before the process exits.
    /// </summary>
    public void UpdateNow(Action<AppSettings> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        var copy = Current.Clone();
        change(copy);
        Current = copy;
        Changed?.Invoke(this, EventArgs.Empty);
        _saveLock.Wait(); // SemaphoreSlim, not a Task: nothing is being blocked on here.
        try
        {
            WriteFile(copy);
        }
        finally
        {
            _saveLock.Release();
        }
    }

    public void Dispose() => _saveLock.Dispose();

    private async Task SaveAsync(AppSettings settings)
    {
        await _saveLock.WaitAsync().ConfigureAwait(false);
        try
        {
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
            var temp = _path + ".tmp";
            await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(settings, Options)).ConfigureAwait(false);
            File.Move(temp, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Settings stay in memory for this session; the next change tries again.
        }
        finally
        {
            _saveLock.Release();
        }
    }

    private void WriteFile(AppSettings settings)
    {
        try
        {
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
            var temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(settings, Options));
            File.Move(temp, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Settings stay in memory for this session; the next change tries again.
        }
    }
}
