using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kvertis.App.Helpers;
using Kvertis.App.Services;
using Kvertis.App.ViewModels.Drop;
using Kvertis.Engine.Abstractions;
using Kvertis.Engine.Formats;
using Kvertis.Queue;

namespace Kvertis.App.ViewModels;

/// <summary>One card in the history panel (one conversion, one file).</summary>
public sealed partial class HistoryItemViewModel : ObservableObject
{
    private readonly HistoryViewModel _owner;

    public HistoryItemViewModel(HistoryViewModel owner, HistoryEntry entry, HistoryItemTexts texts)
    {
        ArgumentNullException.ThrowIfNull(texts);
        _owner = owner;
        Entry = entry;
        WhenText = texts.When;
        TimeText = texts.Time;
        FormatText = texts.Format;
        InputFormatText = texts.InputFormat;
        OutputFormatText = texts.OutputFormat;
        SettingsText = texts.Settings;
        TitleText = texts.Title;
        DetailText = texts.Detail;
        ResultText = texts.Result;
        SavingText = texts.Saving;
        KindBrushKey = texts.KindBrushKey;
        FileName = Path.GetFileName(entry.InputPath);
    }

    public HistoryEntry Entry { get; }

    public string FileName { get; }

    /// <summary>Card title like the draft's single-file round: "name → target".</summary>
    public string TitleText { get; }

    /// <summary>Small line under the title: settings and, when it worked, the sizes.</summary>
    public string DetailText { get; }

    public string WhenText { get; }

    /// <summary>Clock time on the timeline (.runde .zeit).</summary>
    public string TimeText { get; }

    /// <summary>Input format for the chip (.chip-k).</summary>
    public string InputFormatText { get; }

    /// <summary>Output format for the paper label (.etikett).</summary>
    public string OutputFormatText { get; }

    /// <summary>"-87 %" for the savings pill; empty when nothing was saved.</summary>
    public string SavingText { get; }

    public bool HasSaving => SavingText.Length > 0;

    /// <summary>Colour token of the input kind (ADR-017), e.g. "KvImageBrush".</summary>
    public string KindBrushKey { get; }

    /// <summary>The status symbol is drawn in the kind colour, or in the error colour when the job failed.</summary>
    public string StatusBrushKey => Entry.Success ? KindBrushKey : "KvErrorBrush";

    public string FormatText { get; }

    public string SettingsText { get; }

    public string ResultText { get; }

    public bool Success => Entry.Success;

    public bool HasOutput => Entry.Success && !string.IsNullOrEmpty(Entry.OutputPath);

    public string StatusGlyph => Entry.Success ? "\uE73E" : "\uE783";

    public string AutomationName =>
        string.Join(", ", new[] { FileName, FormatText, WhenText, ResultText, SavingText }.Where(t => t.Length > 0));

    [RelayCommand]
    private Task Again() => _owner.AgainAsync(this);

    [RelayCommand]
    private Task OpenFolder() => _owner.OpenFolderAsync(this);
}

/// <summary>The formatted texts of one history card, built by <see cref="HistoryViewModel"/>.</summary>
public sealed record HistoryItemTexts(
    string When,
    string Time,
    string Format,
    string InputFormat,
    string OutputFormat,
    string Settings,
    string Result,
    string Title,
    string Detail,
    string Saving,
    string KindBrushKey);

/// <summary>The conversions of one local calendar day (.tag-titel plus .tag-block), newest first.</summary>
public sealed class HistoryDayGroup : List<HistoryItemViewModel>
{
    public HistoryDayGroup(DateOnly day, string title)
    {
        Day = day;
        Title = title;
    }

    public DateOnly Day { get; }

    /// <summary>"Heute", "Gestern" or the date; read as is by screen readers.</summary>
    public string Title { get; }

    /// <summary>The visible label is upper case like the draft's .tag-titel.</summary>
    public string Label => Title.ToUpper(CultureInfo.CurrentCulture);
}

/// <summary>History side panel (docs/06-design.md, "Verlauf"). Local only and can be cleared.</summary>
public sealed partial class HistoryViewModel : ObservableObject, IDisposable
{
    private readonly JobHistory _history;
    private readonly FormatRegistry _registry;
    private readonly MainViewModel _main;
    private readonly IFilePickerService _pickers;
    private readonly IShellLauncher _shell;
    private readonly IDialogService _dialogs;
    private readonly ILocalizer _loc;
    private readonly IUiDispatcher _ui;
    private readonly IWorkflowSession _session;
    private readonly IStepNavigationService _steps;

    public HistoryViewModel(
        JobHistory history,
        FormatRegistry registry,
        MainViewModel main,
        IFilePickerService pickers,
        IShellLauncher shell,
        IDialogService dialogs,
        ILocalizer loc,
        IUiDispatcher ui,
        IWorkflowSession session,
        IStepNavigationService steps)
    {
        _history = history;
        _registry = registry;
        _main = main;
        _pickers = pickers;
        _shell = shell;
        _dialogs = dialogs;
        _loc = loc;
        _ui = ui;
        _session = session;
        _steps = steps;
        _history.Changed += OnHistoryChanged;
        Reload();
    }

    public ObservableCollection<HistoryItemViewModel> Items { get; } = [];

    /// <summary>The same items grouped by local day for the timeline (worksheet 3.6, .tag-titel).</summary>
    public ObservableCollection<HistoryDayGroup> Groups { get; } = [];

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
        await _main.AddPathsWithSettingsAsync(paths);
        IsOpen = false;
        _steps.GoTo(WorkflowStep.Target);
    }

    public Task OpenFolderAsync(HistoryItemViewModel item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return item.Entry.OutputPath is { Length: > 0 } output
            ? _shell.ShowInFolderAsync(output)
            : Task.CompletedTask;
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

    // "Heute" and "Gestern" depend on the clock, so the day titles are rebuilt whenever the panel opens.
    partial void OnIsOpenChanged(bool value)
    {
        if (value)
        {
            Reload();
        }
    }

    private void Reload()
    {
        Items.Clear();
        Groups.Clear();
        var today = DateOnly.FromDateTime(DateTime.Now);
        var groups = new List<HistoryDayGroup>();
        foreach (var entry in _history.Entries)
        {
            var item = Create(entry);
            Items.Add(item);
            // Entries come newest first, so one pass groups them by local day.
            var day = DateOnly.FromDateTime(entry.When.ToLocalTime().DateTime);
            if (groups.Count == 0 || groups[^1].Day != day)
            {
                groups.Add(new HistoryDayGroup(day, DayTitle(day, today)));
            }
            groups[^1].Add(item);
        }
        // A group is a plain list, so it is added only when complete; the grouped view sees every item.
        foreach (var group in groups)
        {
            Groups.Add(group);
        }
        HasItems = Items.Count > 0;
    }

    private string DayTitle(DateOnly day, DateOnly today)
    {
        if (day == today)
        {
            return _loc.Get("History_Day_Today");
        }
        if (day == today.AddDays(-1))
        {
            return _loc.Get("History_Day_Yesterday");
        }
        var culture = CultureInfo.CurrentCulture;
        var pattern = day.Year == today.Year
            ? "dddd, " + culture.DateTimeFormat.MonthDayPattern
            : culture.DateTimeFormat.LongDatePattern;
        return day.ToString(pattern, culture);
    }

    private HistoryItemViewModel Create(HistoryEntry entry)
    {
        var local = entry.When.ToLocalTime();
        var when = local.ToString("g", CultureInfo.CurrentCulture);
        var time = local.ToString("t", CultureInfo.CurrentCulture);
        var input = _registry.Get(entry.InputFormat)?.DisplayName ?? entry.InputFormat.Id.ToUpperInvariant();
        var output = _registry.Get(entry.Settings.Output)?.DisplayName ?? entry.Settings.Output.Id.ToUpperInvariant();
        var format = _loc.Format("History_Format_Text", input, output);
        var settings = entry.Settings.Preset != ConversionPreset.None
            ? _loc.Get("Preset_" + entry.Settings.Preset.ToString())
            : _loc.Format("History_Quality_Text", entry.Settings.QualityClamped);
        var result = entry.Success
            ? _loc.Format("History_Result_Text", Formatting.Bytes(_loc, entry.BytesIn), Formatting.Bytes(_loc, entry.BytesOut))
            : _loc.Get(ErrorMessageMapper.TitleKey(entry.Error));
        var saving = entry.Success && entry.BytesIn > 0 && entry.BytesOut < entry.BytesIn
            ? _loc.Format("History_Saving_Text", (int)Math.Round((1 - ((double)entry.BytesOut / entry.BytesIn)) * 100))
            : string.Empty;
        var title = _loc.Format("History_Format_Text", Path.GetFileName(entry.InputPath), output);
        var detail = entry.Success ? _loc.Format("History_Detail_Text", settings, result) : settings;
        var kind = TrayViewModel.BrushKeyOf(_registry.KindOf(entry.InputFormat));
        return new HistoryItemViewModel(
            this,
            entry,
            new HistoryItemTexts(when, time, format, input, output, settings, result, title, detail, saving, kind));
    }
}
