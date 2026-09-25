using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Kvertis.App.Helpers;
using Kvertis.App.Services;
using Kvertis.Engine.Abstractions;
using Kvertis.Engine.Formats;
using Kvertis.Engine.Naming;
using Kvertis.Engine.Tuning;
using Microsoft.Extensions.Logging;

namespace Kvertis.App.ViewModels.Target;

/// <summary>
/// The output settings of one kind: ring, size bar, zones, exact target size, "Weiteres" and "Was sich
/// ändert" (docs/03-architektur.md, "Datenmodell der Ziel-Seite"). Everything here applies per kind, never
/// per file; "Jede einzeln" only changes the output format of a file.
/// </summary>
public sealed partial class TuningPanelViewModel : ObservableObject, IDisposable
{
    private readonly ILocalizer _loc;
    private readonly IUiDispatcher _ui;
    private readonly IEstimator _estimator;
    private readonly FormatRegistry _registry;
    private readonly ILogger _logger;
    private readonly Dictionary<TuningAspect, int> _overrides = [];
    private readonly Dictionary<string, GradeSizeTable> _tables = new(StringComparer.OrdinalIgnoreCase);
    private readonly Debouncer _slow;
    private IReadOnlyList<TargetFileViewModel> _files = [];
    private CancellationTokenSource? _build;
    private TargetFileViewModel? _largest;
    private long _largestBytes;
    private bool _silent;
    private bool _disposed;

    public TuningPanelViewModel(
        MediaKind kind,
        ILocalizer loc,
        IUiDispatcher ui,
        IEstimator estimator,
        FormatRegistry registry,
        ILogger logger,
        string namePattern,
        bool stripMetadata,
        TimeProvider? time = null)
    {
        Kind = kind;
        _loc = loc ?? throw new ArgumentNullException(nameof(loc));
        _ui = ui ?? throw new ArgumentNullException(nameof(ui));
        _estimator = estimator ?? throw new ArgumentNullException(nameof(estimator));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        SizeBar = new SizeBarViewModel(loc);
        SizeBar.Dragged += OnBarDragged;
        _slow = new Debouncer(RefreshSlowParts, _ui.Post, Debouncer.DefaultDelay, time);
        namePatternText = string.IsNullOrWhiteSpace(namePattern) ? OutputNamePattern.Default : namePattern;
        this.stripMetadata = stripMetadata;
        grade = QualityGrade.Default.Value;
    }

    public MediaKind Kind { get; }

    public SizeBarViewModel SizeBar { get; }

    public ObservableCollection<ZoneViewModel> Zones { get; } = [];

    public ObservableCollection<EffectViewModel> Effects { get; } = [];

    /// <summary>Raised whenever the resulting settings changed, so the group can refresh its rows.</summary>
    public event EventHandler? SettingsChanged;

    /// <summary>False for documents and 3D: no ring, no bar, no zones; only effects and "Weiteres".</summary>
    [ObservableProperty]
    private bool supportsGrade;

    [ObservableProperty]
    private int grade;

    [ObservableProperty]
    private string bandText = string.Empty;

    [ObservableProperty]
    private string sizeText = string.Empty;

    [ObservableProperty]
    private bool exactTargetEnabled;

    [ObservableProperty]
    private double exactTargetMegabytes = 2;

    [ObservableProperty]
    private bool exactTargetUnreachable;

    [ObservableProperty]
    private bool stripMetadata;

    [ObservableProperty]
    private string namePatternText = OutputNamePattern.Default;

    [ObservableProperty]
    private string namePreview = string.Empty;

    [ObservableProperty]
    private bool hasPrevious;

    [ObservableProperty]
    private int previousGrade;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviousBarLeft))]
    private double previousBarPosition;

    [ObservableProperty]
    private string previousText = string.Empty;

    /// <summary>Offset of the dashed "damals" mark under the size bar, in pixels.</summary>
    public double PreviousBarLeft => PreviousBarPosition / TargetPlanner.BarSteps * SizeBarViewModel.BarWidth;

    /// <summary>"Qualität: 80 von 100, gut" for the ring's screen reader value.</summary>
    public string GradeAutomationValue => _loc.Format("Target_Grade_AutomationValue", Grade, BandText, SizeText);

    /// <summary>Colour of the ring: mint from 65, amber from 45, coral below.</summary>
    public string GradeBrushKey => Grade >= 65 ? "KvMintBrush" : Grade >= 45 ? "KvVideoBrush" : "KvErrorBrush";

    /// <summary>Exact target size in bytes, null when the switch is off.</summary>
    public long? ExactTargetBytes =>
        ExactTargetEnabled && ExactTargetMegabytes > 0
            ? (long)Math.Round(ExactTargetMegabytes * 1024 * 1024, MidpointRounding.AwayFromZero)
            : null;

    /// <summary>The largest estimated output of the group; the summary of the page adds these up.</summary>
    public long TotalBytes => _files.Sum(f => f.EstimatedBytes);

    /// <summary>Attaches the files of the group and rebuilds everything.</summary>
    public void SetFiles(IReadOnlyList<TargetFileViewModel> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        _files = files;
        _largest = files.OrderByDescending(f => f.Input.SizeBytes).FirstOrDefault();
        Rebuild();
    }

    /// <summary>Takes over grade, zones, metadata and target size of a history entry ("Alte Werte übernehmen").</summary>
    public void ApplyPrevious(ConversionSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (_largest is null)
        {
            return;
        }
        _overrides.Clear();
        foreach (var level in GradeMapper.Aspects(settings, _largest.Input, _registry).Where(a => a.Adjustable))
        {
            _overrides[level.Aspect] = level.Value;
        }
        StripMetadata = settings.Metadata == MetadataPolicy.Strip;
        ExactTargetEnabled = settings.TargetSizeBytes is > 0;
        if (settings.TargetSizeBytes is { } bytes and > 0)
        {
            ExactTargetMegabytes = Math.Round(bytes / 1024d / 1024d, 2);
        }
        SetGradeSilently(GradeMapper.GradeOf(settings, _largest.Input, _registry).Clamped);
        Rebuild();
    }

    /// <summary>Shows the "damals" marks of a history entry without changing anything.</summary>
    public void ShowPrevious(ConversionSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (_largest is null)
        {
            return;
        }
        var previous = GradeMapper.GradeOf(settings, _largest.Input, _registry).Clamped;
        PreviousGrade = previous;
        HasPrevious = true;
        try
        {
            var bytes = _estimator.Estimate(_largest.Input, settings).OutputBytes;
            PreviousBarPosition = TargetPlanner.PositionOf(bytes, SizeBar.MinBytes, SizeBar.MaxBytes);
            PreviousText = _loc.Format("Target_Previous_Mark", previous, Formatting.Bytes(_loc, bytes));
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            _logger.LogDebug(ex, "Estimating the previous settings failed");
            PreviousBarPosition = 0;
            PreviousText = _loc.Format("Target_Previous_Mark", previous, string.Empty);
        }
    }

    /// <summary>The settings this panel produces for one file of the group.</summary>
    public ConversionSettings SettingsFor(TargetFileViewModel file)
    {
        ArgumentNullException.ThrowIfNull(file);
        var output = file.EffectiveOutput ?? file.Input.Format;
        var baseline = new ConversionSettings(
            output,
            Metadata: StripMetadata ? MetadataPolicy.Strip : MetadataPolicy.Keep,
            TargetSizeBytes: ExactTargetBytes);
        var settings = GradeMapper.Apply(new QualityGrade(Grade), baseline, file.Input, _registry);
        foreach (var pair in _overrides)
        {
            settings = GradeMapper.WithAspect(settings, pair.Key, pair.Value, file.Input, _registry);
        }
        return settings;
    }

    /// <summary>Rebuilds the grade tables off the UI thread and refreshes everything that hangs on them.</summary>
    public void Rebuild()
    {
        _build?.Cancel();
        _build?.Dispose();
        _build = null;
        if (_files.Count == 0 || _largest is null)
        {
            SupportsGrade = false;
            return;
        }

        var output = _largest.EffectiveOutput ?? _largest.Input.Format;
        SupportsGrade = GradeMapper.SupportsGrade(Kind, output, _registry);

        var cts = new CancellationTokenSource();
        _build = cts;
        var work = _files
            .Select(f => (File: f, Baseline: new ConversionSettings(
                f.EffectiveOutput ?? f.Input.Format,
                Metadata: StripMetadata ? MetadataPolicy.Strip : MetadataPolicy.Keep)))
            .ToList();

        _ = BuildTablesAsync(work, cts.Token);
    }

    private async Task BuildTablesAsync(
        IReadOnlyList<(TargetFileViewModel File, ConversionSettings Baseline)> work,
        CancellationToken token)
    {
        try
        {
            var built = await Task.Run(
                () =>
                {
                    var map = new Dictionary<string, GradeSizeTable>(StringComparer.OrdinalIgnoreCase);
                    foreach (var (file, baseline) in work)
                    {
                        token.ThrowIfCancellationRequested();
                        map[file.Input.Path] = GradeSizeTableBuilder.Build(file.Input, baseline, _estimator, _registry);
                    }
                    return map;
                },
                token).ConfigureAwait(true);
            if (token.IsCancellationRequested || _disposed)
            {
                return;
            }
            _tables.Clear();
            foreach (var pair in built)
            {
                _tables[pair.Key] = pair.Value;
            }
            ApplyTables();
        }
        catch (OperationCanceledException)
        {
            // A newer change won; its own build refreshes the page.
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Building the grade tables failed");
        }
    }

    private void ApplyTables()
    {
        if (_largest is null)
        {
            return;
        }
        var min = _tables.Values.Select(t => t.MinBytes).DefaultIfEmpty(1).Min();
        var max = Math.Max(_files.Max(f => f.Input.SizeBytes), _tables.Values.Select(t => t.MaxBytes).DefaultIfEmpty(2).Max());
        SizeBar.SetScale(min, max, LargestTable, SizeMarks.For(Kind));
        RefreshFast();
        RefreshSlowParts();
    }

    private GradeSizeTable? LargestTable =>
        _largest is not null && _tables.TryGetValue(_largest.Input.Path, out var table) ? table : null;

    /// <summary>Everything that must follow the handle without delay: numbers and estimates.</summary>
    private void RefreshFast()
    {
        var current = new QualityGrade(Grade);
        long largestBytes = 0;
        foreach (var file in _files)
        {
            var bytes = _tables.TryGetValue(file.Input.Path, out var table)
                ? table.BytesAt(current)
                : file.Input.SizeBytes;
            file.SetEstimate(bytes);
            largestBytes = Math.Max(largestBytes, bytes);
        }
        BandText = _loc.Get("Grade_Band_" + current.Band.ToString());
        SizeText = _loc.Format("Target_Size_Approx", Formatting.Bytes(_loc, largestBytes));
        _largestBytes = largestBytes;
        if (SizeBar.HasScale)
        {
            SizeBar.ShowBytes(largestBytes, ExactTargetEnabled);
        }
        OnPropertyChanged(nameof(GradeAutomationValue));
        OnPropertyChanged(nameof(GradeBrushKey));
        OnPropertyChanged(nameof(TotalBytes));
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Zones, effects and the unreachable hint; debounced while dragging.</summary>
    private void RefreshSlowParts()
    {
        if (_disposed || _largest is null)
        {
            return;
        }
        var settings = SettingsFor(_largest);
        var levels = GradeMapper.Aspects(settings, _largest.Input, _registry);
        SyncZones(levels);

        Effects.Clear();
        foreach (var effect in EffectAnalyzer.Analyze(_largest.Input, settings, _registry))
        {
            Effects.Add(EffectViewModel.Create(_loc, effect));
        }
        ExactTargetUnreachable = ExactTargetBytes is { } wanted
            && _tables.Values.Any(t => wanted < t.MinBytes);
        if (ExactTargetUnreachable)
        {
            Effects.Insert(0, EffectViewModel.Create(
                _loc,
                new ConversionEffect(EffectCode.TargetSizeMayBeUnreachable, EffectSeverity.Warning)));
        }
        NamePreview = RenderPreview();
        if (SizeBar.HasScale)
        {
            // 50 ms after the last move: the drag is over, the handle may follow the grade again.
            SizeBar.Settle(_largestBytes);
        }
    }

    private void SyncZones(IReadOnlyList<AspectLevel> levels)
    {
        if (Zones.Count == levels.Count && Zones.Select(z => z.Aspect).SequenceEqual(levels.Select(l => l.Aspect)))
        {
            for (var i = 0; i < levels.Count; i++)
            {
                Zones[i].Apply(levels[i]);
            }
            return;
        }
        Zones.Clear();
        foreach (var level in levels)
        {
            var zone = new ZoneViewModel(_loc, level, OnZoneChanged);
            Zones.Add(zone);
        }
    }

    private void OnZoneChanged(TuningAspect aspect, int value)
    {
        if (_silent || _largest is null)
        {
            return;
        }
        _overrides[aspect] = value;
        SetGradeSilently(GradeMapper.GradeOf(SettingsFor(_largest), _largest.Input, _registry).Clamped);
        RefreshFast();
        _slow.Request();
    }

    private void OnBarDragged(object? sender, long bytes)
    {
        if (LargestTable is not { } table)
        {
            return;
        }
        // The bar is a second view of the grade (ADR-019): position -> bytes -> grade, nothing else.
        SetGradeSilently(table.GradeForBytes(bytes).Clamped);
        _overrides.Clear();
        RefreshFast();
        _slow.Request();
    }

    private void SetGradeSilently(int value)
    {
        _silent = true;
        try
        {
            Grade = value;
        }
        finally
        {
            _silent = false;
        }
    }

    private string RenderPreview()
    {
        if (_largest is null)
        {
            return string.Empty;
        }
        try
        {
            return OutputNamePattern.Render(
                NamePatternText,
                _largest.Input.Path,
                (_largest.EffectiveOutput ?? _largest.Input.Format).Id,
                DateTimeOffset.Now,
                1);
        }
        catch (ArgumentException)
        {
            return string.Empty;
        }
    }

    partial void OnGradeChanged(int value)
    {
        if (_silent)
        {
            return;
        }
        // Dragging the ring re-couples the zones (docs/03-architektur.md, step 5).
        _overrides.Clear();
        RefreshFast();
        _slow.Request();
    }

    partial void OnStripMetadataChanged(bool value) => Rebuild();

    partial void OnNamePatternTextChanged(string value) => NamePreview = RenderPreview();

    partial void OnExactTargetEnabledChanged(bool value) => _slow.Request();

    partial void OnExactTargetMegabytesChanged(double value) => _slow.Request();

    partial void OnBandTextChanged(string value) => OnPropertyChanged(nameof(GradeAutomationValue));

    partial void OnSizeTextChanged(string value) => OnPropertyChanged(nameof(GradeAutomationValue));

    public void Dispose()
    {
        _disposed = true;
        SizeBar.Dragged -= OnBarDragged;
        _slow.Dispose();
        _build?.Cancel();
        _build?.Dispose();
        _build = null;
    }
}
