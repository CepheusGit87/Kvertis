using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kvertis.App.Helpers;
using Kvertis.App.Services;
using Kvertis.Engine.Abstractions;
using Kvertis.Engine.Formats;
using Kvertis.Queue;
using Microsoft.Extensions.Logging;
using Windows.Storage;
using Windows.System;

namespace Kvertis.App.ViewModels;

/// <summary>One line in the history panel.</summary>
public sealed partial class HistoryItemViewModel : ObservableObject
{
    private readonly HistoryViewModel _owner;

    public HistoryItemViewModel(HistoryViewModel owner, HistoryEntry entry, string whenText, string formatText, string settingsText, string resultText)
    {
        _owner = owner;
        Entry = entry;
        WhenText = whenText;
        FormatText = formatText;
        SettingsText = settingsText;
        ResultText = resultText;
        FileName = Path.GetFileName(entry.InputPath);
    }

    public HistoryEntry Entry { get; }

    public string FileName { get; }

    public string WhenText { get; }

    public string FormatText { get; }

    public string SettingsText { get; }

    public string ResultText { get; }

    public bool Success => Entry.Success;

    public bool HasOutput => Entry.Success && !string.IsNullOrEmpty(Entry.OutputPath);

    public string StatusGlyph => Entry.Success ? "\uE73E" : "\uE783";

    public string AutomationName => string.Join(", ", FileName, FormatText, WhenText, ResultText);

    [RelayCommand]
    private Task Again() => _owner.AgainAsync(this);

    [RelayCommand]
    private Task OpenFolder() => _owner.OpenFolderAsync(this);
}

/// <summary>History side panel (docs/06-design.md, "Verlauf"). Local only and can be cleared.</summary>
public sealed partial class HistoryViewModel : ObservableObject, IDisposable
{
    private readonly JobHistory _history;
    private readonly FormatRegistry _registry;
    private readonly MainViewModel _main;
    private readonly IFilePickerService _pickers;
    private readonly IDialogService _dialogs;
    private readonly ILocalizer _loc;
    private readonly IUiDispatcher _ui;
    private readonly IWorkflowSession _session;
    private readonly IStepNavigationService _steps;
    private readonly ILogger<HistoryViewModel> _logger;

    public HistoryViewModel(
        JobHistory history,
        FormatRegistry registry,
        MainViewModel main,
        IFilePickerService pickers,
        IDialogService dialogs,
        ILocalizer loc,
        IUiDispatcher ui,
        IWorkflowSession session,
        IStepNavigationService steps,
        ILogger<HistoryViewModel> logger)
    {
        _history = history;
        _registry = registry;
        _main = main;
        _pickers = pickers;
        _dialogs = dialogs;
        _loc = loc;
        _ui = ui;
        _session = session;
        _steps = steps;
        _logger = logger;
        _history.Changed += OnHistoryChanged;
        Reload();
    }

    public ObservableCollection<HistoryItemViewModel> Items { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ClearCommand))]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private bool hasItems;

    /// <summary>Whether the side panel is open (toggled from the title bar).</summary>
    [ObservableProperty]
    private bool isOpen;

    public bool IsEmpty => !HasItems;

    /// <summary>
    /// Picks files, stages them with the entry's settings and opens step 2, where the entry shows up as the
    /// "damals" card (ADR-020, "Anpassen aus dem Verlauf").
    /// </summary>
    public async Task AgainAsync(HistoryItemViewModel item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var paths = await _pickers.PickFilesAsync();
        if (paths.Count == 0)
        {
            return;
        }
        _session.Previous = item.Entry;
        await _main.AddPathsWithSettingsAsync(paths, item.Entry.Settings);
        IsOpen = false;
        _steps.GoTo(WorkflowStep.Target);
    }

    public async Task OpenFolderAsync(HistoryItemViewModel item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var directory = item.Entry.OutputPath is { } output ? Path.GetDirectoryName(output) : null;
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
            _logger.LogInformation(ex, "Opening a history folder failed");
        }
    }

    [RelayCommand(CanExecute = nameof(HasItems))]
    private async Task ClearAsync()
    {
        var ok = await _dialogs.ConfirmAsync(
            _loc.Get("History_Clear_Title"),
            _loc.Get("History_Clear_Body"),
            _loc.Get("History_Clear_Confirm"),
            _loc.Get("Dialog_Cancel_Button"));
        if (ok)
        {
            await _history.ClearAsync();
        }
    }

    public void Dispose() => _history.Changed -= OnHistoryChanged;

    private void OnHistoryChanged(object? sender, EventArgs e) => _ui.Post(Reload);

    private void Reload()
    {
        Items.Clear();
        foreach (var entry in _history.Entries)
        {
            Items.Add(Create(entry));
        }
        HasItems = Items.Count > 0;
    }

    private HistoryItemViewModel Create(HistoryEntry entry)
    {
        var when = entry.When.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
        var input = _registry.Get(entry.InputFormat)?.DisplayName ?? entry.InputFormat.Id.ToUpperInvariant();
        var output = _registry.Get(entry.Settings.Output)?.DisplayName ?? entry.Settings.Output.Id.ToUpperInvariant();
        var format = _loc.Format("History_Format_Text", input, output);
        var settings = entry.Settings.Preset != ConversionPreset.None
            ? _loc.Get("Preset_" + entry.Settings.Preset.ToString())
            : _loc.Format("History_Quality_Text", entry.Settings.QualityClamped);
        var result = entry.Success
            ? _loc.Format("History_Result_Text", Formatting.Bytes(_loc, entry.BytesIn), Formatting.Bytes(_loc, entry.BytesOut))
            : _loc.Get(ErrorMessageMapper.TitleKey(entry.Error));
        return new HistoryItemViewModel(this, entry, when, format, settings, result);
    }
}
