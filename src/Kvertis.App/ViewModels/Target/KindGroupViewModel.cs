using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Kvertis.App.Services;
using Kvertis.Engine.Abstractions;
using Kvertis.Engine.Formats;
using Microsoft.Extensions.Logging;

namespace Kvertis.App.ViewModels.Target;

/// <summary>Whether one output format applies to the whole kind or every file picks its own.</summary>
public enum TargetMode
{
    AllSame = 0,
    Individual,
}

/// <summary>
/// One media kind of step 2: its files, the shared format list and the tuning panel. Grade, zones, exact
/// size and "Weiteres" belong to the kind, never to a single file.
/// </summary>
public sealed partial class KindGroupViewModel : ObservableObject, IDisposable
{
    private readonly ILocalizer _loc;
    private bool _silent;

    public KindGroupViewModel(
        MediaKind kind,
        IReadOnlyList<StagedFile> staged,
        IReadOnlyList<FormatOption> sharedFormats,
        ILocalizer loc,
        IUiDispatcher ui,
        IEstimator estimator,
        FormatRegistry registry,
        IConverterResolver resolver,
        ISystemCodecCapabilities? codecs,
        ILogger logger,
        string namePattern,
        bool stripMetadata,
        bool isLocked,
        string lockTitle,
        string lockText)
    {
        ArgumentNullException.ThrowIfNull(staged);
        ArgumentNullException.ThrowIfNull(sharedFormats);
        _loc = loc ?? throw new ArgumentNullException(nameof(loc));
        ArgumentNullException.ThrowIfNull(registry);

        Kind = kind;
        ColorKey = TargetPlanner.ColorKey(kind);
        KindName = loc.Get("Target_Kind_" + kind.ToString());
        SharedFormats = sharedFormats;
        IsLocked = isLocked;
        LockTitle = lockTitle;
        LockText = lockText;
        Tuning = new TuningPanelViewModel(kind, loc, ui, estimator, registry, logger, namePattern, stripMetadata);

        foreach (var file in staged)
        {
            var own = TargetPlanner.SharedFormats([file.Input], resolver, registry, codecs);
            var vm = new TargetFileViewModel(loc, file, registry, own, OnFileChanged);
            Files.Add(vm);
            Paths.Add(new PathRowViewModel(vm));
        }

        sharedFormat = sharedFormats.FirstOrDefault(f => f.IsSuggested)
            ?? (sharedFormats.Count > 0 ? sharedFormats[0] : null);
        PushFormat();
        Tuning.SettingsChanged += OnTuningChanged;
        Tuning.SetFiles(Files);
    }

    public MediaKind Kind { get; }

    /// <summary>Theme resource prefix of the kind colour, e.g. "KvImage" (ADR-017).</summary>
    public string ColorKey { get; }

    public string ColorBrushKey => ColorKey + "Brush";

    public string KindName { get; }

    public ObservableCollection<TargetFileViewModel> Files { get; } = [];

    public ObservableCollection<PathRowViewModel> Paths { get; } = [];

    public IReadOnlyList<FormatOption> SharedFormats { get; }

    public TuningPanelViewModel Tuning { get; }

    public bool IsLocked { get; }

    public string LockTitle { get; }

    public string LockText { get; }

    public bool CanChooseMode => Files.Count > 1;

    public string CountText => _loc.Format("Target_Kind_Count", Files.Count);

    public string AutomationName => _loc.Format("Target_Kind_AutomationName", KindName, Files.Count);

    public bool HasFormats => SharedFormats.Count > 0;

    [ObservableProperty]
    private FormatOption? sharedFormat;

    [ObservableProperty]
    private TargetMode mode;

    /// <summary>Bound by the two radio buttons of the mode switch.</summary>
    public bool IsAllSame
    {
        get => Mode == TargetMode.AllSame;
        set
        {
            if (value)
            {
                Mode = TargetMode.AllSame;
            }
        }
    }

    public bool IsIndividual
    {
        get => Mode == TargetMode.Individual;
        set
        {
            if (value)
            {
                Mode = TargetMode.Individual;
            }
        }
    }

    /// <summary>Raised when formats or settings changed and the page has to recompute its summary.</summary>
    public event EventHandler? Changed;

    /// <summary>The drafts of this group for the plan; locked groups still deliver them (they get skipped).</summary>
    public IReadOnlyList<PlanDraft> Drafts() =>
        Files
            .Where(f => f.EffectiveOutput is not null)
            .Select(f => new PlanDraft(f.Input, Tuning.SettingsFor(f), Tuning.NamePatternText))
            .ToList();

    public void ApplyPrevious(ConversionSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var match = SharedFormats.FirstOrDefault(f => f.Id == settings.Output);
        if (match is not null)
        {
            SharedFormat = match;
        }
        Tuning.ApplyPrevious(settings);
    }

    public void ShowPrevious(ConversionSettings settings) => Tuning.ShowPrevious(settings);

    /// <summary>
    /// Step 1's zoom named a target (<see cref="Services.IWorkflowSession.PreferredOutput"/>): it becomes the
    /// shared format if every file of the group can become it. Returns false when it is not in the group.
    /// </summary>
    public bool Preselect(FormatId output)
    {
        var match = SharedFormats.FirstOrDefault(f => f.Id == output);
        if (match is null)
        {
            return false;
        }

        SharedFormat = match;
        return true;
    }

    partial void OnSharedFormatChanged(FormatOption? value)
    {
        PushFormat();
        Tuning.Rebuild();
        Raise();
    }

    partial void OnModeChanged(TargetMode value)
    {
        _silent = true;
        try
        {
            foreach (var file in Files)
            {
                file.IsIndividual = value == TargetMode.Individual;
            }
        }
        finally
        {
            _silent = false;
        }
        OnPropertyChanged(nameof(IsAllSame));
        OnPropertyChanged(nameof(IsIndividual));
        Tuning.Rebuild();
        Raise();
    }

    private void PushFormat()
    {
        _silent = true;
        try
        {
            foreach (var file in Files)
            {
                file.SharedFormat = SharedFormat;
            }
        }
        finally
        {
            _silent = false;
        }
    }

    private void OnFileChanged(TargetFileViewModel file)
    {
        if (_silent)
        {
            return;
        }
        Tuning.Rebuild();
        Raise();
    }

    private void OnTuningChanged(object? sender, EventArgs e)
    {
        foreach (var row in Paths)
        {
            row.Refresh();
        }
        Raise();
    }

    private void Raise()
    {
        OnPropertyChanged(nameof(CountText));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        Tuning.SettingsChanged -= OnTuningChanged;
        Tuning.Dispose();
    }
}
