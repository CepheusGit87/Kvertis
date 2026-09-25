using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kvertis.App.Helpers;
using Kvertis.App.Services;
using Kvertis.Engine.Abstractions;
using Kvertis.Engine.Formats;
using Microsoft.Extensions.Logging;

namespace Kvertis.App.ViewModels.Target;

/// <summary>
/// Step 2 "Ziel" (docs/03-architektur.md, "Datenmodell der Ziel-Seite"). It reads the staged files of the
/// session, lets the user pick output formats and quality per kind and writes the <see cref="TargetPlan"/>.
/// </summary>
public sealed partial class TargetPageViewModel : ObservableObject, IDisposable
{
    private readonly IWorkflowSession _session;
    private readonly FormatRegistry _registry;
    private readonly IConverterResolver _resolver;
    private readonly ISystemCodecCapabilities _codecs;
    private readonly IEstimator _estimator;
    private readonly FreemiumPolicy _freemium;
    private readonly ILicenseService _license;
    private readonly ISettingsService _settings;
    private readonly ILocalizer _loc;
    private readonly IUiDispatcher _ui;
    private readonly IStepNavigationService _steps;
    private readonly ILoggerFactory _loggers;

    public TargetPageViewModel(
        IWorkflowSession session,
        FormatRegistry registry,
        IConverterResolver resolver,
        ISystemCodecCapabilities codecs,
        IEstimator estimator,
        FreemiumPolicy freemium,
        ILicenseService license,
        ISettingsService settings,
        ILocalizer loc,
        IUiDispatcher ui,
        IStepNavigationService steps,
        ILoggerFactory loggers)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _codecs = codecs ?? throw new ArgumentNullException(nameof(codecs));
        _estimator = estimator ?? throw new ArgumentNullException(nameof(estimator));
        _freemium = freemium ?? throw new ArgumentNullException(nameof(freemium));
        _license = license ?? throw new ArgumentNullException(nameof(license));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _loc = loc ?? throw new ArgumentNullException(nameof(loc));
        _ui = ui ?? throw new ArgumentNullException(nameof(ui));
        _steps = steps ?? throw new ArgumentNullException(nameof(steps));
        _loggers = loggers ?? throw new ArgumentNullException(nameof(loggers));
    }

    public ObservableCollection<KindGroupViewModel> Kinds { get; } = [];

    /// <summary>
    /// The drawn middle of the page (ADR-022): orbit, hole and ways. Owned here so it survives the drawing
    /// surface being torn down and built again; the page feeds it kind, planets and anchors.
    /// </summary>
    public Scenes.TargetPathsScene PathsScene { get; } = new(new Scenes.TargetPathsLayout(0f, 0f));

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    private KindGroupViewModel? selectedKind;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPrevious))]
    private PreviousSettingsViewModel? previous;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ContinueCommand))]
    private bool canContinue;

    [ObservableProperty]
    private string summaryText = string.Empty;

    [ObservableProperty]
    private bool isEmpty = true;

    /// <summary>The free tier's batch card: shown when more files are staged than one start may hold.</summary>
    [ObservableProperty]
    private bool showBatchCard;

    [ObservableProperty]
    private string batchCardTitle = string.Empty;

    [ObservableProperty]
    private string batchCardText = string.Empty;

    public bool HasSelection => SelectedKind is not null;

    public bool HasPrevious => Previous is not null;

    /// <summary>Builds the groups from the session. Called every time the page is shown.</summary>
    public void Load()
    {
        Unload();
        var usable = _session.Staged.Where(s => s.IsUsable).ToList();
        IsEmpty = usable.Count == 0;
        var logger = _loggers.CreateLogger<TuningPanelViewModel>();
        var namePattern = _settings.Current.NamePattern ?? string.Empty;
        var stripMetadata = !_settings.Current.KeepMetadata;

        foreach (var kind in TargetPlanner.OrderKinds(usable.Select(s => s.Input.Kind)))
        {
            var files = usable.Where(s => s.Input.Kind == kind).ToList();
            var formats = TargetPlanner.SharedFormats(files.Select(f => f.Input).ToList(), _resolver, _registry, _codecs);
            var locked = _freemium.IsKindLocked(kind);
            var group = new KindGroupViewModel(
                kind,
                files,
                formats,
                _loc,
                _ui,
                _estimator,
                _registry,
                _resolver,
                _codecs,
                logger,
                namePattern,
                stripMetadata,
                locked,
                locked ? _loc.Get("Pro_Card_Video_Title") : string.Empty,
                locked ? _loc.Get("Pro_Card_Video_Text") : string.Empty);
            group.Changed += OnGroupChanged;
            Kinds.Add(group);
        }

        // Step 1 may have been zoomed to one kind (ADR-022): open that one first if it is staged at all.
        SelectedKind = Kinds.FirstOrDefault(k => _session.FocusKind is { } focus && k.Kind == focus)
            ?? Kinds.FirstOrDefault();
        // The zoom may also have named a target; it only counts where the whole group can become it.
        if (_session.PreferredOutput is { } preferred)
        {
            var group = Kinds.FirstOrDefault(k => _session.FocusKind is { } focus && k.Kind == focus);
            group?.Preselect(preferred);
        }
        LoadPrevious();
        Refresh();
        // Buying Pro unlocks the video group and drops the batch limit; the page is rebuilt from scratch.
        _license.StatusChanged += OnLicenseChanged;
    }

    /// <summary>Drops the groups and their timers; the page calls it when it is left.</summary>
    public void Unload()
    {
        _license.StatusChanged -= OnLicenseChanged;
        foreach (var group in Kinds)
        {
            group.Changed -= OnGroupChanged;
            group.Dispose();
        }
        Kinds.Clear();
        Previous = null;
        SelectedKind = null;
    }

    private void OnLicenseChanged(object? sender, EventArgs e) => _ui.Post(Load);

    private void LoadPrevious()
    {
        if (_session.Previous is not { } entry)
        {
            return;
        }
        var label = TargetPlanner.Label(_registry, entry.Settings.Output);
        var kind = _registry.KindOf(entry.Settings.Output);
        var group = Kinds.FirstOrDefault(g => g.SharedFormats.Any(f => f.Id == entry.Settings.Output))
            ?? Kinds.FirstOrDefault(g => g.Kind == kind);
        var possible = group is not null && TargetPlanner.PreviousFits(group.SharedFormats, entry.Settings.Output);
        Previous = new PreviousSettingsViewModel(_loc, entry, label, possible, ApplyPrevious);
        group?.ShowPrevious(entry.Settings);
    }

    private void ApplyPrevious()
    {
        if (_session.Previous is not { } entry)
        {
            return;
        }
        foreach (var group in Kinds.Where(g => g.SharedFormats.Any(f => f.Id == entry.Settings.Output)))
        {
            group.ApplyPrevious(entry.Settings);
        }
        Refresh();
    }

    private void OnGroupChanged(object? sender, EventArgs e) => Refresh();

    private void Refresh()
    {
        var plan = BuildPlan();
        CanContinue = plan.Items.Count > 0;
        ShowBatchCard = plan.Skipped.Any(s => s.Reason == TargetPlanner.ReasonBatchSize);
        if (ShowBatchCard)
        {
            BatchCardTitle = _loc.Get("Pro_Card_Batch_Title");
            BatchCardText = _loc.Format("Pro_Card_Batch_Text", FreemiumPolicy.FreeBatchLimit);
        }
        SummaryText = plan.Items.Count == 0
            ? string.Empty
            : _loc.Format(
                "Target_Summary_Text",
                plan.Items.Count,
                Formatting.Bytes(_loc, Kinds.Sum(k => k.Tuning.TotalBytes)));
    }

    private TargetPlan BuildPlan() =>
        TargetPlanner.BuildPlan(Kinds.SelectMany(k => k.Drafts()).ToList(), _freemium.Limits);

    [RelayCommand(CanExecute = nameof(CanContinue))]
    private void Continue()
    {
        _session.Plan = BuildPlan();
        // The history entry has done its job; it must not come back as a "damals" card on the next round.
        _session.Previous = null;
        _steps.GoTo(WorkflowStep.Convert);
    }

    [RelayCommand]
    private void Back() => _steps.GoTo(WorkflowStep.Drop);

    public void Dispose() => Unload();
}
