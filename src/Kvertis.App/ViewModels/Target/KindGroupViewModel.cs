using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Kvertis.App.Helpers;
using Kvertis.App.Services;
using Kvertis.Engine.Abstractions;
using Kvertis.Engine.Formats;
using Kvertis.Engine.Tuning;
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
    private readonly IEstimator _estimator;
    private readonly FormatRegistry _registry;
    private readonly ILogger _logger;
    private CancellationTokenSource? _sizes;
    private bool _silent;
    private bool _disposed;

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
        _estimator = estimator ?? throw new ArgumentNullException(nameof(estimator));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

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

        foreach (var option in sharedFormats)
        {
            TargetOptions.Add(new TargetFormatViewModel(loc, option, loc.Get("Format_" + option.Id.Id + "_Hint")));
        }

        sharedFormat = sharedFormats.FirstOrDefault(f => f.IsSuggested)
            ?? (sharedFormats.Count > 0 ? sharedFormats[0] : null);
        selectedTarget = TargetOptions.FirstOrDefault(t => t.Option == sharedFormat);
        SyncTargetSelection();
        PushFormat();
        Tuning.SettingsChanged += OnTuningChanged;
        Tuning.Settled += OnTuningSettled;
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

    /// <summary>The rows of the target list "WIRD ZU", one per shared format, with a size estimate each.</summary>
    public ObservableCollection<TargetFormatViewModel> TargetOptions { get; } = [];

    /// <summary>The id of the recommended format, for the drawn ways; null when there is none.</summary>
    public string? RecommendedId => SharedFormats.FirstOrDefault(f => f.IsSuggested)?.Id.Id;

    public TuningPanelViewModel Tuning { get; }

    public bool IsLocked { get; }

    public string LockTitle { get; }

    public string LockText { get; }

    public bool CanChooseMode => Files.Count > 1;

    public string CountText => _loc.Format("Target_Kind_Count", Files.Count);

    /// <summary>The bare number next to the kind name in the list ("Bilder 2").</summary>
    public string FileCountText => Files.Count.ToString(CultureInfo.CurrentCulture);

    /// <summary>Heading of the group with the right singular or plural: "1 Bild", "2 Bilder".</summary>
    public string TitleText => HeadingKey(Kind, Files.Count == 1) is { } key
        ? _loc.Format(key, Files.Count)
        : _loc.Format("Target_Kind_Count", Files.Count);

    /// <summary>Source formats and their total size: "HEIC, PNG · 10,6 MB".</summary>
    public string SourcesText => _loc.Format(
        "Target_Group_Sources",
        string.Join(", ", Files.Select(f => f.SourceLabel).Distinct(StringComparer.OrdinalIgnoreCase)),
        Formatting.Bytes(_loc, Files.Sum(f => f.Input.SizeBytes)));

    /// <summary>First half of the size line in the middle: "10,6 MB →".</summary>
    public string FromText => _loc.Format("Target_Group_From", Formatting.Bytes(_loc, Files.Sum(f => f.Input.SizeBytes)));

    /// <summary>Second half: the estimated output of the group, "≈ 6,1 MB".</summary>
    public string ToText => _loc.Format("Target_Size_Approx", Formatting.Bytes(_loc, Tuning.TotalBytes));

    /// <summary>The group's output format under the kind name ("JPG").</summary>
    public string TargetLabel => SharedFormat?.Label ?? string.Empty;

    public string AutomationName => _loc.Format("Target_Kind_AutomationName", KindName, Files.Count);

    public bool HasFormats => SharedFormats.Count > 0;

    [ObservableProperty]
    private FormatOption? sharedFormat;

    /// <summary>The chosen row of the target list; kept in step with <see cref="SharedFormat"/> both ways.</summary>
    [ObservableProperty]
    private TargetFormatViewModel? selectedTarget;

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
        OnPropertyChanged(nameof(TargetLabel));
        var target = TargetOptions.FirstOrDefault(t => t.Option == value);
        if (!ReferenceEquals(SelectedTarget, target))
        {
            SelectedTarget = target;
        }
        PushFormat();
        Tuning.Rebuild();
        Raise();
    }

    partial void OnSelectedTargetChanged(TargetFormatViewModel? value)
    {
        SyncTargetSelection();
        // The list may clear its selection while it rebuilds; the group keeps its format then.
        if (value is not null && !ReferenceEquals(SharedFormat, value.Option))
        {
            SharedFormat = value.Option;
        }
    }

    private void SyncTargetSelection()
    {
        foreach (var option in TargetOptions)
        {
            option.IsSelected = ReferenceEquals(option, SelectedTarget);
        }
    }

    private void OnTuningSettled(object? sender, EventArgs e) => RefreshTargetSizes();

    /// <summary>
    /// Estimates the group in every shared format off the UI thread, with the current grade and metadata
    /// choice; a newer request cancels the older one, like the grade tables. The chosen format shows the
    /// panel's own total, so the list and the ring never disagree.
    /// </summary>
    private void RefreshTargetSizes()
    {
        _sizes?.Cancel();
        _sizes?.Dispose();
        _sizes = null;
        if (_disposed || TargetOptions.Count == 0 || Files.Count == 0)
        {
            return;
        }

        var cts = new CancellationTokenSource();
        _sizes = cts;
        var grade = new QualityGrade(Tuning.Grade);
        var metadata = Tuning.StripMetadata ? MetadataPolicy.Strip : MetadataPolicy.Keep;
        var inputs = Files.Select(f => f.Input).ToList();
        var formats = TargetOptions.Select(t => t.Id).ToList();
        _ = EstimateTargetsAsync(inputs, formats, grade, metadata, cts.Token);
    }

    private async Task EstimateTargetsAsync(
        IReadOnlyList<InputInfo> inputs,
        IReadOnlyList<FormatId> formats,
        QualityGrade grade,
        MetadataPolicy metadata,
        CancellationToken token)
    {
        try
        {
            var totals = await Task.Run(
                () =>
                {
                    var result = new long[formats.Count];
                    for (var i = 0; i < formats.Count; i++)
                    {
                        token.ThrowIfCancellationRequested();
                        long sum = 0;
                        foreach (var input in inputs)
                        {
                            var baseline = new ConversionSettings(formats[i], Metadata: metadata);
                            var settings = GradeMapper.Apply(grade, baseline, input, _registry);
                            sum += _estimator.Estimate(input, settings).OutputBytes;
                        }
                        result[i] = sum;
                    }
                    return result;
                },
                token).ConfigureAwait(true);
            if (token.IsCancellationRequested || _disposed)
            {
                return;
            }
            for (var i = 0; i < TargetOptions.Count && i < totals.Length; i++)
            {
                var option = TargetOptions[i];
                option.SetEstimate(ReferenceEquals(option, SelectedTarget) && Tuning.TotalBytes > 0 ? Tuning.TotalBytes : totals[i]);
            }
        }
        catch (OperationCanceledException)
        {
            // A newer change won.
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            _logger.LogDebug(ex, "Estimating the target formats failed");
        }
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
        OnPropertyChanged(nameof(ToText));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    // Literal keys, one pair per kind, so the resource check finds every one of them.
    private static string? HeadingKey(MediaKind kind, bool one) => kind switch
    {
        MediaKind.Image => one ? "Target_Heading_Image_One" : "Target_Heading_Image_Many",
        MediaKind.Audio => one ? "Target_Heading_Audio_One" : "Target_Heading_Audio_Many",
        MediaKind.Video => one ? "Target_Heading_Video_One" : "Target_Heading_Video_Many",
        MediaKind.Document => one ? "Target_Heading_Document_One" : "Target_Heading_Document_Many",
        MediaKind.Model3D => one ? "Target_Heading_Model3D_One" : "Target_Heading_Model3D_Many",
        _ => null,
    };

    public void Dispose()
    {
        _disposed = true;
        Tuning.SettingsChanged -= OnTuningChanged;
        Tuning.Settled -= OnTuningSettled;
        Tuning.Dispose();
        _sizes?.Cancel();
        _sizes?.Dispose();
        _sizes = null;
    }
}
