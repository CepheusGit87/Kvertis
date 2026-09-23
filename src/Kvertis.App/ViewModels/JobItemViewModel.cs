using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kvertis.App.Helpers;
using Kvertis.App.Services;
using Kvertis.Engine.Abstractions;
using Kvertis.Engine.Conversion;
using Kvertis.Engine.Naming;
using Kvertis.Queue;
using Microsoft.UI.Xaml.Media;

namespace Kvertis.App.ViewModels;

/// <summary>Where a card is in its life: staged in the UI, then owned by the queue.</summary>
public enum JobItemState
{
    /// <summary>Format detection is running.</summary>
    Detecting = 0,
    /// <summary>Detected; waiting for Start. Settings can be changed.</summary>
    Ready,
    /// <summary>Cannot be converted (unsupported, corrupt, protected). Never enqueued.</summary>
    Rejected,
    Queued,
    Running,
    Paused,
    Completed,
    Failed,
    Cancelled,
}

/// <summary>Actions a card delegates to the page view model.</summary>
public interface IJobItemHost
{
    ILocalizer Localizer { get; }

    void OnItemSettingsChanged(JobItemViewModel item);

    void Remove(JobItemViewModel item);

    void PauseOrResume(JobItemViewModel item);

    void Cancel(JobItemViewModel item);

    void Retry(JobItemViewModel item);

    Task OpenFolderAsync(JobItemViewModel item);

    Task ShowErrorHelpAsync(JobItemViewModel item);

    void RequestPreview(JobItemViewModel item);

    void ApplyToAll(JobItemViewModel item);
}

/// <summary>One file card (docs/06-design.md, "Job-Karte").</summary>
public sealed partial class JobItemViewModel : ObservableObject
{
    public const int UnitKilobytes = 0;
    public const int UnitMegabytes = 1;

    private const string GlyphImage = "\uE91B";
    private const string GlyphAudio = "\uE8D6";
    private const string GlyphVideo = "\uE714";
    private const string GlyphDocument = "\uE8A5";
    private const string GlyphUnknown = "\uE7C3";

    private readonly IJobItemHost _host;
    private readonly ILocalizer _loc;
    private bool _suppressSettingsChanged;

    public JobItemViewModel(IJobItemHost host, string path, bool isFromClipboard, IReadOnlyList<PresetOption> presets, string namePattern, bool stripMetadata)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _loc = host.Localizer;
        FilePath = path;
        FileName = Path.GetFileName(path);
        IsFromClipboard = isFromClipboard;
        PresetOptions = presets ?? throw new ArgumentNullException(nameof(presets));
        _suppressSettingsChanged = true;
        SelectedPreset = presets.Count > 0 ? presets[0] : null;
        NamePattern = string.IsNullOrWhiteSpace(namePattern) ? OutputNamePattern.Default : namePattern;
        StripMetadata = stripMetadata;
        _suppressSettingsChanged = false;
        StatusText = _loc.Get("Card_State_Detecting");
    }

    public Guid Id { get; } = Guid.NewGuid();

    public string FilePath { get; }

    public string FileName { get; }

    /// <summary>The image came from the clipboard and lives in the app's cache; it needs an explicit output folder.</summary>
    public bool IsFromClipboard { get; }

    public InputInfo? Input { get; private set; }

    public ConversionJob? Job { get; private set; }

    public ObservableCollection<FormatOption> FormatOptions { get; } = [];

    public IReadOnlyList<PresetOption> PresetOptions { get; }

    public MediaKind Kind => Input?.Kind ?? MediaKind.Unknown;

    public bool IsVideo => Kind == MediaKind.Video;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDetecting), nameof(IsReady), nameof(IsRejected), nameof(IsRunning), nameof(IsPaused),
        nameof(IsCompleted), nameof(IsFailed), nameof(IsCancelled), nameof(IsActive), nameof(CanRemove), nameof(CanRetry),
        nameof(ShowProgress), nameof(ShowEstimate), nameof(PauseGlyph), nameof(PauseLabel), nameof(AutomationName), nameof(ShowPresetChip))]
    private JobItemState state = JobItemState.Detecting;

    [ObservableProperty]
    private ImageSource? thumbnail;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasThumbnail))]
    private bool thumbnailLoaded;

    [ObservableProperty]
    private string kindGlyph = GlyphUnknown;

    [ObservableProperty]
    private string inputFormatLabel = string.Empty;

    [ObservableProperty]
    private string sizeText = string.Empty;

    [ObservableProperty]
    private string detailsText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OutputChipText), nameof(AutomationName), nameof(OutputChipAutomationName))]
    private FormatOption? selectedFormat;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PresetChipText), nameof(PresetChipAutomationName))]
    private PresetOption? selectedPreset;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(QualityText))]
    private double quality = 80;

    /// <summary>Target size in <see cref="TargetSizeUnitIndex"/> units; NaN = no target size (NumberBox convention).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTargetSize))]
    private double targetSize = double.NaN;

    [ObservableProperty]
    private int targetSizeUnitIndex = UnitMegabytes;

    [ObservableProperty]
    private bool stripMetadata = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NamePreview))]
    private string namePattern = string.Empty;

    [ObservableProperty]
    private string estimateSizeText = string.Empty;

    [ObservableProperty]
    private string estimateTimeText = string.Empty;

    [ObservableProperty]
    private string warningText = string.Empty;

    [ObservableProperty]
    private double progressValue;

    [ObservableProperty]
    private string progressText = string.Empty;

    [ObservableProperty]
    private string resultText = string.Empty;

    [ObservableProperty]
    private string errorTitle = string.Empty;

    [ObservableProperty]
    private string errorBody = string.Empty;

    [ObservableProperty]
    private string statusText = string.Empty;

    public Estimate? Estimate { get; private set; }

    public bool IsDetecting => State == JobItemState.Detecting;

    public bool IsReady => State == JobItemState.Ready;

    public bool IsRejected => State == JobItemState.Rejected;

    public bool IsRunning => State == JobItemState.Running;

    public bool IsPaused => State == JobItemState.Paused;

    public bool IsCompleted => State == JobItemState.Completed;

    public bool IsFailed => State is JobItemState.Failed or JobItemState.Rejected;

    public bool IsCancelled => State == JobItemState.Cancelled;

    /// <summary>Owned by the queue and not finished.</summary>
    public bool IsActive => State is JobItemState.Queued or JobItemState.Running or JobItemState.Paused;

    public bool CanRemove => State is not (JobItemState.Running or JobItemState.Detecting);

    public bool CanRetry => State is JobItemState.Failed or JobItemState.Cancelled;

    public bool ShowProgress => State is JobItemState.Running or JobItemState.Paused or JobItemState.Queued;

    public bool ShowEstimate => State == JobItemState.Ready;

    public bool ShowPresetChip => State == JobItemState.Ready && Kind is MediaKind.Image or MediaKind.Audio or MediaKind.Video;

    public bool HasThumbnail => ThumbnailLoaded;

    public bool HasTargetSize => !double.IsNaN(TargetSize) && TargetSize > 0;

    public string PauseGlyph => State == JobItemState.Paused ? "\uE768" : "\uE769";

    public string PauseLabel => _loc.Get(State == JobItemState.Paused ? "Card_Resume_Label" : "Card_Pause_Label");

    public string OutputChipText => SelectedFormat is null ? string.Empty : _loc.Format("Card_OutputChip_Text", SelectedFormat.Label);

    public string OutputChipAutomationName => SelectedFormat is null ? string.Empty : _loc.Format("Card_OutputChip_AutomationName", SelectedFormat.Label);

    public string PresetChipText => SelectedPreset?.Label ?? string.Empty;

    public string PresetChipAutomationName => _loc.Format("Card_PresetChip_AutomationName", PresetChipText);

    public string QualityText => ((int)Math.Round(Quality)).ToString(CultureInfo.CurrentCulture);

    public string NamePreview
    {
        get
        {
            var extension = SelectedFormat?.Id.Id ?? "out";
            try
            {
                return OutputNamePattern.Render(NamePattern, FilePath, extension, DateTimeOffset.Now, 1);
            }
            catch (ArgumentException)
            {
                return string.Empty;
            }
        }
    }

    /// <summary>Screen reader name of the whole card: "photo.heic, HEIC to JPG, waiting" (docs/06, accessibility).</summary>
    public string AutomationName =>
        SelectedFormat is null
            ? _loc.Format("Card_AutomationName_NoFormat", FileName, StatusText)
            : _loc.Format("Card_AutomationName", FileName, InputFormatLabel, SelectedFormat.Label, StatusText);

    /// <summary>List items report this to screen readers when no explicit name is set.</summary>
    public override string ToString() => AutomationName;

    /// <summary>Called once detection succeeded.</summary>
    public void Initialize(InputInfo input, string inputFormatLabel, IEnumerable<FormatOption> options, string sizeText, string detailsText, string warningText)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(options);
        Input = input;
        _suppressSettingsChanged = true;
        try
        {
            FormatOptions.Clear();
            foreach (var option in options)
            {
                FormatOptions.Add(option);
            }
            SelectedFormat = FormatOptions.FirstOrDefault(o => o.IsSuggested) ?? FormatOptions.FirstOrDefault();
        }
        finally
        {
            _suppressSettingsChanged = false;
        }
        InputFormatLabel = inputFormatLabel;
        KindGlyph = input.Kind switch
        {
            MediaKind.Image => GlyphImage,
            MediaKind.Audio => GlyphAudio,
            MediaKind.Video => GlyphVideo,
            MediaKind.Document => GlyphDocument,
            _ => GlyphUnknown,
        };
        SizeText = sizeText;
        DetailsText = detailsText;
        WarningText = warningText;
        SetState(JobItemState.Ready);
        OnPropertyChanged(nameof(Kind));
        OnPropertyChanged(nameof(IsVideo));
        OnPropertyChanged(nameof(ShowPresetChip));
    }

    /// <summary>Takes over output format and options from earlier settings (history "again").</summary>
    public void ApplySettings(ConversionSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _suppressSettingsChanged = true;
        try
        {
            SelectedPreset = PresetOptions.FirstOrDefault(p => p.Preset == settings.Preset) ?? SelectedPreset;
            var format = FormatOptions.FirstOrDefault(o => o.Id == settings.Output);
            if (format is not null)
            {
                SelectedFormat = format;
            }
            Quality = settings.QualityClamped;
            StripMetadata = settings.Metadata == MetadataPolicy.Strip;
            SetTargetSizeBytes(settings.TargetSizeBytes);
        }
        finally
        {
            _suppressSettingsChanged = false;
        }
        _host.OnItemSettingsChanged(this);
    }

    /// <summary>Copies the "More" options of <paramref name="source"/> (apply to all).</summary>
    public void CopyOptionsFrom(JobItemViewModel source)
    {
        ArgumentNullException.ThrowIfNull(source);
        _suppressSettingsChanged = true;
        try
        {
            if (source.Kind == Kind)
            {
                SelectedPreset = PresetOptions.FirstOrDefault(p => p.Preset == source.SelectedPreset?.Preset) ?? SelectedPreset;
                var format = source.SelectedFormat is null ? null : FormatOptions.FirstOrDefault(o => o.Id == source.SelectedFormat.Id);
                if (format is not null)
                {
                    SelectedFormat = format;
                }
                Quality = source.Quality;
                TargetSize = source.TargetSize;
                TargetSizeUnitIndex = source.TargetSizeUnitIndex;
            }
            StripMetadata = source.StripMetadata;
            NamePattern = source.NamePattern;
        }
        finally
        {
            _suppressSettingsChanged = false;
        }
        _host.OnItemSettingsChanged(this);
    }

    public void SetRejected(ErrorMessage error)
    {
        ArgumentNullException.ThrowIfNull(error);
        ErrorTitle = error.Title;
        ErrorBody = error.Body;
        SetState(JobItemState.Rejected);
    }

    public void ApplyEstimate(Estimate estimate)
    {
        ArgumentNullException.ThrowIfNull(estimate);
        Estimate = estimate;
        if (estimate.Confidence <= 0)
        {
            EstimateSizeText = string.Empty;
            EstimateTimeText = string.Empty;
            return;
        }
        EstimateSizeText = _loc.Format("Card_EstimateSize_Text", Formatting.Bytes(_loc, estimate.OutputBytes));
        EstimateTimeText = _loc.Format("Card_EstimateTime_Text", Formatting.Duration(_loc, estimate.Duration));
    }

    /// <summary>Builds the engine settings from the card. Preset hints (max dimension, bitrate) come from the preset catalog.</summary>
    public ConversionSettings BuildSettings()
    {
        var output = SelectedFormat?.Id ?? throw new InvalidOperationException("No output format selected.");
        var preset = SelectedPreset?.Preset ?? ConversionPreset.None;
        var metadata = StripMetadata ? MetadataPolicy.Strip : MetadataPolicy.Keep;
        var advanced = PresetCatalog.Apply(new ConversionSettings(output, Preset: preset), Kind).Advanced;
        return new ConversionSettings(output, (int)Math.Round(Quality), GetTargetSizeBytes(), metadata, preset, advanced);
    }

    public long? GetTargetSizeBytes()
    {
        if (!HasTargetSize)
        {
            return null;
        }
        var factor = TargetSizeUnitIndex == UnitKilobytes ? 1024d : 1024d * 1024d;
        return (long)(TargetSize * factor);
    }

    public void AttachJob(ConversionJob job)
    {
        Job = job ?? throw new ArgumentNullException(nameof(job));
        ProgressValue = 0;
        ProgressText = string.Empty;
        ResultText = string.Empty;
        ErrorTitle = string.Empty;
        ErrorBody = string.Empty;
        UpdateFromJob(job, null);
    }

    /// <summary>Copies the queue's view of the job. Called on the UI thread.</summary>
    public void UpdateFromJob(ConversionJob job, ErrorMessageMapper? errors)
    {
        ArgumentNullException.ThrowIfNull(job);
        if (!ReferenceEquals(job, Job))
        {
            return;
        }

        var progress = job.Progress;
        ProgressValue = Math.Clamp(progress.Fraction, 0, 1) * 100;
        ProgressText = progress.Remaining is { } remaining && job.State == Kvertis.Queue.JobState.Running
            ? _loc.Format("Card_Remaining_Text", Formatting.Duration(_loc, remaining))
            : string.Empty;

        switch (job.State)
        {
            case Kvertis.Queue.JobState.Completed when job.Result is { } result:
                ResultText = _loc.Format("Card_Result_Text",
                    Formatting.Bytes(_loc, result.InputBytes),
                    Formatting.Bytes(_loc, result.OutputBytes),
                    Formatting.Change(_loc, result.InputBytes, result.OutputBytes));
                break;
            case Kvertis.Queue.JobState.Failed when errors is not null:
                var message = errors.Map(job.Error);
                ErrorTitle = message.Title;
                ErrorBody = message.Body;
                break;
        }

        SetState(job.State switch
        {
            Kvertis.Queue.JobState.Queued => JobItemState.Queued,
            Kvertis.Queue.JobState.Running => JobItemState.Running,
            Kvertis.Queue.JobState.Paused => JobItemState.Paused,
            Kvertis.Queue.JobState.Completed => JobItemState.Completed,
            Kvertis.Queue.JobState.Failed => JobItemState.Failed,
            _ => JobItemState.Cancelled,
        });
    }

    [RelayCommand]
    private void Remove() => _host.Remove(this);

    [RelayCommand]
    private void PauseOrResume() => _host.PauseOrResume(this);

    [RelayCommand]
    private void Cancel() => _host.Cancel(this);

    [RelayCommand]
    private void Retry() => _host.Retry(this);

    [RelayCommand]
    private Task OpenFolder() => _host.OpenFolderAsync(this);

    [RelayCommand]
    private Task ShowErrorHelp() => _host.ShowErrorHelpAsync(this);

    [RelayCommand]
    private void Preview() => _host.RequestPreview(this);

    [RelayCommand]
    private void ApplyToAll() => _host.ApplyToAll(this);

    partial void OnSelectedFormatChanged(FormatOption? value)
    {
        OnPropertyChanged(nameof(NamePreview));
        NotifySettingsChanged();
    }

    partial void OnSelectedPresetChanged(PresetOption? value)
    {
        if (_suppressSettingsChanged || value is null || Input is null)
        {
            NotifySettingsChanged();
            return;
        }

        // A preset fills in the visible options; the user can still adjust them afterwards.
        _suppressSettingsChanged = true;
        try
        {
            if (PresetCatalog.Get(value.Preset, Kind) is { } definition)
            {
                if (definition.Output is { } output && FormatOptions.FirstOrDefault(o => o.Id == output) is { } format)
                {
                    SelectedFormat = format;
                }
                if (definition.Quality is { } presetQuality)
                {
                    Quality = presetQuality;
                }
                SetTargetSizeBytes(definition.Lossless ? null : definition.TargetSizeBytes);
                StripMetadata = definition.Metadata == MetadataPolicy.Strip;
            }
        }
        finally
        {
            _suppressSettingsChanged = false;
        }
        NotifySettingsChanged();
    }

    partial void OnQualityChanged(double value) => NotifySettingsChanged();

    partial void OnTargetSizeChanged(double value) => NotifySettingsChanged();

    partial void OnTargetSizeUnitIndexChanged(int value) => NotifySettingsChanged();

    partial void OnStripMetadataChanged(bool value) => NotifySettingsChanged();

    partial void OnStatusTextChanged(string value) => OnPropertyChanged(nameof(AutomationName));

    private void SetTargetSizeBytes(long? bytes)
    {
        if (bytes is not { } value || value <= 0)
        {
            TargetSize = double.NaN;
            return;
        }
        if (value >= 1024 * 1024)
        {
            TargetSizeUnitIndex = UnitMegabytes;
            TargetSize = Math.Round(value / (1024d * 1024d), 1);
        }
        else
        {
            TargetSizeUnitIndex = UnitKilobytes;
            TargetSize = Math.Round(value / 1024d);
        }
    }

    private void SetState(JobItemState value)
    {
        State = value;
        StatusText = _loc.Get("Card_State_" + value.ToString());
    }

    private void NotifySettingsChanged()
    {
        if (!_suppressSettingsChanged && State == JobItemState.Ready)
        {
            _host.OnItemSettingsChanged(this);
        }
    }
}
