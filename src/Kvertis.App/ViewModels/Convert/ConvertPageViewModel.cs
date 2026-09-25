using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kvertis.App.Helpers;
using Kvertis.App.Services;
using Kvertis.Engine.Abstractions;
using Kvertis.Engine.Formats;
using Kvertis.Engine.IO;
using Kvertis.Queue;
using Microsoft.Extensions.Logging;
using Windows.Storage;

namespace Kvertis.App.ViewModels.Convert;

/// <summary>
/// Step 3 "Umwandeln" (ADR-021). It reads the plan of step 2, computes the target path of every file,
/// starts the round through <see cref="IConversionCoordinator"/> and shows progress, errors and the closing
/// report. It is the only place that decides about the output location; the queue is never touched here.
/// </summary>
public sealed partial class ConvertPageViewModel : ObservableObject, IConvertRowHost, IDisposable
{
    /// <summary>How many running files the middle shows; the rest is summed up in one line.</summary>
    public const int SwirlSlots = 2;

    /// <summary>The narrator hears the overall line again at most this often (unless the counter moved).</summary>
    private static readonly TimeSpan AnnounceInterval = TimeSpan.FromSeconds(5);

    private readonly IWorkflowSession _session;
    private readonly IConversionCoordinator _coordinator;
    private readonly FormatRegistry _registry;
    private readonly IEstimator _estimator;
    private readonly ErrorMessageMapper _errors;
    private readonly ILocalizer _loc;
    private readonly ISettingsService _settings;
    private readonly IFilePickerService _pickers;
    private readonly IDialogService _dialogs;
    private readonly IShellLauncher _shell;
    private readonly IStepNavigationService _steps;
    private readonly INavigationService _navigation;
    private readonly ILicenseService _license;
    private readonly IUiDispatcher _ui;
    private readonly TimeProvider _time;
    private readonly ILogger<ConvertPageViewModel> _logger;

    private readonly Dictionary<string, JobState?> _announced = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<TargetPathPreview> _previews = [];
    private TargetPlan? _builtFrom;
    private string? _customFolderPath;
    private long _lastAnnounceTicks;
    private int _lastAnnouncedDone = -1;
    private bool _reportAnnounced;

    public ConvertPageViewModel(
        IWorkflowSession session,
        IConversionCoordinator coordinator,
        FormatRegistry registry,
        IEstimator estimator,
        ErrorMessageMapper errors,
        ILocalizer loc,
        ISettingsService settings,
        IFilePickerService pickers,
        IDialogService dialogs,
        IShellLauncher shell,
        IStepNavigationService steps,
        INavigationService navigation,
        ILicenseService license,
        IUiDispatcher ui,
        ILogger<ConvertPageViewModel> logger)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _estimator = estimator ?? throw new ArgumentNullException(nameof(estimator));
        _errors = errors ?? throw new ArgumentNullException(nameof(errors));
        _loc = loc ?? throw new ArgumentNullException(nameof(loc));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _pickers = pickers ?? throw new ArgumentNullException(nameof(pickers));
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        _shell = shell ?? throw new ArgumentNullException(nameof(shell));
        _steps = steps ?? throw new ArgumentNullException(nameof(steps));
        _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
        _license = license ?? throw new ArgumentNullException(nameof(license));
        _ui = ui ?? throw new ArgumentNullException(nameof(ui));
        _time = TimeProvider.System;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        Location = new LocationCardViewModel(_loc, ApplySharedLocationAsync);
        PauseLabel = _loc.Get("Convert_Pause_Label");
        _coordinator.Changed += OnRoundChanged;
        _license.StatusChanged += OnLicenseChanged;
    }

    public ObservableCollection<ConvertRowViewModel> Rows { get; } = [];

    /// <summary>Left column: what is still waiting, and after the run what failed.</summary>
    public ObservableCollection<ConvertRowViewModel> Inbox { get; } = [];

    /// <summary>Middle: at most <see cref="SwirlSlots"/> running files.</summary>
    public ObservableCollection<ConvertRowViewModel> Swirl { get; } = [];

    /// <summary>Right: what is finished.</summary>
    public ObservableCollection<ConvertRowViewModel> Done { get; } = [];

    /// <summary>Files step 2 left out, with the reason (free tier).</summary>
    public ObservableCollection<string> SkippedTexts { get; } = [];

    /// <summary>One line per file with its own target: "foto.heic → D:\Ziel".</summary>
    public ObservableCollection<string> BagEntries { get; } = [];

    public LocationCardViewModel Location { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowNoPlan))]
    private bool hasPlan;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsReady), nameof(IsRunningOrPaused), nameof(IsFinished), nameof(ShowSwirlHint))]
    [NotifyCanExecuteChangedFor(nameof(StartCommand), nameof(PauseOrResumeCommand), nameof(CancelAllCommand),
        nameof(BackCommand), nameof(NewRoundCommand))]
    private RoundState state = RoundState.Ready;

    [ObservableProperty]
    private string inboxSummaryText = string.Empty;

    [ObservableProperty]
    private string inboxFailedText = string.Empty;

    [ObservableProperty]
    private string swirlHintText = string.Empty;

    [ObservableProperty]
    private string swirlMoreText = string.Empty;

    [ObservableProperty]
    private string doneText = string.Empty;

    [ObservableProperty]
    private string bagText = string.Empty;

    [ObservableProperty]
    private string listSummaryText = string.Empty;

    [ObservableProperty]
    private double overallProgress;

    [ObservableProperty]
    private string overallText = string.Empty;

    [ObservableProperty]
    private string overallAutomationText = string.Empty;

    /// <summary>Held back so the narrator is not flooded (docs/entwuerfe/schritt-3-umwandeln.md).</summary>
    [ObservableProperty]
    private string overallAnnouncement = string.Empty;

    [ObservableProperty]
    private string startButtonText = string.Empty;

    [ObservableProperty]
    private string pauseLabel = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    private bool canStart;

    [ObservableProperty]
    private bool showProCard;

    [ObservableProperty]
    private string proCardText = string.Empty;

    [ObservableProperty]
    private bool hasSkipped;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasReport))]
    private RoundReportViewModel? report;

    /// <summary>Assertive live region: one finished or failed file, and once the report.</summary>
    [ObservableProperty]
    private string announcement = string.Empty;

    /// <summary>Something went wrong outside a single file (preparing the page, starting the round).</summary>
    [ObservableProperty]
    private string errorText = string.Empty;

    /// <summary>The "Tasche": the files that have a target of their own.</summary>
    [ObservableProperty]
    private bool hasBagEntries;

    public bool ShowNoPlan => !HasPlan;

    public bool IsReady => State == RoundState.Ready;

    public bool IsRunningOrPaused => State is RoundState.Running or RoundState.Paused;

    public bool IsFinished => State == RoundState.Finished;

    public bool ShowSwirlHint => State == RoundState.Ready;

    public bool HasReport => Report is not null;

    // ---- Loading ----------------------------------------------------------------------------------

    /// <summary>
    /// Called every time the page is shown. Never throws: a failure leaves the rows as they are and shows a
    /// short hint instead of tearing down the page.
    /// </summary>
    public async Task LoadAsync()
    {
        try
        {
            await LoadCoreAsync();
            ErrorText = string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "The convert page could not be prepared");
            ErrorText = _loc.Get("Convert_Error_Load_Text");
        }
    }

    private async Task LoadCoreAsync()
    {
        var plan = _session.Plan;
        HasPlan = plan is { Items.Count: > 0 };
        if (!HasPlan || plan is null)
        {
            Rows.Clear();
            UpdateAggregates();
            return;
        }

        if (_session.Location is null)
        {
            _session.Location = await ResolveDefaultLocationAsync();
        }
        UpdateLocationCard();

        if (!ReferenceEquals(plan, _builtFrom))
        {
            // A new plan ends the previous round; its rows and its report go with it (ADR-020). While a round
            // runs the step header blocks the way back, so this cannot hit a running coordinator.
            if (_coordinator.State is RoundState.Ready or RoundState.Finished)
            {
                _coordinator.Reset();
            }
            _builtFrom = plan;
            BuildRows(plan);
        }
        RefreshPreviews();
        RefreshFromCoordinator();
    }

    private async Task<OutputLocation> ResolveDefaultLocationAsync()
    {
        var current = _settings.Current;
        switch (current.OutputLocation)
        {
            case OutputLocationKind.SubFolder:
                return OutputLocation.SubFolder();
            case OutputLocationKind.Custom:
                var remembered = await _pickers.GetRememberedOutputFolderAsync(current.CustomOutputFolderToken);
                _customFolderPath = remembered ?? current.CustomOutputFolderPath;
                return string.IsNullOrEmpty(_customFolderPath)
                    ? OutputLocation.SameFolder
                    : OutputLocation.Custom(_customFolderPath);
            default:
                return OutputLocation.SameFolder;
        }
    }

    private void BuildRows(TargetPlan plan)
    {
        Rows.Clear();
        foreach (var item in plan.Items)
        {
            Rows.Add(new ConvertRowViewModel(item, this, _loc, _registry, _errors, EstimateBytes(item)));
        }
        SkippedTexts.Clear();
        foreach (var skipped in plan.Skipped)
        {
            SkippedTexts.Add(_loc.Format(
                skipped.Reason switch
                {
                    TargetPlanner.ReasonVideo => "Convert_Skipped_Video",
                    TargetPlanner.ReasonBatchSize => "Convert_Skipped_Batch",
                    _ => "Convert_Skipped_Other",
                },
                Path.GetFileName(skipped.Input.Path)));
        }
        HasSkipped = SkippedTexts.Count > 0;
        if (HasSkipped && !_license.IsPro)
        {
            ShowPro(plan.Skipped[0].Reason);
        }
        _announced.Clear();
        _reportAnnounced = false;
    }

    private long EstimateBytes(PlannedConversion item)
    {
        try
        {
            var estimate = _estimator.Estimate(item.Input, item.Settings);
            return estimate.Confidence > 0 && estimate.OutputBytes > 0 ? estimate.OutputBytes : item.Input.SizeBytes;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            _logger.LogDebug(ex, "Estimate for the convert list failed");
            return item.Input.SizeBytes;
        }
    }

    /// <summary>Recomputes every target path and, after the start, moves the still waiting jobs.</summary>
    private void RefreshPreviews()
    {
        if (_session.Plan is not { Items.Count: > 0 } plan || Rows.Count != plan.Items.Count)
        {
            _previews = [];
            UpdateAggregates();
            return;
        }
        _previews = TargetPathPlanner.PreviewAll(
            plan,
            _session.Location ?? OutputLocation.SameFolder,
            _session.OwnLocations,
            _registry,
            DateTimeOffset.Now,
            IsTaken,
            AppPaths.ClipboardFolder);
        for (var i = 0; i < Rows.Count; i++)
        {
            if (Rows[i].CanChangeTarget)
            {
                Rows[i].ApplyPreview(_previews[i]);
            }
        }
        if (_coordinator.State is RoundState.Running or RoundState.Paused)
        {
            _coordinator.RelocateWaiting(_previews);
        }
        UpdateAggregates();
    }

    /// <summary>
    /// The same test the queue's output path reserver makes when a job starts: a directory of that name and a
    /// half-written <c>.kvertis-tmp</c> file block the path too (ADR-007).
    /// </summary>
    private static bool IsTaken(string path) =>
        File.Exists(path) || Directory.Exists(path) || File.Exists(path + ConversionOutput.TempSuffix);

    // ---- Queue events -----------------------------------------------------------------------------

    private void OnRoundChanged(object? sender, RoundChangedEventArgs e) => RefreshFromCoordinator();

    // The licence service raises this on a worker thread; everything below touches bound properties.
    private void OnLicenseChanged(object? sender, EventArgs e) => _ui.Post(() =>
    {
        if (_license.IsPro)
        {
            ShowProCard = false;
        }
    });

    private void RefreshFromCoordinator()
    {
        var jobs = _coordinator.Jobs;
        for (var i = 0; i < Rows.Count && i < jobs.Count; i++)
        {
            Rows[i].ApplyJob(jobs[i]);
            Announce(Rows[i]);
        }
        State = _coordinator.State;
        PauseLabel = _loc.Get(State == RoundState.Paused ? "Convert_Resume_Label" : "Convert_Pause_Label");
        UpdateReport();
        UpdateAggregates();
    }

    private void Announce(ConvertRowViewModel row)
    {
        var previous = _announced.TryGetValue(row.InputPath, out var known) ? known : null;
        if (previous == row.State || !row.IsFinished)
        {
            _announced[row.InputPath] = row.State;
            return;
        }
        _announced[row.InputPath] = row.State;
        Announcement = row.State switch
        {
            JobState.Completed => _loc.Format("Convert_Announce_Completed", row.FileName),
            JobState.Failed => _loc.Format("Convert_Announce_Failed", row.FileName, row.ErrorTitle),
            _ => _loc.Format("Convert_Announce_Cancelled", row.FileName),
        };
    }

    private void UpdateReport()
    {
        if (_coordinator.State != RoundState.Finished || _coordinator.Report is not { } report)
        {
            Report = null;
            _reportAnnounced = false;
            return;
        }
        if (Report is not null)
        {
            return;
        }
        var folder = Rows.FirstOrDefault(r => r.IsDone && !string.IsNullOrEmpty(r.FullPath))?.FullPath;
        Report = new RoundReportViewModel(
            report, _loc, _errors, () => _shell.OpenFolderAsync(Path.GetDirectoryName(folder ?? string.Empty) ?? string.Empty),
            folder is not null);
        if (!_reportAnnounced)
        {
            _reportAnnounced = true;
            Announcement = Report.AnnouncementText;
        }
    }

    // ---- Aggregates -------------------------------------------------------------------------------

    private void UpdateAggregates()
    {
        Replace(Inbox, State == RoundState.Finished
            ? Rows.Where(r => r.IsFailed)
            : Rows.Where(r => !r.IsFinished && !r.IsRunning));
        var running = Rows.Where(r => r.IsRunning).ToList();
        Replace(Swirl, running.Take(SwirlSlots));
        SwirlMoreText = running.Count > SwirlSlots
            ? _loc.Format("Convert_Swirl_More", running.Count - SwirlSlots)
            : string.Empty;
        Replace(Done, Rows.Where(r => r.IsDone));

        var total = Rows.Count;
        var doneCount = Rows.Count(r => r.IsFinished);
        var waiting = Rows.Where(r => !r.IsFinished && !r.IsRunning).ToList();
        InboxSummaryText = _loc.Format(
            "Convert_Inbox_Summary", waiting.Count, Formatting.Bytes(_loc, waiting.Sum(r => r.Item.Input.SizeBytes)));
        var failed = Rows.Count(r => r.IsFailed);
        InboxFailedText = failed > 0 ? _loc.Format("Convert_Inbox_Failed", failed) : string.Empty;
        DoneText = _loc.Format("Convert_Result_Done", Rows.Count(r => r.IsDone), total);
        SwirlHintText = _loc.Format("Convert_Swirl_Ready", total);

        var own = Rows.Where(r => r.IsOwnLocation).ToList();
        BagText = own.Count == 1 ? _loc.Get("Convert_Bag_One") : _loc.Format("Convert_Bag_Some", own.Count);
        HasBagEntries = own.Count > 0;
        var wanted = own.Select(r => _loc.Format("Convert_Bag_Entry", r.FileName, r.TargetDirectoryText)).ToList();
        if (!BagEntries.SequenceEqual(wanted, StringComparer.Ordinal))
        {
            BagEntries.Clear();
            foreach (var entry in wanted)
            {
                BagEntries.Add(entry);
            }
        }

        ListSummaryText = _loc.Format(
            "Convert_List_Summary",
            Formatting.Bytes(_loc, Rows.Sum(r => r.Item.Input.SizeBytes)),
            _loc.Get(State switch
            {
                RoundState.Running => "Convert_List_State_Running",
                RoundState.Paused => "Convert_List_State_Running",
                RoundState.Finished => "Convert_List_State_Done",
                _ => "Convert_List_State_Ready",
            }));

        StartButtonText = _loc.Format("Convert_Start_Button", total);
        CanStart = State == RoundState.Ready && total > 0 && _previews.Count == total && _previews.All(p => !p.NeedsFolder);
        Location.IsEnabled = State != RoundState.Finished;

        UpdateOverall(total, doneCount);
    }

    private void UpdateOverall(int total, int doneCount)
    {
        var overall = _coordinator.Overall;
        if (State == RoundState.Ready || total == 0)
        {
            OverallProgress = 0;
            OverallText = total == 0 ? string.Empty : _loc.Format("Convert_Overall_Ready", total);
        }
        else if (State == RoundState.Finished)
        {
            OverallProgress = 100;
            OverallText = _loc.Format("Convert_Overall_Done", overall.Completed, overall.Total);
        }
        else
        {
            OverallProgress = Math.Clamp(overall.Fraction, 0, 1) * 100;
            OverallText = overall.Remaining is { } remaining
                ? _loc.Format("Convert_Overall_Remaining", overall.Done, overall.Total, Formatting.Duration(_loc, remaining))
                : _loc.Format("Convert_Overall_Progress", overall.Done, overall.Total);
        }
        OverallAutomationText = string.IsNullOrEmpty(OverallText)
            ? string.Empty
            : _loc.Format(
                "Convert_Overall_AutomationName",
                ((int)OverallProgress).ToString(CultureInfo.CurrentCulture),
                OverallText);
        UpdateOverallAnnouncement(doneCount);
    }

    /// <summary>
    /// The narrator gets the polite line when the counter moved, otherwise at most every five seconds
    /// (docs/entwuerfe/schritt-3-umwandeln.md, "Barrierefreiheit").
    /// </summary>
    private void UpdateOverallAnnouncement(int doneCount)
    {
        var now = _time.GetTimestamp();
        var due = doneCount != _lastAnnouncedDone
                  || _lastAnnounceTicks == 0
                  || _time.GetElapsedTime(_lastAnnounceTicks, now) >= AnnounceInterval;
        if (!due)
        {
            return;
        }
        _lastAnnouncedDone = doneCount;
        _lastAnnounceTicks = now;
        OverallAnnouncement = OverallText;
    }

    private static void Replace(ObservableCollection<ConvertRowViewModel> target, IEnumerable<ConvertRowViewModel> source)
    {
        var wanted = source.ToList();
        if (target.Count == wanted.Count && target.SequenceEqual(wanted))
        {
            return;
        }
        target.Clear();
        foreach (var row in wanted)
        {
            target.Add(row);
        }
    }

    // ---- Location ---------------------------------------------------------------------------------

    private async Task ApplySharedLocationAsync(OutputLocationKind kind)
    {
        if (kind == OutputLocationKind.Custom)
        {
            var folder = await _pickers.PickFolderAsync();
            if (folder is null)
            {
                UpdateLocationCard();
                return;
            }
            await RememberAsync(folder);
        }
        _session.Location = kind switch
        {
            OutputLocationKind.SubFolder => OutputLocation.SubFolder(),
            OutputLocationKind.Custom => OutputLocation.Custom(_customFolderPath ?? string.Empty),
            _ => OutputLocation.SameFolder,
        };
        await _settings.UpdateAsync(s => s.OutputLocation = kind);
        UpdateLocationCard();
        RefreshPreviews();
    }

    /// <summary>A folder dropped from the explorer onto the location card.</summary>
    public async Task DropFolderAsync(StorageFolder folder)
    {
        ArgumentNullException.ThrowIfNull(folder);
        await RememberAsync(folder);
        _session.Location = OutputLocation.Custom(folder.Path);
        await _settings.UpdateAsync(s => s.OutputLocation = OutputLocationKind.Custom);
        UpdateLocationCard();
        RefreshPreviews();
    }

    private async Task RememberAsync(StorageFolder folder)
    {
        var token = _pickers.RememberOutputFolder(folder);
        _customFolderPath = folder.Path;
        await _settings.UpdateAsync(s =>
        {
            s.CustomOutputFolderToken = token;
            s.CustomOutputFolderPath = folder.Path;
        });
    }

    private void UpdateLocationCard()
    {
        var kind = _session.Location switch
        {
            OutputLocation.SubFolderLocation => OutputLocationKind.SubFolder,
            OutputLocation.CustomLocation => OutputLocationKind.Custom,
            _ => OutputLocationKind.SameFolder,
        };
        if (_session.Location is OutputLocation.CustomLocation custom && !string.IsNullOrEmpty(custom.Path))
        {
            _customFolderPath = custom.Path;
        }
        Location.Update(kind, _customFolderPath);
    }

    // ---- IConvertRowHost --------------------------------------------------------------------------

    public Task OpenAsync(ConvertRowViewModel row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return _shell.OpenFileAsync(row.FullPath);
    }

    public Task ShowInFolderAsync(ConvertRowViewModel row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return _shell.ShowInFolderAsync(row.FullPath);
    }

    public async Task SetOwnLocationAsync(ConvertRowViewModel row, OutputLocationKind? kind)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (kind is null)
        {
            _session.SetOwnLocation(row.InputPath, null);
            RefreshPreviews();
            return;
        }
        OutputLocation location;
        if (kind == OutputLocationKind.Custom)
        {
            var folder = await _pickers.PickFolderAsync();
            if (folder is null)
            {
                RefreshPreviews();
                return;
            }
            await RememberAsync(folder);
            location = OutputLocation.Custom(folder.Path);
        }
        else
        {
            location = kind == OutputLocationKind.SubFolder ? OutputLocation.SubFolder() : OutputLocation.SameFolder;
        }
        _session.SetOwnLocation(row.InputPath, location);
        RefreshPreviews();
    }

    // ---- Commands ---------------------------------------------------------------------------------

    [RelayCommand(CanExecute = nameof(CanStart))]
    private void Start()
    {
        try
        {
            _coordinator.Start(_previews);
            ShowProCard = false;
            ErrorText = string.Empty;
        }
        catch (JobAdmissionException ex)
        {
            // Second net of the free tier: nothing was enqueued.
            ShowPro(ex.Result.Reason);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            // Not a file error: the round as a whole could not be handed over.
            _logger.LogWarning(ex, "The round could not be started");
            ErrorText = _loc.Get("Convert_Error_StartFailed_Text");
        }
        RefreshFromCoordinator();
    }

    private bool CanPauseOrCancel() => IsRunningOrPaused;

    [RelayCommand(CanExecute = nameof(CanPauseOrCancel))]
    private void PauseOrResume()
    {
        if (State == RoundState.Paused)
        {
            _coordinator.ResumeAll();
        }
        else
        {
            _coordinator.PauseAll();
        }
        RefreshFromCoordinator();
    }

    [RelayCommand(CanExecute = nameof(CanPauseOrCancel))]
    private async Task CancelAllAsync()
    {
        var ok = await _dialogs.ConfirmAsync(
            _loc.Get("Convert_CancelAll_Title"),
            _loc.Get("Convert_CancelAll_Body"),
            _loc.Get("Convert_CancelAll_Confirm"),
            _loc.Get("Dialog_Cancel_Button"));
        if (!ok)
        {
            return;
        }
        _coordinator.CancelAll();
        RefreshFromCoordinator();
    }

    [RelayCommand(CanExecute = nameof(IsReady))]
    private void Back() => _steps.GoTo(WorkflowStep.Target);

    [RelayCommand(CanExecute = nameof(IsFinished))]
    private void NewRound()
    {
        _coordinator.Reset();
        _session.Reset();
        _builtFrom = null;
        Rows.Clear();
        SkippedTexts.Clear();
        HasSkipped = false;
        ShowProCard = false;
        Report = null;
        Announcement = string.Empty;
        ErrorText = string.Empty;
        BagEntries.Clear();
        _previews = [];
        UpdateAggregates();
        _steps.GoTo(WorkflowStep.Drop);
    }

    [RelayCommand]
    private void OpenPro() => _navigation.Navigate(AppPage.Pro);

    private void ShowPro(string? reason)
    {
        ProCardText = reason == FreemiumPolicy.ReasonBatchSize
            ? _loc.Format("Pro_Card_Batch_Text", FreemiumPolicy.FreeBatchLimit)
            : _loc.Get("Pro_Card_Video_Text");
        ShowProCard = true;
    }

    public void Dispose()
    {
        _coordinator.Changed -= OnRoundChanged;
        _license.StatusChanged -= OnLicenseChanged;
    }
}
