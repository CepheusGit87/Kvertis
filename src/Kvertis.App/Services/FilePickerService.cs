using Windows.Storage;
using Windows.Storage.AccessCache;
using Windows.Storage.Pickers;

namespace Kvertis.App.Services;

/// <summary>
/// File access only through system pickers (docs/02, docs/07). The chosen output folder is remembered in the
/// FutureAccessList so the permission survives restarts.
/// </summary>
public interface IFilePickerService
{
    /// <summary>Returns the chosen file paths, or an empty list when the user cancels.</summary>
    Task<IReadOnlyList<string>> PickFilesAsync();

    /// <summary>Returns the chosen folder, or null when the user cancels.</summary>
    Task<StorageFolder?> PickFolderAsync();

    /// <summary>Remembers <paramref name="folder"/> as the output folder and returns its FutureAccessList token.</summary>
    string RememberOutputFolder(StorageFolder folder);

    /// <summary>Returns the remembered output folder path, or null when the permission is gone.</summary>
    Task<string?> GetRememberedOutputFolderAsync(string? token);
}

public sealed class FilePickerService : IFilePickerService
{
    public const string OutputFolderToken = "OutputFolder";

    private readonly IWindowContext _window;

    public FilePickerService(IWindowContext window)
    {
        _window = window;
    }

    public async Task<IReadOnlyList<string>> PickFilesAsync()
    {
        var picker = new FileOpenPicker
        {
            ViewMode = PickerViewMode.List,
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
        };
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, _window.Handle);

        var files = await picker.PickMultipleFilesAsync();
        if (files is null)
        {
            return [];
        }
        return files.Select(f => f.Path).Where(p => !string.IsNullOrEmpty(p)).ToList();
    }

    public async Task<StorageFolder?> PickFolderAsync()
    {
        var picker = new FolderPicker
        {
            ViewMode = PickerViewMode.List,
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
        };
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, _window.Handle);
        return await picker.PickSingleFolderAsync();
    }

    public string RememberOutputFolder(StorageFolder folder)
    {
        ArgumentNullException.ThrowIfNull(folder);
        StorageApplicationPermissions.FutureAccessList.AddOrReplace(OutputFolderToken, folder);
        return OutputFolderToken;
    }

    public async Task<string?> GetRememberedOutputFolderAsync(string? token)
    {
        if (string.IsNullOrEmpty(token) || !StorageApplicationPermissions.FutureAccessList.ContainsItem(token))
        {
            return null;
        }
        try
        {
            var folder = await StorageApplicationPermissions.FutureAccessList.GetFolderAsync(token);
            return folder?.Path;
        }
        catch (Exception ex) when (ex is FileNotFoundException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }
}
