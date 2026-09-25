using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kvertis.App.Helpers;
using Kvertis.App.Scenes;
using Kvertis.App.Services;
using Kvertis.App.ViewModels.Drop;
using Kvertis.Engine.Abstractions;
using Kvertis.Engine.Formats;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage;
using Windows.Storage.FileProperties;

namespace Kvertis.App.ViewModels;

/// <summary>
/// Step 1 "Dateien" (docs/06-design.md, "Hauptansicht"): staging files and handing them to the session.
/// Since step 3 owns the round (ADR-021) this view model no longer knows the queue: no start, no output
/// location, no progress and no history restart.
/// </summary>
public sealed partial class MainViewModel : ObservableObject, IJobItemHost, IDisposable
{
    /// <summary>Folders with more files than this ask before adding (docs/06).</summary>
    public const int FolderConfirmThreshold = 500;

    private const int MaxParallelDetections = 4;
    private const uint ThumbnailSize = 96;

    private readonly IFormatDetector _detector;
    private readonly FormatRegistry _registry;
    private readonly ISystemCodecCapabilities _codecs;
    private readonly IConverterResolver _resolver;
    private readonly ISettingsService _settings;
    private readonly IFilePickerService _pickers;
    private readonly IDialogService _dialogs;
    private readonly ClipboardImageService _clipboard;
    private readonly ErrorMessageMapper _errors;
    private readonly ILocalizer _loc;
    private readonly IWorkflowSession _session;
    private readonly IStepNavigationService _steps;
    private readonly ILogger<MainViewModel> _logger;
    private readonly SemaphoreSlim _detectGate = new(MaxParallelDetections, MaxParallelDetections);

    /// <summary>The paths of the cards last handed to the session; guards against publishing on every tick.</summary>
    private string _stagedKey = string.Empty;

    /// <summary>The files that already have a body in the scene, so nothing is added twice.</summary>
    private readonly HashSet<Guid> _inScene = [];

    public MainViewModel(
        IFormatDetector detector,
        FormatRegistry registry,
        ISystemCodecCapabilities codecs,
        IConverterResolver resolver,
        ISettingsService settings,
        IFilePickerService pickers,
        IDialogService dialogs,
        ClipboardImageService clipboard,
        ErrorMessageMapper errors,
        ILocalizer loc,
        IWorkflowSession session,
        IStepNavigationService steps,
        ILogger<MainViewModel> logger)
    {
        _detector = detector;
        _registry = registry;
        _codecs = codecs;
        _resolver = resolver;
        _settings = settings;
        _pickers = pickers;
        _dialogs = dialogs;
        _clipboard = clipboard;
        _errors = errors;
        _loc = loc;
        _session = session;
        _steps = steps;
        _logger = logger;

        // The galaxy of step 1 (ADR-022). The scene belongs to the view model, not to the drawing surface: it
        // survives navigating away and back, so the planets stand where they stood. It costs nothing while no
        // surface exists, because nobody calls Update then.
        Scene = new GalaxyScene(
            new GalaxyLayout(700f, 280f),
            NeutralPalette,
            new Random(Random.Shared.Next()),
            new GalaxyBudget());
        Trays = GalaxyLayout.OrbitOrder
            .Reverse()
            .Select(kind => new TrayViewModel(kind, registry, codecs, loc))
            .ToList();
        Rejected = new RejectedListViewModel(loc);
        Paths = new PathsViewModel(registry, codecs, loc, TimeProvider.System);

        Jobs.CollectionChanged += (_, _) => UpdateCounts();
        _session.Changed += OnSessionChanged;
        UpdateCounts();
    }

    /// <summary>Grey stand-in until a drawing surface reads the real theme colours (<c>ScenePaletteReader</c>).</summary>
    private static ScenePalette NeutralPalette { get; } = new(
        new SceneColor(128, 128, 128),
        new SceneColor(128, 128, 128),
        new SceneColor(128, 128, 128),
        new SceneColor(128, 128, 128),
        new SceneColor(128, 128, 128),
        new SceneColor(128, 128, 128),
        new SceneColor(128, 128, 128),
        new SceneColor(230, 230, 230),
        new SceneColor(16, 16, 16),
        new SceneColor(140, 140, 140),
        IsDark: true);

    /// <summary>The galaxy of step 1. The UI thread only enqueues commands and reads the snapshot.</summary>
    public GalaxyScene Scene { get; }

    /// <summary>The five trays, left to right: images, audio, video, 3D models, documents.</summary>
    public IReadOnlyList<TrayViewModel> Trays { get; }

    /// <summary>The card "Nicht umwandelbar" above the galaxy.</summary>
    public RejectedListViewModel Rejected { get; }

    /// <summary>The zoom "Wege durchs Loch"; empty while no kind is zoomed.</summary>
    public PathsViewModel Paths { get; }

    /// <summary>Raised after the trays, the rejected card and the scene were brought in line with the files.</summary>
    public event EventHandler? StagedChanged;

    /// <summary>
    /// "Neue Runde" in step 3 resets the session. Step 1 has to follow, otherwise its cards would stay and
    /// <see cref="PublishStaged"/> would see an unchanged key and never hand them over again.
    /// </summary>
    private void OnSessionChanged(object? sender, EventArgs e)
    {
        if (_session.Staged.Count == 0 && _session.Plan is null && Jobs.Count > 0)
        {
            Clear();
        }
    }

    /// <summary>Drops every card and forgets what was handed to the session.</summary>
    public void Clear()
    {
        _stagedKey = string.Empty;
        Jobs.Clear();
        ZoomKind = null;
        _inScene.Clear();
        Scene.Enqueue(new Clear());
        UpdateCounts();
    }

    public ILocalizer Localizer => _loc;

    public ObservableCollection<JobItemViewModel> Jobs { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmptyState))]
    private bool hasJobs;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GoToTargetCommand))]
    private bool hasStagedJobs;

    [ObservableProperty]
    private bool isAdding;

    /// <summary>The kind the galaxy is zoomed to, or null for the overview.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsZoomed))]
    private MediaKind? zoomKind;

    public bool ShowEmptyState => !HasJobs;

    public bool IsZoomed => ZoomKind is not null;

    // ---- Galaxy: zoom and scene ------------------------------------------------------------------

    /// <summary>Clicking the same tray head again (or the hole, or Esc) goes back to the overview.</summary>
    [RelayCommand]
    public void ToggleZoom(MediaKind? kind) => ZoomKind = ZoomKind == kind ? null : kind;

    partial void OnZoomKindChanged(MediaKind? value)
    {
        Scene.Enqueue(new ZoomTo(value));
        foreach (var tray in Trays)
        {
            tray.IsZoomed = value is { } zoomed && tray.Kind == zoomed;
            tray.IsBar = value is not null;
        }

        if (value is { } kind)
        {
            var staged = Jobs
                .Where(j => j.IsReady && j.Kind == kind && j.Input is not null)
                .Select(j => j.Input!.Format)
                .Distinct()
                .ToList();
            Paths.Open(kind, staged);
        }
        else
        {
            Paths.Close();
        }
    }

    /// <summary>Mirrors the detected files into the trays, the rejected card and the scene.</summary>
    private void SyncTrays()
    {
        var all = Jobs.Cast<ITrayFile>().ToList();
        foreach (var tray in Trays)
        {
            tray.Sync(all);
        }

        Rejected.Sync(all);
        SyncScene();
        // The still picture draws its dots from the kinds, which only exist once detection is done; a plain
        // collection change of Jobs comes too early for it.
        StagedChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Adds a planet for every newly detected file and removes the ones that left. Files still being detected
    /// have no kind yet, so they enter the scene the moment detection named one.
    /// </summary>
    private void SyncScene()
    {
        foreach (var job in Jobs)
        {
            if (job.IsDetecting || _inScene.Contains(job.Id))
            {
                continue;
            }

            // Kind Unknown tells the scene "this one flies to the rejected card instead of to an orbit".
            var kind = job.IsRejected ? MediaKind.Unknown : job.Kind;
            var label = string.IsNullOrEmpty(job.InputFormatLabel)
                ? Path.GetExtension(job.FileName).TrimStart('.').ToUpperInvariant()
                : job.InputFormatLabel;
            Scene.Enqueue(new AddBody(job.Id, kind, label, job.FileName, job.Input?.SizeBytes ?? 0L));
            _inScene.Add(job.Id);
        }

        var live = Jobs.Select(j => j.Id).ToHashSet();
        foreach (var id in _inScene.Where(id => !live.Contains(id)).ToList())
        {
            Scene.Enqueue(new RemoveBody(id));
            _inScene.Remove(id);
        }
    }

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

    /// <summary>
    /// Stages files for "Anpassen aus dem Verlauf". The old settings travel through
    /// <see cref="IWorkflowSession.Previous"/> into step 2, not into the card any more (ADR-020).
    /// </summary>
    public Task AddPathsWithSettingsAsync(IReadOnlyList<string> paths) =>
        AddPathsAsync(paths, isFromClipboard: false);

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

    private async Task AddPathsAsync(IReadOnlyList<string> paths, bool isFromClipboard)
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
            var item = new JobItemViewModel(this, path, isFromClipboard, _settings.Current.NamePattern, !_settings.Current.KeepMetadata);
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
            await Task.WhenAll(added.Select(DetectAsync));
        }
        finally
        {
            IsAdding = false;
            UpdateCounts();
        }
    }

    private async Task DetectAsync(JobItemViewModel item)
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
            if (input.Kind == MediaKind.Unknown || suggestion is null || producible.Count == 0)
            {
                item.SetRejected(_errors.Map(ConversionErrorCode.UnsupportedFormat));
                return;
            }
            var defaultId = producible.Contains(suggestion.Default) ? suggestion.Default : producible[0];
            var options = producible
                .Select(id => new FormatOption(id, _registry.Get(id)?.DisplayName ?? id.Id.ToUpperInvariant(), id == defaultId))
                .ToList();
            var inputLabel = _registry.Get(input.Format)?.DisplayName ?? input.Format.Id.ToUpperInvariant();
            item.Initialize(
                input, inputLabel, options, Formatting.Bytes(_loc, input.SizeBytes), DescribeInput(input), DescribeWarnings(input));
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
            // Every file lands in its tray and on its orbit the moment its own detection is done, not only
            // once the whole batch finished.
            UpdateCounts();
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
        return string.Join(" · ", parts);
    }

    /// <summary>Warnings that only depend on the file itself; the ones that depend on the target are step 2's.</summary>
    private string DescribeWarnings(InputInfo input) =>
        string.Join(" ", input.Warnings
            // The extension mismatch has its own line in the tray row (Tray_ExtensionMismatch_Text).
            .Where(w => w is not (InputWarning.MetadataNotStrippable or InputWarning.ExtensionMismatch))
            .Select(w => _loc.Get("Warning_" + w.ToString())));

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

    // ---- On to step 2 ----------------------------------------------------------------------------

    private bool CanGoToTarget() => HasStagedJobs;

    /// <summary>Step 1 hands the detected files to the session and moves on to step 2 (ADR-020).</summary>
    [RelayCommand(CanExecute = nameof(CanGoToTarget))]
    private void GoToTarget()
    {
        PublishStaged();
        // The zoom is a view, not a decision: it only tells step 2 which kind to open first (ADR-022).
        _session.FocusKind = ZoomKind;
        _session.PreferredOutput = ZoomKind is null ? null : Paths.PreferredOutput;
        _steps.GoTo(WorkflowStep.Target);
    }

    /// <summary>Mirrors the ready cards into <see cref="IWorkflowSession.Staged"/>.</summary>
    private void PublishStaged()
    {
        if (Jobs.Count == 0)
        {
            // The list was emptied ("Neue Runde"): the plan and the history entry go with it.
            if (_stagedKey.Length > 0)
            {
                _stagedKey = string.Empty;
                _session.Reset();
            }
            return;
        }
        var ready = Jobs.Where(j => j.IsReady && j.Input is not null).ToList();
        var key = string.Join("|", ready.Select(j => j.FilePath));
        if (key == _stagedKey)
        {
            return;
        }
        _stagedKey = key;
        _session.SetStaged(ready
            .Select(j => new StagedFile(
                j.Input!,
                j.Kind is MediaKind.Image or MediaKind.Video ? j.FilePath : null))
            .ToList());
    }

    private void UpdateCounts()
    {
        HasJobs = Jobs.Count > 0;
        HasStagedJobs = Jobs.Any(j => j.IsReady);
        SyncTrays();
        PublishStaged();
    }

    // ---- IJobItemHost ----------------------------------------------------------------------------

    public void Remove(JobItemViewModel item)
    {
        ArgumentNullException.ThrowIfNull(item);
        Jobs.Remove(item);
        UpdateCounts();
    }

    public void Dispose()
    {
        _session.Changed -= OnSessionChanged;
        _detectGate.Dispose();
    }
}
