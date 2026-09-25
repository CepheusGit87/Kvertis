using Microsoft.Extensions.Logging;
using Windows.Storage;
using Windows.System;

namespace Kvertis.App.Services;

/// <summary>
/// "Öffnen" and "Im Ordner zeigen" for a finished file. Uses the WinRT launcher, which asks the shell and
/// needs no extra capability; it never starts a process of its own.
/// </summary>
public interface IShellLauncher
{
    /// <summary>Opens the file with the application the user has for that type.</summary>
    Task OpenFileAsync(string path);

    /// <summary>Opens the containing folder with the file selected.</summary>
    Task ShowInFolderAsync(string path);

    Task OpenFolderAsync(string directory);
}

/// <inheritdoc cref="IShellLauncher"/>
public sealed class ShellLauncher : IShellLauncher
{
    private readonly ILogger<ShellLauncher> _logger;

    public ShellLauncher(ILogger<ShellLauncher> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task OpenFileAsync(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            return;
        }
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(path);
            await Launcher.LaunchFileAsync(file);
        }
        catch (Exception ex)
        {
            _logger.LogInformation(ex, "Opening a converted file failed");
        }
    }

    public async Task ShowInFolderAsync(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return;
        }
        var directory = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
        {
            return;
        }
        try
        {
            var folder = await StorageFolder.GetFolderFromPathAsync(directory);
            var options = new FolderLauncherOptions();
            if (File.Exists(path))
            {
                options.ItemsToSelect.Add(await StorageFile.GetFileFromPathAsync(path));
            }
            await Launcher.LaunchFolderAsync(folder, options);
        }
        catch (Exception ex)
        {
            _logger.LogInformation(ex, "Showing a converted file in its folder failed");
        }
    }

    public async Task OpenFolderAsync(string directory)
    {
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
        {
            return;
        }
        try
        {
            var folder = await StorageFolder.GetFolderFromPathAsync(directory);
            await Launcher.LaunchFolderAsync(folder);
        }
        catch (Exception ex)
        {
            _logger.LogInformation(ex, "Opening an output folder failed");
        }
    }
}
