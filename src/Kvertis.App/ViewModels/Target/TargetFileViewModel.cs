using CommunityToolkit.Mvvm.ComponentModel;
using Kvertis.App.Helpers;
using Kvertis.App.Services;
using Kvertis.Engine.Abstractions;
using Kvertis.Engine.Formats;

namespace Kvertis.App.ViewModels.Target;

/// <summary>One file card in the left column of step 2.</summary>
public sealed partial class TargetFileViewModel : ObservableObject
{
    private readonly ILocalizer _loc;
    private readonly Action<TargetFileViewModel> _changed;

    public TargetFileViewModel(
        ILocalizer loc,
        StagedFile staged,
        FormatRegistry registry,
        IReadOnlyList<FormatOption> formats,
        Action<TargetFileViewModel> changed)
    {
        ArgumentNullException.ThrowIfNull(loc);
        ArgumentNullException.ThrowIfNull(staged);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(formats);
        _loc = loc;
        _changed = changed ?? throw new ArgumentNullException(nameof(changed));

        Input = staged.Input;
        ThumbnailPath = staged.ThumbnailPath;
        Formats = formats;
        FileName = Path.GetFileName(staged.Input.Path);
        SourceLabel = TargetPlanner.Label(registry, staged.Input.Format);
        SizeText = Formatting.Bytes(loc, staged.Input.SizeBytes);
        DetailsText = Describe(staged.Input);
        WarningText = string.Join(" ", staged.Input.Warnings.Select(w => loc.Get("Warning_" + w.ToString())));
    }

    public InputInfo Input { get; }

    public string? ThumbnailPath { get; }

    public bool HasThumbnail => !string.IsNullOrEmpty(ThumbnailPath);

    public string FileName { get; }

    /// <summary>Source format name ("HEIC").</summary>
    public string SourceLabel { get; }

    public string SizeText { get; }

    public string DetailsText { get; }

    public string WarningText { get; }

    public bool HasWarning => !string.IsNullOrEmpty(WarningText);

    /// <summary>What this file alone could become; used in "Jede einzeln".</summary>
    public IReadOnlyList<FormatOption> Formats { get; }

    /// <summary>Only set in "Jede einzeln"; null means the group's shared format applies.</summary>
    [ObservableProperty]
    private FormatOption? ownFormat;

    /// <summary>The group's choice, kept in sync by <see cref="KindGroupViewModel"/>.</summary>
    [ObservableProperty]
    private FormatOption? sharedFormat;

    [ObservableProperty]
    private bool isIndividual;

    [ObservableProperty]
    private long estimatedBytes;

    [ObservableProperty]
    private string estimateText = string.Empty;

    /// <summary>The output that will be produced: the file's own choice wins over the group's.</summary>
    public FormatOption? EffectiveFormat => IsIndividual ? OwnFormat ?? SharedFormat : SharedFormat;

    public FormatId? EffectiveOutput => EffectiveFormat?.Id;

    public string TargetLabel => EffectiveFormat?.Label ?? string.Empty;

    public string AutomationName => _loc.Format("Target_File_AutomationName", FileName, SourceLabel, TargetLabel, EstimateText);

    /// <summary>Shows the estimate that the grade table produced for this file.</summary>
    public void SetEstimate(long bytes)
    {
        EstimatedBytes = bytes;
        EstimateText = _loc.Format("Target_Size_Approx", Formatting.Bytes(_loc, bytes));
    }

    partial void OnOwnFormatChanged(FormatOption? value) => RaiseOutput(notify: true);

    partial void OnSharedFormatChanged(FormatOption? value) => RaiseOutput(notify: false);

    partial void OnIsIndividualChanged(bool value) => RaiseOutput(notify: false);

    partial void OnEstimateTextChanged(string value) => OnPropertyChanged(nameof(AutomationName));

    private void RaiseOutput(bool notify)
    {
        OnPropertyChanged(nameof(EffectiveFormat));
        OnPropertyChanged(nameof(EffectiveOutput));
        OnPropertyChanged(nameof(TargetLabel));
        OnPropertyChanged(nameof(AutomationName));
        if (notify)
        {
            _changed(this);
        }
    }

    private string Describe(InputInfo input)
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
}
