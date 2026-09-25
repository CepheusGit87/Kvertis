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

/// <summary>The filter buttons of the history (worksheet 3.6, .segment): one kind, failed jobs or everything.</summary>
public enum HistoryFilter
{
    All = 0,
    Image,
    Audio,
    Video,
    Document,
    Model,
    Failed,
}

/// <summary>
/// What the history hands to the rest of the app. The implementation lives next to the pickers and the shell
/// (<c>Services/HistoryActions.cs</c>), so this view model stays free of WinUI and can be tested on its own.
/// </summary>
public interface IHistoryActions
{
    /// <summary>Asks before the whole history is deleted.</summary>
    Task<bool> ConfirmClearAsync();

    /// <summary>The system file picker; an empty list when the user cancels.</summary>
    Task<IReadOnlyList<string>> PickFilesAsync();

    /// <summary>
    /// Stages <paramref name="paths"/> with the settings of <paramref name="entry"/>; step 2 then shows the entry
    /// as the "damals" card (ADR-020, "Anpassen aus dem Verlauf").
    /// </summary>
    Task StageAgainAsync(HistoryEntry entry, IReadOnlyList<string> paths);

    /// <summary>Opens step 2. Called after the overlay closed, so the step transition may run.</summary>
    void GoToTarget();

    Task OpenFolderAsync(string directory);

    Task ShowInFolderAsync(string path);
}

/// <summary>One entry of the timeline (one conversion, one file) and the source of the detail pane.</summary>
public sealed partial class HistoryItemViewModel : ObservableObject
{
    public HistoryItemViewModel(HistoryEntry entry, MediaKind kind, HistoryItemTexts texts)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(texts);
        Entry = entry;
        Kind = kind;
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
        Pane = texts.Pane;
        FileName = Path.GetFileName(entry.InputPath);
    }

    public HistoryEntry Entry { get; }

    public MediaKind Kind { get; }

    public string FileName { get; }

    /// <summary>Card title like the draft's single-file round: "name → target".</summary>
    public string TitleText { get; }

    /// <summary>Small line under the title: settings and, when it worked, the sizes.</summary>
    public string DetailText { get; }

    public string WhenText { get; }

    /// <summary>Clock time on the timeline (.k1-zeit).</summary>
    public string TimeText { get; }

    /// <summary>Input format for the chip (.chip).</summary>
    public string InputFormatText { get; }

    /// <summary>Output format for the paper label (.eti).</summary>
    public string OutputFormatText { get; }

    /// <summary>"−87 %" for the savings pill; empty when nothing was saved.</summary>
    public string SavingText { get; }

    public bool HasSaving => SavingText.Length > 0;

    /// <summary>Colour token of the input kind (ADR-017), e.g. "KvImageBrush".</summary>
    public string KindBrushKey { get; }

    /// <summary>The timeline node is drawn in the kind colour, or in the error colour when the job failed.</summary>
    public string StatusBrushKey => Entry.Success ? KindBrushKey : "KvErrorBrush";

    public string FormatText { get; }

    public string SettingsText { get; }

    public string ResultText { get; }

    public bool Success => Entry.Success;

    public bool Failed => !Entry.Success;

    public bool HasOutput => Entry.Success && !string.IsNullOrEmpty(Entry.OutputPath);

    public string StatusGlyph => Entry.Success ? "" : "";

    /// <summary>The texts of the detail pane (.k4-d).</summary>
    public HistoryDetailTexts Pane { get; }

    /// <summary>Whether this entry is the one shown in the detail pane (.k1-run.gewaehlt).</summary>
    [ObservableProperty]
    private bool isSelected;

    public string AutomationName =>
        string.Join(", ", new[] { FileName, FormatText, WhenText, ResultText, SavingText }.Where(t => t.Length > 0));
}

/// <summary>The formatted texts of one timeline card, built by <see cref="HistoryViewModel"/>.</summary>
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
    string KindBrushKey,
    HistoryDetailTexts Pane);

/// <summary>
/// The detail pane of one entry (.k4-dk, .k4-zahlen, .k4-einst, .vd): numbers are already formatted, the bar
/// and the quality meter are fractions between 0 and 1.
/// </summary>
public sealed record HistoryDetailTexts(
    string Stamp,
    string BytesIn,
    string BytesOut,
    string Saved,
    double OutRatio,
    string Purpose,
    string Quality,
    double QualityRatio,
    string Duration,
    string Metadata,
    string Location,
    string InputFolder,
    string ErrorTitle,
    string ErrorBody);

/// <summary>The conversions of one local calendar day (.k1-tk plus .k1-runs), newest first.</summary>
public sealed class HistoryDayGroup : List<HistoryItemViewModel>
{
    public HistoryDayGroup(DateOnly day, string title, string subtitle)
    {
        Day = day;
        Title = title;
        Subtitle = subtitle;
    }

    public DateOnly Day { get; }

    /// <summary>"Heute", "Gestern" or the date; read as is by screen readers.</summary>
    public string Title { get; }

    /// <summary>Weekday and date beside "Heute" and "Gestern" (.k1-tk small); empty when the title is the date.</summary>
    public string Subtitle { get; }
}

/// <summary>One button of the filter segment. Checking it sets the filter of the owner.</summary>
public sealed partial class HistoryFilterOption : ObservableObject
{
    private readonly HistoryViewModel _owner;

    public HistoryFilterOption(HistoryViewModel owner, HistoryFilter filter, string label, string dotBrushKey)
    {
        _owner = owner;
        Filter = filter;
        Label = label;
        DotBrushKey = dotBrushKey;
    }

    public HistoryFilter Filter { get; }

    public string Label { get; }

    /// <summary>Colour of the 8 px square in front of the label (.segment button i); empty for "Alle".</summary>
    public string DotBrushKey { get; }

    public bool HasDot => DotBrushKey.Length > 0;

    [ObservableProperty]
    private bool isChecked;

    partial void OnIsCheckedChanged(bool value)
    {
        if (value)
        {
            _owner.Filter = Filter;
        }
    }
}

/// <summary>
/// The history as a full-window overlay (docs/06-design.md, "Verlauf", Teil E Nachtrag): timeline on the
/// left, the chosen entry on the right, search by file name and filter buttons. Local only and can be
/// cleared. Free of WinUI: filter, search and selection are tested in Kvertis.App.Tests.
/// </summary>
public sealed partial class HistoryViewModel : ObservableObject, IDisposable
{
    /// <summary>The search waits this long after the last key before it filters (task: 150 ms).</summary>
    public static readonly TimeSpan SearchDelay = TimeSpan.FromMilliseconds(150);

    private readonly JobHistory _history;
    private readonly FormatRegistry _registry;
    private readonly ILocalizer _loc;
    private readonly IUiDispatcher _ui;
    private readonly IHistoryActions _actions;
    private readonly TimeProvider _time;
    private readonly ITimer _searchTimer;
    private string _appliedSearch = string.Empty;
    private bool _disposed;

    public HistoryViewModel(
        JobHistory history,
        FormatRegistry registry,
        ILocalizer loc,
        IUiDispatcher ui,
        IHistoryActions actions,
        TimeProvider? time = null)
    {
        _history = history ?? throw new ArgumentNullException(nameof(history));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _loc = loc ?? throw new ArgumentNullException(nameof(loc));
        _ui = ui ?? throw new ArgumentNullException(nameof(ui));
        _actions = actions ?? throw new ArgumentNullException(nameof(actions));
        _time = time ?? TimeProvider.System;
        _searchTimer = _time.CreateTimer(_ => _ui.Post(ApplySearchNow), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        Filters =
        [
            new HistoryFilterOption(this, HistoryFilter.All, loc.Get("History_Filter_All"), string.Empty),
            new HistoryFilterOption(this, HistoryFilter.Image, loc.Get("History_Filter_Image"), "KvImageBrush"),
            new HistoryFilterOption(this, HistoryFilter.Audio, loc.Get("History_Filter_Audio"), "KvAudioBrush"),
            new HistoryFilterOption(this, HistoryFilter.Video, loc.Get("History_Filter_Video"), "KvVideoBrush"),
            new HistoryFilterOption(this, HistoryFilter.Document, loc.Get("History_Filter_Document"), "KvDocumentBrush"),
            new HistoryFilterOption(this, HistoryFilter.Model, loc.Get("History_Filter_Model"), "KvModelBrush"),
            new HistoryFilterOption(this, HistoryFilter.Failed, loc.Get("History_Filter_Failed"), "KvErrorBrush"),
        ];
        Filters[0].IsChecked = true;
        _history.Changed += OnHistoryChanged;
        Reload();
    }

    /// <summary>Every entry of the history, newest first, regardless of filter and search.</summary>
    public ObservableCollection<HistoryItemViewModel> Items { get; } = [];

    /// <summary>The entries that pass filter and search, grouped by local day for the timeline.</summary>
    public ObservableCollection<HistoryDayGroup> Groups { get; } = [];

    public IReadOnlyList<HistoryFilterOption> Filters { get; }

    /// <summary>Raised after filter, search or a reload rebuilt <see cref="Groups"/>; the view restores its selection then.</summary>
    public event EventHandler? Applied;

    /// <summary>Whether the overlay is open (title bar button, Esc and "Schließen" close it).</summary>
    [ObservableProperty]
    private bool isOpen;

    [ObservableProperty]
    private HistoryFilter filter;

    /// <summary>The text in the search box. It filters <see cref="SearchDelay"/> after the last change.</summary>
    [ObservableProperty]
    private string searchText = string.Empty;

    /// <summary>The entry in the detail pane; the first visible one unless the user picked another.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyCanExecuteChangedFor(nameof(AgainCommand), nameof(OpenFolderCommand), nameof(ShowInFolderCommand))]
    private HistoryItemViewModel? selected;

    /// <summary>The history holds at least one entry (independent of filter and search).</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ClearCommand))]
    [NotifyPropertyChangedFor(nameof(IsEmpty), nameof(NoMatches))]
    private bool hasItems;

    /// <summary>At least one entry passes filter and search.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NoMatches))]
    private bool hasVisible;

    /// <summary>Number of entries that pass filter and search, for the match line (.treffer b).</summary>
    [ObservableProperty]
    private string matchCountText = "0";

    /// <summary>Rest of the match line after the number, e.g. "von 12 Umwandlungen".</summary>
    [ObservableProperty]
    private string matchSuffixText = string.Empty;

    /// <summary>The whole match line for the screen reader (announced politely when it changes).</summary>
    [ObservableProperty]
    private string matchText = string.Empty;

    public bool HasSelection => Selected is not null;

    /// <summary>Nothing was ever converted (.v-leer).</summary>
    public bool IsEmpty => !HasItems;

    /// <summary>There is history, but filter and search leave nothing.</summary>
    public bool NoMatches => HasItems && !HasVisible;

    /// <summary>The search that is applied right now (the box may be ahead of it during the delay).</summary>
    public string AppliedSearch => _appliedSearch;

    /// <summary>Filters with the current box text at once (Enter, clearing, tests).</summary>
    public void ApplySearchNow()
    {
        if (_disposed)
        {
            return;
        }
        _searchTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        var query = (SearchText ?? string.Empty).Trim();
        if (string.Equals(query, _appliedSearch, StringComparison.Ordinal))
        {
            return;
        }
        _appliedSearch = query;
        OnPropertyChanged(nameof(AppliedSearch));
        Apply();
    }

    /// <summary>Whether <paramref name="item"/> passes the filter <paramref name="filter"/>.</summary>
    public static bool Passes(HistoryItemViewModel item, HistoryFilter filter)
    {
        ArgumentNullException.ThrowIfNull(item);
        return filter switch
        {
            HistoryFilter.All => true,
            HistoryFilter.Failed => !item.Entry.Success,
            HistoryFilter.Image => item.Kind == MediaKind.Image,
            HistoryFilter.Audio => item.Kind == MediaKind.Audio,
            HistoryFilter.Video => item.Kind == MediaKind.Video,
            HistoryFilter.Document => item.Kind == MediaKind.Document,
            HistoryFilter.Model => item.Kind == MediaKind.Model3D,
            _ => true,
        };
    }

    /// <summary>Whether the file name of <paramref name="item"/> contains <paramref name="query"/> (case-insensitive).</summary>
    public static bool MatchesSearch(HistoryItemViewModel item, string? query)
    {
        ArgumentNullException.ThrowIfNull(item);
        return string.IsNullOrWhiteSpace(query)
            || item.FileName.Contains(query.Trim(), StringComparison.CurrentCultureIgnoreCase);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _history.Changed -= OnHistoryChanged;
        _searchTimer.Dispose();
    }

    [RelayCommand]
    private void Close() => IsOpen = false;

    [RelayCommand]
    private void ClearSearch()
    {
        SearchText = string.Empty;
        ApplySearchNow();
    }

    /// <summary>"Filter zurücksetzen" in the no-match state: all kinds, no search.</summary>
    [RelayCommand]
    private void ResetFilters()
    {
        Filter = HistoryFilter.All;
        ClearSearch();
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task AgainAsync()
    {
        if (Selected is not { } item)
        {
            return;
        }
        var paths = await _actions.PickFilesAsync();
        if (paths.Count == 0)
        {
            return;
        }
        await _actions.StageAgainAsync(item.Entry, paths);
        // Close first: a step transition never starts while the overlay covers the window (ADR-023).
        IsOpen = false;
        _actions.GoToTarget();
    }

    private bool CanUseOutput() => Selected is { HasOutput: true };

    [RelayCommand(CanExecute = nameof(CanUseOutput))]
    private Task OpenFolderAsync() =>
        Selected?.Entry.OutputPath is { Length: > 0 } output && Path.GetDirectoryName(output) is { Length: > 0 } folder
            ? _actions.OpenFolderAsync(folder)
            : Task.CompletedTask;

    [RelayCommand(CanExecute = nameof(CanUseOutput))]
    private Task ShowInFolderAsync() =>
        Selected?.Entry.OutputPath is { Length: > 0 } output
            ? _actions.ShowInFolderAsync(output)
            : Task.CompletedTask;

    [RelayCommand(CanExecute = nameof(HasItems))]
    private async Task ClearAsync()
    {
        if (await _actions.ConfirmClearAsync())
        {
            await _history.ClearAsync();
        }
    }

    partial void OnSearchTextChanged(string value)
    {
        if (!_disposed)
        {
            // Every key restarts the wait, so the list only moves once the user pauses.
            _searchTimer.Change(SearchDelay, Timeout.InfiniteTimeSpan);
        }
    }

    partial void OnFilterChanged(HistoryFilter value)
    {
        foreach (var option in Filters)
        {
            option.IsChecked = option.Filter == value;
        }
        Apply();
    }

    partial void OnSelectedChanged(HistoryItemViewModel? oldValue, HistoryItemViewModel? newValue)
    {
        if (oldValue is not null)
        {
            oldValue.IsSelected = false;
        }
        if (newValue is not null)
        {
            newValue.IsSelected = true;
        }
    }

    // "Heute" and "Gestern" depend on the clock, so the day titles are rebuilt whenever the overlay opens.
    partial void OnIsOpenChanged(bool value)
    {
        if (value)
        {
            Reload();
        }
    }

    private void OnHistoryChanged(object? sender, EventArgs e) => _ui.Post(Reload);

    private void Reload()
    {
        var selectedId = Selected?.Entry.Id;
        Items.Clear();
        var today = DateOnly.FromDateTime(_time.GetLocalNow().DateTime);
        foreach (var entry in _history.Entries)
        {
            Items.Add(Create(entry, today));
        }
        HasItems = Items.Count > 0;
        Apply(selectedId);
    }

    /// <summary>Filters, searches and regroups; keeps the chosen entry when it is still visible.</summary>
    private void Apply(Guid? keepId = null)
    {
        keepId ??= Selected?.Entry.Id;
        var visible = Items.Where(i => Passes(i, Filter) && MatchesSearch(i, _appliedSearch)).ToList();

        Groups.Clear();
        var today = DateOnly.FromDateTime(_time.GetLocalNow().DateTime);
        var groups = new List<HistoryDayGroup>();
        foreach (var item in visible)
        {
            // Items come newest first, so one pass groups them by local day.
            var day = DateOnly.FromDateTime(item.Entry.When.ToLocalTime().DateTime);
            if (groups.Count == 0 || groups[^1].Day != day)
            {
                groups.Add(new HistoryDayGroup(day, DayTitle(day, today), DaySubtitle(day, today)));
            }
            groups[^1].Add(item);
        }
        // A group is a plain list, so it is added only when complete; the grouped view sees every item.
        foreach (var group in groups)
        {
            Groups.Add(group);
        }

        HasVisible = visible.Count > 0;
        Selected = visible.FirstOrDefault(i => i.Entry.Id == keepId) ?? visible.FirstOrDefault();
        UpdateMatchText(visible.Count);
        Applied?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateMatchText(int visible)
    {
        var total = Items.Count;
        var filtered = Filter != HistoryFilter.All || _appliedSearch.Length > 0;
        MatchCountText = visible.ToString(CultureInfo.CurrentCulture);
        MatchSuffixText = filtered
            ? _loc.Format("History_Match_Of", total)
            : _loc.Get(total == 1 ? "History_Match_One" : "History_Match_Many");
        MatchText = string.Concat(MatchCountText, " ", MatchSuffixText);
    }

    private static string DaySubtitle(DateOnly day, DateOnly today) =>
        day == today || day == today.AddDays(-1)
            ? day.ToString("dddd, " + CultureInfo.CurrentCulture.DateTimeFormat.MonthDayPattern, CultureInfo.CurrentCulture)
            : string.Empty;

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

    private HistoryItemViewModel Create(HistoryEntry entry, DateOnly today)
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
        var bytesIn = Formatting.Bytes(_loc, entry.BytesIn);
        var bytesOut = entry.Success ? Formatting.Bytes(_loc, entry.BytesOut) : _loc.Get("History_Value_None");
        var result = entry.Success
            ? _loc.Format("History_Result_Text", bytesIn, bytesOut)
            : _loc.Get(ErrorMessageMapper.TitleKey(entry.Error));
        var saved = entry.Success && entry.BytesIn > 0 && entry.BytesOut < entry.BytesIn;
        var saving = saved
            ? _loc.Format("History_Saving_Text", (int)Math.Round((1 - ((double)entry.BytesOut / entry.BytesIn)) * 100))
            : string.Empty;
        var title = _loc.Format("History_Format_Text", Path.GetFileName(entry.InputPath), output);
        var detail = entry.Success ? _loc.Format("History_Detail_Text", settings, result) : settings;
        var kind = _registry.KindOf(entry.InputFormat);

        var day = DateOnly.FromDateTime(local.DateTime);
        var ratio = entry.Success && entry.BytesIn > 0 ? Math.Clamp((double)entry.BytesOut / entry.BytesIn, 0, 1) : 0;
        var details = new HistoryDetailTexts(
            Stamp: _loc.Format("History_Stamp_Text", DayTitle(day, today), time),
            BytesIn: bytesIn,
            BytesOut: bytesOut,
            Saved: saved ? Formatting.Bytes(_loc, entry.BytesIn - entry.BytesOut) : _loc.Get("History_Value_None"),
            OutRatio: ratio,
            Purpose: _loc.Get("Preset_" + entry.Settings.Preset.ToString()),
            Quality: entry.Settings.QualityClamped.ToString(CultureInfo.CurrentCulture),
            QualityRatio: entry.Settings.QualityClamped / 100.0,
            Duration: entry.Elapsed > TimeSpan.Zero ? Formatting.Duration(_loc, entry.Elapsed) : _loc.Get("History_Value_None"),
            Metadata: _loc.Get(entry.Settings.Metadata == MetadataPolicy.Keep ? "History_Metadata_Keep" : "History_Metadata_Strip"),
            Location: entry.OutputPath is { Length: > 0 } path ? Path.GetDirectoryName(path) ?? path : _loc.Get("History_Value_None"),
            InputFolder: Path.GetDirectoryName(entry.InputPath) ?? string.Empty,
            ErrorTitle: entry.Success ? string.Empty : _loc.Get(ErrorMessageMapper.TitleKey(entry.Error)),
            ErrorBody: entry.Success ? string.Empty : _loc.Get(ErrorMessageMapper.BodyKey(entry.Error)));

        return new HistoryItemViewModel(
            entry,
            kind,
            new HistoryItemTexts(when, time, format, input, output, settings, result, title, detail, saving, TrayViewModel.BrushKeyOf(kind), details));
    }
}
