using Kvertis.App.ViewModels;
using Kvertis.Queue;

namespace Kvertis.App.Services;

/// <summary>
/// The side effects of the history overlay: pickers, staging, step navigation, the shell and the confirm
/// dialog. Kept out of <see cref="HistoryViewModel"/> so the overlay logic stays testable without WinUI.
/// </summary>
public sealed class HistoryActions : IHistoryActions
{
    private readonly MainViewModel _main;
    private readonly IFilePickerService _pickers;
    private readonly IShellLauncher _shell;
    private readonly IDialogService _dialogs;
    private readonly ILocalizer _loc;
    private readonly IWorkflowSession _session;
    private readonly IStepNavigationService _steps;

    public HistoryActions(
        MainViewModel main,
        IFilePickerService pickers,
        IShellLauncher shell,
        IDialogService dialogs,
        ILocalizer loc,
        IWorkflowSession session,
        IStepNavigationService steps)
    {
        _main = main;
        _pickers = pickers;
        _shell = shell;
        _dialogs = dialogs;
        _loc = loc;
        _session = session;
        _steps = steps;
    }

    public Task<bool> ConfirmClearAsync() =>
        _dialogs.ConfirmAsync(
            _loc.Get("History_Clear_Title"),
            _loc.Get("History_Clear_Body"),
            _loc.Get("History_Clear_Confirm"),
            _loc.Get("Dialog_Cancel_Button"));

    public Task<IReadOnlyList<string>> PickFilesAsync() => _pickers.PickFilesAsync();

    public Task StageAgainAsync(HistoryEntry entry, IReadOnlyList<string> paths)
    {
        ArgumentNullException.ThrowIfNull(entry);
        _session.Previous = entry;
        return _main.AddPathsWithSettingsAsync(paths);
    }

    public void GoToTarget() => _steps.GoTo(WorkflowStep.Target);

    public Task OpenFolderAsync(string directory) => _shell.OpenFolderAsync(directory);

    public Task ShowInFolderAsync(string path) => _shell.ShowInFolderAsync(path);
}
