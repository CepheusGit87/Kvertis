using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kvertis.App.Helpers;
using Kvertis.App.Services;
using Kvertis.Engine.Abstractions;
using Kvertis.Engine.Conversion.Images;
using Kvertis.Engine.Formats;
using Kvertis.Queue;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage;
using Windows.Storage.FileProperties;
using Windows.System;

namespace Kvertis.App.ViewModels;

/// <summary>
/// The main screen (docs/06-design.md, "Hauptansicht"): staging files, the queue view and the action bar.
/// Talks to the engine only through <see cref="IFormatDetector"/>, <see cref="IEstimator"/>,
/// <see cref="IConverterResolver"/> (preview) and the <see cref="IJobQueue"/>.
/// </summary>
public sealed partial class MainViewModel : ObservableObject, IJobItemHost, IDisposable
{
    /// <summary>Folders with more files than this ask before adding (docs/06).</summary>
    public const int FolderConfirmThreshold = 500;

    public const int OutputSameFolderIndex = 0;
    public const int OutputSubFolderIndex = 1;
    public const int OutputCustomIndex = 2;

    private const int MaxParallelDetections = 4;
    private const uint ThumbnailSize = 96;

    private readonly IFormatDetector _detector;
    private readonly FormatRegistry _registry;
    private readonly ISystemCodecCapabilities _codecs;
    private readonly IEstimator _estimator;
    private readonly IJobQueue _queue;
    private readonly IConverterResolver _resolver;
    private readonly ILicenseService _license;
    private readonly ISettingsService _settings;
    private readonly IFilePickerService _pickers;
    private readonly IDialogService _dialogs;
    private readonly ClipboardImageService _clipboard;
    private readonly ErrorMessageMapper _errors;
    private readonly ILocalizer _loc;
    private readonly IUiDispatcher _ui;
    private readonly INavigationService _navigation;
    private readonly ILogger<MainViewModel> _logger;
    private readonly Dictionary<Guid, JobItemViewModel> _byJobId = [];
    private readonly SemaphoreSlim _detectGate = new(MaxParallelDetections, MaxParallelDetections);
    private int _lastOutputIndex;
    private bool _changingOutputIndex;

    public MainViewModel(
        IFormatDetector detector,
        FormatRegistry registry,
        ISystemCodecCapabilities codecs,
        IEstimator estimator,
        IJobQueue queue,
        IConverterResolver resolver,
        ILicenseService license,
        ISettingsService settings,
        IFilePickerService pickers,
        IDialogService dialogs,
        ClipboardImageService clipboard,
        ErrorMessageMapper errors,
        ILocalizer loc,
        IUiDispatcher ui,
        INavigationService navigation,
        ILogger<MainViewModel> logger)
    {
        _detector = detector;
        _registry = registry;
        _codecs = codecs;
        _estimator = estimator;
        _queue = queue;
        _resolver = resolver;
        _license = license;
        _settings = settings;
        _pickers = pickers;
        _dialogs = dialogs;
        _clipboard = clipboard;
        _errors = errors;
        _loc = loc;
        _ui = ui;
        _navigation = navigation;
        _logger = logger;

        PresetOptions =
        [
            new PresetOption(ConversionPreset.None, loc.Get("Preset_None")),
            new PresetOption(ConversionPreset.Messenger, loc.Get("Preset_Messenger")),
            new PresetOption(ConversionPreset.Email, loc.Get("Preset_Email")),
            new PresetOption(ConversionPreset.SocialMedia, loc.Get("Preset_SocialMedia")),
            new PresetOption(ConversionPreset.Website, loc.Get("Preset_Website")),
            new PresetOption(ConversionPreset.Archive, loc.Get("Preset_Archive")),
        ];

        var current = settings.Current;
        _lastOutputIndex = current.OutputLocation switch
        {
            OutputLocationKind.SubFolder => OutputSubFolderIndex,
            OutputLocationKind.Custom when !string.IsNullOrEmpty(current.CustomOutputFolderPath) => OutputCustomIndex,
            _ => OutputSameFolderIndex,
        };
        _changingOutputIndex = true;
        OutputLocationIndex = _lastOutputIndex;
        _changingOutputIndex = false;
        CustomFolderPath = current.CustomOutputFolderPath ?? string.Empty;
        PauseAllLabel = loc.Get("Main_PauseAll_Label");

        Jobs.CollectionChanged += (_, _) => UpdateOverall();
        _queue.JobChanged += OnJobChanged;
        _license.StatusChanged += OnLicenseChanged;
        UpdateOverall();
    }

    public ILocalizer Localizer => _loc;

    public ObservableCollection<JobItemViewModel> Jobs { get; } = [];

    public IReadOnlyList<PresetOption> PresetOptions { get; }

    /// <summary>Raised when a card asks for the preview dialog; the page shows it.</summary>
    public event EventHandler<PreviewViewModel>? PreviewRequested;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState))]
    private bool hasJobs;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    private bool hasStagedJobs;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PauseAllCommand), nameof(CancelAllCommand))]
    private bool isQueueActive;

    [ObservableProperty]
    private bool isPausedAll;

    [ObservableProperty]
    private string pauseAllLabel = string.Empty;

    [ObservableProperty]
    private double overallProgress;

    [ObservableProperty]
    private string overallText = string.Empty;

    [ObservableProperty]
    private string overallAutomationText = string.Empty;

    [ObservableProperty]
    private bool isAdding;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowCustomFolderPath))]
    private int outputLocationIndex;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCustomFolder), nameof(ShowCustomFolderPath))]
    private string customFolderPath = string.Empty;

    [ObservableProperty]
    private bool showProCard;

    [ObservableProperty]
    private string proCardText = string.Empty;

    [ObservableProperty]
    private JobItemViewModel? selectedJob;

    [ObservableProperty]
    private bool hasFinishedJobs;

    /// <summary>Short text for the screen reader live region (job finished or failed).</summary>
    [ObservableProperty]
    private string announcement = string.Empty;

    public bool ShowEmptyState => !HasJobs;

    public bool HasCustomFolder => !string.IsNullOrEmpty(CustomFolderPath);

    public bool ShowCustomFolderPath => OutputLocationIndex == OutputCustomIndex && HasCustomFolder;

    public bool IsPro => _license.IsPro;

    // ---- Adding files ----------------------------------------------------------------------------

    [RelayCommand]
    private async Task AddFilesAsync()
    {
        var paths = await _pickers.PickFilesAsync();
        await AddPathsAsync(paths, isFromClipboard: false);
    }

    [RelayCommand]
    private async Task AddFolderAsync()
    {
        var folder = await _pickers.PickFolderAsync();
        if (folder is not null)
        {
            await AddFoldersAsync([folder.Path], []);
        }
    }

    [RelayCommand]
    private async Task PasteAsync()
    {
        try
        {
            var paste = await _clipboard.GetPasteAsync();
            await AddPathsAsync(paste.Paths, paste.IsTemporary);
        }
        catch (Exception ex)
        {
            _logger.LogInformation(ex, "Reading the clipboard failed");
        }
    }

    /// <summary>Drag-and-drop entry point: files are added directly, folders are expanded recursively.</summary>
    public async Task AddStorageItemsAsync(IReadOnlyList<IStorageItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var files = items.OfType<IStorageFile>().Select(f => f.Path).Where(p => !string.IsNullOrEmpty(p)).ToList();
        var folders = items.OfType<IStorageFolder>().Select(f => f.Path).Where(p => !string.IsNullOrEmpty(p)).ToList();
        await AddFoldersAsync(folders, files);
    }

    /// <summary>Stages files with the settings of an earlier conversion (history "again").</summary>
    public Task AddPathsWithSettingsAsync(IReadOnlyList<string> paths, ConversionSettings settings) =>
        AddPathsAsync(paths, isFromClipboard: false, settings);

    private async Task AddFoldersAsync(IReadOnlyList<string> folders, IReadOnlyList<string> files)
    {
        var all = new List<string>(files);
        if (folders.Count > 0)
        {
            IsAdding = true;
            try
            {
                var found = await Task.Run(() => folders.SelectMany(EnumerateFolder).ToList());
                if (found.Count > FolderConfirmThreshold)
                {
                    var ok = await _dialogs.ConfirmAsync(
                        _loc.Get("Main_ManyFiles_Title"),
                        _loc.Format("Main_ManyFiles_Body", found.Count),
                        _loc.Get("Main_ManyFiles_Confirm"),
                        _loc.Get("Dialog_Cancel_Button"));
                    if (!ok)
                    {
                        return;
                    }
                }
                all.AddRange(found);
            }
            finally
            {
                IsAdding = false;
            }
        }
        await AddPathsAsync(all, isFromClipboard: false);
    }

    private static IEnumerable<string> EnumerateFolder(string folder)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = System.IO.FileAttributes.Hidden | System.IO.FileAttributes.System,
        };
        try
        {
            return Directory.EnumerateFiles(folder, "*", options)
                .Where(p => !p.EndsWith(Kvertis.Engine.IO.ConversionOutput.TempSuffix, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private async Task AddPathsAsync(IReadOnlyList<string> paths, bool isFromClipboard, ConversionSettings? template = null)
    {
        if (paths.Count == 0)
        {
            return;
        }
        var known = new HashSet<string>(Jobs.Where(j => j.IsReady || j.IsDetecting).Select(j => j.FilePath), StringComparer.OrdinalIgnoreCase);
        var added = new List<JobItemViewModel>();
        foreach (var path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (known.Contains(path))
            {
                continue;
            }
            var item = new JobItemViewModel(this, path, isFromClipboard, PresetOptions, _settings.Current.NamePattern, !_settings.Current.KeepMetadata);
            Jobs.Add(item);
            added.Add(item);
        }
        if (added.Count == 0)
        {
            return;
        }

        IsAdding = true;
        try
        {
            await Task.WhenAll(added.Select(item => DetectAsync(item, template)));
        }
        finally
        {
            IsAdding = false;
            UpdateOverall();
        }

        if (!_license.IsPro && added.Any(i => i.IsVideo))
        {
            ShowPro(FreemiumPolicy.ReasonVideo);
        }
    }

    private async Task DetectAsync(JobItemViewModel item, ConversionSettings? template)
    {
        await _detectGate.WaitAsync();
        try
        {
            // Off the UI thread: detection probes files, and the first read of the system codec capabilities
            // (Suggest, CanConvert) may block for up to 3 s while Media Foundation is queried. The await resumes
            // on the UI thread, so the item updates below need no extra marshalling.
            var path = item.FilePath;
            var (input, suggestion, producible) = await Task.Run(async () =>
            {
                var detected = await _detector.DetectAsync(path, CancellationToken.None).ConfigureAwait(false);
                var suggested = _registry.Suggest(detected, _codecs);
                // The static matrix lists what a format family can become; the resolver knows what this file can
                // become (e.g. an MKV with H.264 has no WebM output in Phase 1).
                var targets = detected.Kind == MediaKind.Unknown || suggested is null
                    ? new List<FormatId>()
                    : suggested.Options.Where(id => _resolver.CanConvert(detected, id)).ToList();
                return (detected, suggested, targets);
            });
            if (input.Kind == MediaKind.Unknown || suggestion is null)
            {
                item.SetRejected(_errors.Map(ConversionErrorCode.UnsupportedFormat));
                return;
            }
            if (producible.Count == 0)
            {
                item.SetRejected(_errors.Map(ConversionErrorCode.UnsupportedFormat));
                return;
            }
            var defaultId = producible.Contains(suggestion.Default) ? suggestion.Default : producible[0];
            var options = producible
                .Select(id => new FormatOption(id, _registry.Get(id)?.DisplayName ?? id.Id.ToUpperInvariant(), id == defaultId))
                .ToList();
            var inputLabel = _registry.Get(input.Format)?.DisplayName ?? input.Format.Id.ToUpperInvariant();
            item.Initialize(input, inputLabel, options, Formatting.Bytes(_loc, input.SizeBytes), DescribeInput(input), string.Empty);
            if (template is not null)
            {
                item.ApplySettings(template);
            }
            OnItemSettingsChanged(item);
            _ = LoadThumbnailAsync(item);
        }
        catch (ConversionException ex)
        {
            item.SetRejected(_errors.Map(ex.Code));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            item.SetRejected(_errors.Map(ConversionErrorCode.InputNotReadable));
        }
        finally
        {
            _detectGate.Release();
        }
    }

    private string DescribeInput(InputInfo input)
    {
        var parts = new List<string>();
        if (input.Duration is { } duration && duration > TimeSpan.Zero)
        {
            parts.Add(Formatting.Clock(duration));
        }
        if (input.Width is { } width && input.Height is { } height)
        {
            parts.Add(_loc.Format("Card_Resolution_Text", width, height));
        }
        if (input.PageCount is { } pages)
        {
            parts.Add(_loc.Format("Card_Pages_Text", pages));
        }
        return string.Join(" \u00B7 ", parts);
    }

    private string DescribeWarnings(JobItemViewModel item)
    {
        if (item.Input is not { } input)
        {
            return string.Empty;
        }
        var warnings = input.Warnings.ToList();
        if (item.SelectedFormat is { } output
            && input.Kind == MediaKind.Image
            && ImageConverter.WillLoseTransparency(input, output.Id, _registry)
            && !warnings.Contains(InputWarning.TransparencyLost))
        {
            warnings.Add(InputWarning.TransparencyLost);
        }
        if (!item.StripMetadata)
        {
            // Only relevant when the user asked for metadata removal.
            warnings.Remove(InputWarning.MetadataNotStrippable);
        }
        return string.Join(" ", warnings.Select(w => _loc.Get("Warning_" + w.ToString())));
    }

    private async Task LoadThumbnailAsync(JobItemViewModel item)
    {
        if (item.Kind is not (MediaKind.Image or MediaKind.Video))
        {
            return;
        }
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(item.FilePath);
            using var thumbnail = await file.GetThumbnailAsync(ThumbnailMode.SingleItem, ThumbnailSize);
            if (thumbnail is null || thumbnail.Size == 0)
            {
                return;
            }
            var bitmap = new BitmapImage { DecodePixelWidth = (int)ThumbnailSize };
            await bitmap.SetSourceAsync(thumbnail);
            item.Thumbnail = bitmap;
            item.ThumbnailLoaded = true;
        }
        catch (Exception ex)
        {
            // No thumbnail is fine; the card shows the kind icon.
            _logger.LogDebug(ex, "Thumbnail unavailable");
        }
    }

    // ---- Start, pause, cancel ---------------------------------------------------------------------

    private bool CanStart() => HasStagedJobs;

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartAsync()
    {
        var staged = Jobs.Where(j => j.IsReady && j.Input is not null && j.SelectedFormat is not null).ToList();
        if (staged.Count == 0)
        {
            return;
        }

        var location = await ResolveOutputLocationAsync(staged.Any(j => j.IsFromClipboard));
        if (location is null)
        {
            return;
        }

        var jobs = new List<(JobItemViewModel Item, ConversionJob Job)>();
        try
        {
            for (var i = 0; i < staged.Count; i++)
            {
                var item = staged[i];
                var itemLocation = item.IsFromClipboard && location is not OutputLocation.CustomLocation
                    ? OutputLocation.Custom(CustomFolderPath)
                    : location;
                var directory = OutputDirectoryResolver.Resolve(item.Input!.Path, itemLocation);
                var job = new ConversionJob(item.Input, item.BuildSettings(), directory, item.NamePattern, staged.Count > 1 ? i + 1 : null);
                jobs.Add((item, job));
            }
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "Output location could not be resolved");
            var message = _errors.Map(ConversionErrorCode.OutputNotWritable);
            await _dialogs.ShowMessageAsync(message.Title, message.Body);
            return;
        }

        foreach (var (item, job) in jobs)
        {
            _byJobId[job.Id] = item;
        }
        try
        {
            _queue.EnqueueRange(jobs.Select(j => j.Job));
        }
        catch (JobAdmissionException ex)
        {
            foreach (var (_, job) in jobs)
            {
                _byJobId.Remove(job.Id);
            }
            ShowPro(ex.Result.Reason);
            return;
        }

        foreach (var (item, job) in jobs)
        {
            item.AttachJob(job);
        }
        ShowProCard = false;
        UpdateOverall();
    }

    private async Task<OutputLocation?> ResolveOutputLocationAsync(bool needsExplicitFolder)
    {
        if (OutputLocationIndex == OutputCustomIndex || needsExplicitFolder)
        {
            if (!HasCustomFolder || !Directory.Exists(CustomFolderPath))
            {
                if (!await PickOutputFolderAsync())
                {
                    return null;
                }
            }
        }
        return OutputLocationIndex switch
        {
            OutputSubFolderIndex => OutputLocation.SubFolder(),
            OutputCustomIndex => OutputLocation.Custom(CustomFolderPath),
            _ => OutputLocation.SameFolder,
        };
    }

    [RelayCommand]
    private async Task ChangeOutputFolderAsync()
    {
        if (await PickOutputFolderAsync())
        {
            SetOutputIndexSilently(OutputCustomIndex);
        }
    }

    private async Task<bool> PickOutputFolderAsync()
    {
        var folder = await _pickers.PickFolderAsync();
        if (folder is null)
        {
            return false;
        }
        var token = _pickers.RememberOutputFolder(folder);
        CustomFolderPath = folder.Path;
        await _settings.UpdateAsync(s =>
        {
            s.CustomOutputFolderToken = token;
            s.CustomOutputFolderPath = folder.Path;
        });
        return true;
    }

    partial void OnOutputLocationIndexChanged(int value)
    {
        if (_changingOutputIndex)
        {
            return;
        }
        _ = ApplyOutputLocationAsync(value);
    }

    private async Task ApplyOutputLocationAsync(int value)
    {
        if (value == OutputCustomIndex && !HasCustomFolder && !await PickOutputFolderAsync())
        {
            // Picker cancelled: go back to the previous choice.
            SetOutputIndexSilently(_lastOutputIndex);
            return;
        }
        _lastOutputIndex = value;
        await _settings.UpdateAsync(s => s.OutputLocation = value switch
        {
            OutputSubFolderIndex => OutputLocationKind.SubFolder,
            OutputCustomIndex => OutputLocationKind.Custom,
            _ => OutputLocationKind.SameFolder,
        });
    }

    private void SetOutputIndexSilently(int index)
    {
        _changingOutputIndex = true;
        try
        {
            OutputLocationIndex = index;
            _lastOutputIndex = index;
        }
        finally
        {
            _changingOutputIndex = false;
        }
        _ = _settings.UpdateAsync(s => s.OutputLocation = index switch
        {
            OutputSubFolderIndex => OutputLocationKind.SubFolder,
            OutputCustomIndex => OutputLocationKind.Custom,
            _ => OutputLocationKind.SameFolder,
        });
    }

    /// <summary>Restores the remembered custom folder on start; drops it when the permission is gone.</summary>
    public async Task InitializeAsync()
    {
        var path = await _pickers.GetRememberedOutputFolderAsync(_settings.Current.CustomOutputFolderToken);
        CustomFolderPath = path ?? string.Empty;
        if (path is null && OutputLocationIndex == OutputCustomIndex)
        {
            SetOutputIndexSilently(OutputSameFolderIndex);
        }
    }

    [RelayCommand(CanExecute = nameof(IsQueueActive))]
    private void PauseAll()
    {
        if (IsPausedAll)
        {
            _queue.ResumeAll();
        }
        else
        {
            _queue.PauseAll();
        }
    }

    [RelayCommand(CanExecute = nameof(IsQueueActive))]
    private void CancelAll() => _queue.CancelAll();

    [RelayCommand]
    private void ClearFinished()
    {
        _queue.ClearFinished();
        foreach (var item in Jobs.Where(j => j.IsRejected).ToList())
        {
            Jobs.Remove(item);
        }
    }

    /// <summary>Keyboard: Delete removes the selected card.</summary>
    public void RemoveSelected()
    {
        if (SelectedJob is { CanRemove: true } item)
        {
            Remove(item);
        }
    }

    /// <summary>Keyboard: Space pauses or resumes the selected card.</summary>
    public void ToggleSelected()
    {
        if (SelectedJob is { } item && (item.IsActive))
        {
            PauseOrResume(item);
        }
    }

    // ---- Pro --------------------------------------------------------------------------------------

    private void ShowPro(string? reason)
    {
        ProCardText = reason == FreemiumPolicy.ReasonBatchSize
            ? _loc.Format("Pro_Card_Batch_Text", FreemiumPolicy.FreeBatchLimit)
            : _loc.Get("Pro_Card_Video_Text");
        ShowProCard = true;
    }

    [RelayCommand]
    private void DismissProCard() => ShowProCard = false;

    [RelayCommand]
    private void OpenPro() => _navigation.Navigate(AppPage.Pro);

    private void OnLicenseChanged(object? sender, EventArgs e) => _ui.Post(() =>
    {
        OnPropertyChanged(nameof(IsPro));
        if (_license.IsPro)
        {
            ShowProCard = false;
        }
    });

    // ---- Queue events -----------------------------------------------------------------------------

    private void OnJobChanged(object? sender, JobChangedEventArgs e) => _ui.Post(() => HandleJobChanged(e.Job, e.ChangeKind));

    private void HandleJobChanged(ConversionJob job, JobChangeKind kind)
    {
        _byJobId.TryGetValue(job.Id, out var item);
        switch (kind)
        {
            case JobChangeKind.Removed:
                if (item is not null && ReferenceEquals(item.Job, job))
                {
                    _byJobId.Remove(job.Id);
                    Jobs.Remove(item);
                }
                break;
            default:
                if (item is null)
                {
                    // Added by someone else (history re-run): show it as a card.
                    item = CreateItemForJob(job);
                }
                var wasActive = item.IsActive;
                item.UpdateFromJob(job, _errors);
                if (wasActive && !item.IsActive)
                {
                    Announce(item);
                }
                break;
        }
        UpdateOverall();
    }

    private JobItemViewModel CreateItemForJob(ConversionJob job)
    {
        var item = new JobItemViewModel(this, job.Input.Path, false, PresetOptions, job.NamePattern, job.Settings.Metadata == MetadataPolicy.Strip);
        var label = _registry.Get(job.Input.Format)?.DisplayName ?? job.Input.Format.Id.ToUpperInvariant();
        var output = new FormatOption(job.Settings.Output, _registry.Get(job.Settings.Output)?.DisplayName ?? job.Settings.Output.Id, true);
        item.Initialize(job.Input, label, [output], Formatting.Bytes(_loc, job.Input.SizeBytes), DescribeInput(job.Input), string.Empty);
        item.AttachJob(job);
        _byJobId[job.Id] = item;
        Jobs.Add(item);
        _ = LoadThumbnailAsync(item);
        return item;
    }

    private void Announce(JobItemViewModel item)
    {
        Announcement = item.IsCompleted
            ? _loc.Format("Main_Announce_Completed", item.FileName)
            : item.IsFailed
                ? _loc.Format("Main_Announce_Failed", item.FileName, item.ErrorTitle)
                : _loc.Format("Main_Announce_Cancelled", item.FileName);
    }

    private void UpdateOverall()
    {
        HasJobs = Jobs.Count > 0;
        var staged = Jobs.Where(j => j.IsReady).ToList();
        HasStagedJobs = staged.Count > 0;
        HasFinishedJobs = Jobs.Any(j => j.IsCompleted || j.IsFailed || j.IsCancelled);

        var overall = _queue.Overall;
        IsQueueActive = overall.Running + overall.Pending > 0;
        var activeJobs = _queue.Jobs.Where(j => j.State is JobState.Queued or JobState.Running or JobState.Paused).ToList();
        IsPausedAll = activeJobs.Count > 0 && activeJobs.All(j => j.State == JobState.Paused);
        PauseAllLabel = _loc.Get(IsPausedAll ? "Main_ResumeAll_Label" : "Main_PauseAll_Label");

        if (IsQueueActive)
        {
            OverallProgress = Math.Clamp(overall.Fraction, 0, 1) * 100;
            OverallText = overall.Remaining is { } remaining
                ? _loc.Format("Main_Overall_Remaining", overall.Done, overall.Total, Formatting.Duration(_loc, remaining))
                : _loc.Format("Main_Overall_ProgressText", overall.Done, overall.Total);
        }
        else if (staged.Count > 0)
        {
            OverallProgress = 0;
            var seconds = staged.Sum(j => j.Estimate?.Duration.TotalSeconds ?? 0);
            var parallel = Math.Max(1, Math.Min(_queue.MaxParallel, staged.Count));
            OverallText = seconds > 0
                ? _loc.Format("Main_Overall_Estimate", staged.Count, Formatting.Duration(_loc, TimeSpan.FromSeconds(seconds / parallel)))
                : _loc.Format("Main_Overall_Count", staged.Count);
        }
        else if (overall.Total > 0)
        {
            OverallProgress = 100;
            OverallText = overall.BytesIn > 0
                ? _loc.Format("Main_Overall_Done", overall.Completed, Formatting.Bytes(_loc, overall.BytesIn), Formatting.Bytes(_loc, overall.BytesOut))
                : _loc.Format("Main_Overall_ProgressText", overall.Done, overall.Total);
        }
        else
        {
            OverallProgress = 0;
            OverallText = string.Empty;
        }
        OverallAutomationText = string.IsNullOrEmpty(OverallText)
            ? string.Empty
            : _loc.Format("Main_Overall_AutomationName", ((int)OverallProgress).ToString(CultureInfo.CurrentCulture), OverallText);
    }

    // ---- IJobItemHost -----------------------------------------------------------------------------

    public void OnItemSettingsChanged(JobItemViewModel item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.Input is null || item.SelectedFormat is null)
        {
            return;
        }
        try
        {
            item.ApplyEstimate(_estimator.Estimate(item.Input, item.BuildSettings()));
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Estimate failed");
            item.ApplyEstimate(Estimate.Unknown);
        }
        item.WarningText = DescribeWarnings(item);
        UpdateOverall();
    }

    public void Remove(JobItemViewModel item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.Job is { } job)
        {
            if (!_queue.Remove(job.Id) && _queue.Jobs.Any(j => j.Id == job.Id))
            {
                return; // Running: cancel first.
            }
            _byJobId.Remove(job.Id);
        }
        Jobs.Remove(item);
    }

    public void PauseOrResume(JobItemViewModel item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.Job is not { } job)
        {
            return;
        }
        if (job.State == JobState.Paused)
        {
            _queue.Resume(job.Id);
        }
        else
        {
            _queue.Pause(job.Id);
        }
    }

    public void Cancel(JobItemViewModel item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.Job is { } job)
        {
            _queue.Cancel(job.Id);
        }
    }

    public void Retry(JobItemViewModel item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.Job is not { } old)
        {
            return;
        }
        try
        {
            var fresh = old.State == JobState.Completed ? EnqueueAgain(old) : _queue.Retry(old.Id);
            if (fresh is null)
            {
                return;
            }
            _byJobId.Remove(old.Id);
            _byJobId[fresh.Id] = item;
            item.AttachJob(fresh);
        }
        catch (JobAdmissionException ex)
        {
            ShowPro(ex.Result.Reason);
        }
    }

    private ConversionJob EnqueueAgain(ConversionJob old)
    {
        var fresh = old.CloneAsNew();
        _queue.Enqueue(fresh);
        return fresh;
    }

    public async Task OpenFolderAsync(JobItemViewModel item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var output = item.Job?.Result?.OutputPath ?? item.Job?.OutputPath;
        var directory = output is null ? item.Job?.OutputDirectory : Path.GetDirectoryName(output);
        if (string.IsNullOrEmpty(directory))
        {
            return;
        }
        try
        {
            var folder = await StorageFolder.GetFolderFromPathAsync(directory);
            var options = new FolderLauncherOptions();
            if (output is not null && File.Exists(output))
            {
                options.ItemsToSelect.Add(await StorageFile.GetFileFromPathAsync(output));
            }
            await Launcher.LaunchFolderAsync(folder, options);
        }
        catch (Exception ex)
        {
            _logger.LogInformation(ex, "Opening the output folder failed");
        }
    }

    public Task ShowErrorHelpAsync(JobItemViewModel item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return _dialogs.ShowMessageAsync(item.ErrorTitle, item.ErrorBody);
    }

    public void RequestPreview(JobItemViewModel item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.IsReady)
        {
            PreviewRequested?.Invoke(this, new PreviewViewModel(item, _resolver, _errors, _loc, _logger));
        }
    }

    public void ApplyToAll(JobItemViewModel item)
    {
        ArgumentNullException.ThrowIfNull(item);
        foreach (var other in Jobs.Where(j => j.IsReady && !ReferenceEquals(j, item)).ToList())
        {
            other.CopyOptionsFrom(item);
        }
    }

    public void Dispose()
    {
        _queue.JobChanged -= OnJobChanged;
        _license.StatusChanged -= OnLicenseChanged;
        _detectGate.Dispose();
    }
}
