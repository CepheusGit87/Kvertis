using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kvertis.App.Helpers;
using Kvertis.App.Services;
using Kvertis.Engine.Abstractions;
using Kvertis.Engine.Formats;
using Kvertis.Queue;

namespace Kvertis.App.ViewModels.Convert;

/// <summary>What one row of step 3 asks the page to do for it.</summary>
public interface IConvertRowHost
{
    Task OpenAsync(ConvertRowViewModel row);

    Task ShowInFolderAsync(ConvertRowViewModel row);

    /// <summary>Sets the file's own target; <paramref name="kind"/> null means "like all the others".</summary>
    Task SetOwnLocationAsync(ConvertRowViewModel row, OutputLocationKind? kind);
}

/// <summary>
/// One line of the list "Dateien in diesem Auftrag" (docs/entwuerfe/schritt-3-umwandeln.md). It holds the
/// preview of the target path before the start and the queue's view of the job afterwards.
/// </summary>
public sealed partial class ConvertRowViewModel : ObservableObject
{
    private readonly IConvertRowHost _host;
    private readonly ILocalizer _loc;
    private readonly ErrorMessageMapper _errors;
    private readonly long _inputBytes;
    private readonly long _estimatedBytes;

    public ConvertRowViewModel(
        PlannedConversion item,
        IConvertRowHost host,
        ILocalizer loc,
        FormatRegistry registry,
        ErrorMessageMapper errors,
        long estimatedBytes)
    {
        Item = item ?? throw new ArgumentNullException(nameof(item));
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _loc = loc ?? throw new ArgumentNullException(nameof(loc));
        _errors = errors ?? throw new ArgumentNullException(nameof(errors));
        ArgumentNullException.ThrowIfNull(registry);

        InputPath = item.Input.Path;
        FileName = Path.GetFileName(item.Input.Path);
        ColorBrushKey = TargetPlanner.ColorKey(item.Input.Kind) + "Brush";
        SourceLabel = TargetPlanner.Label(registry, item.Input.Format);
        TargetLabel = TargetPlanner.Label(registry, item.Settings.Output);
        SourceCode = ShortCode(registry, item.Input.Format);
        TargetCode = ShortCode(registry, item.Settings.Output);
        FormatsText = loc.Format("Convert_Row_Formats", SourceLabel, TargetLabel);
        _inputBytes = item.Input.SizeBytes;
        _estimatedBytes = estimatedBytes;
        InputDirectory = Path.GetDirectoryName(item.Input.Path) ?? string.Empty;
        SubFolderDirectory = Path.Combine(InputDirectory, OutputLocation.DefaultSubFolderName);
        SizesText = loc.Format(
            "Convert_Row_Sizes", Formatting.Bytes(loc, _inputBytes), Formatting.Bytes(loc, estimatedBytes));
        StateText = loc.Get("Convert_Row_State_Waiting");
        StatusText = loc.Get("Convert_Row_Status_Ready");
    }

    /// <summary>Longest directory part shown in the list before it is shortened from the left (draft: kurzPfad 42).</summary>
    public const int MaxDirectoryChars = 42;

    /// <summary>The short code of a format for the chips (.w5-chip): the main file extension, "JPG", "MD", "OGG".</summary>
    public static string ShortCode(FormatRegistry registry, FormatId id)
    {
        ArgumentNullException.ThrowIfNull(registry);
        var extension = registry.Get(id)?.PrimaryExtension;
        return (string.IsNullOrEmpty(extension) ? id.Id : extension).ToUpperInvariant();
    }

    /// <summary>"C:\Users\…\Bilder\Urlaub\" for the list: the folder with a trailing separator, shortened from the left.</summary>
    public static string ShortDirectory(string? directory, int maxChars = MaxDirectoryChars)
    {
        if (string.IsNullOrEmpty(directory))
        {
            return string.Empty;
        }
        var withSeparator = directory.EndsWith(Path.DirectorySeparatorChar) ? directory : directory + Path.DirectorySeparatorChar;
        if (withSeparator.Length <= maxChars || maxChars < 8)
        {
            return withSeparator;
        }
        // Keep the drive and the end of the path: "C:\…\Bilder\Urlaub 2026\".
        var root = Path.GetPathRoot(withSeparator) ?? string.Empty;
        var tail = withSeparator[^(maxChars - root.Length - 1)..];
        var cut = tail.IndexOf(Path.DirectorySeparatorChar);
        if (cut > 0 && cut < tail.Length - 1)
        {
            tail = tail[cut..];
        }
        return root + "\u2026" + (tail.StartsWith(Path.DirectorySeparatorChar) ? tail : Path.DirectorySeparatorChar + tail);
    }

    public PlannedConversion Item { get; }

    public string InputPath { get; }

    public string FileName { get; }

    /// <summary>Theme brush of the file's kind (ADR-017); high contrast falls back to the text colour.</summary>
    public string ColorBrushKey { get; }

    public string SourceLabel { get; }

    public string TargetLabel { get; }

    public string FormatsText { get; }

    /// <summary>Chip text of the source format (.w5-chip.alt).</summary>
    public string SourceCode { get; }

    /// <summary>Chip text of the target format (.w5-chip.neu).</summary>
    public string TargetCode { get; }

    /// <summary>Size of the input file in bytes.</summary>
    public long InputBytes => _inputBytes;

    /// <summary>Estimated size of the result before the round.</summary>
    public long EstimatedBytes => _estimatedBytes;

    /// <summary>Size of the result once the file is done, otherwise null.</summary>
    public long? OutputBytes { get; private set; }

    /// <summary>The folder of the original ("Neben dem Original" in the row menu).</summary>
    public string InputDirectory { get; }

    /// <summary>The sub folder next to the original ("Unterordner" in the row menu).</summary>
    public string SubFolderDirectory { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AutomationName))]
    private string sizesText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AutomationName), nameof(TargetDirectoryShort))]
    private string targetDirectoryText = string.Empty;

    /// <summary>Short visible status (.w5-st): "bereit", "42 %", "✓ fertig", "✕ Fehler". The screen reader gets <see cref="StateText"/>.</summary>
    [ObservableProperty]
    private string statusText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AutomationName))]
    private string targetFileName = string.Empty;

    [ObservableProperty]
    private string fullPath = string.Empty;

    [ObservableProperty]
    private string targetMarkText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowPath), nameof(TargetDirectoryShort))]
    private bool needsFolder;

    [ObservableProperty]
    private bool isOwnLocation;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AutomationName))]
    private string stateText = string.Empty;

    [ObservableProperty]
    private double progressValue;

    [ObservableProperty]
    private string percentText = string.Empty;

    [ObservableProperty]
    private string remainingText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string errorTitle = string.Empty;

    [ObservableProperty]
    private string errorBody = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWaiting), nameof(IsRunning), nameof(IsDone), nameof(IsFailed),
        nameof(IsFinished), nameof(CanChangeTarget), nameof(ShowProgress), nameof(ShowPath), nameof(ShowChange))]
    private JobState? state;

    /// <summary>The folder part of the target path as the list shows it.</summary>
    public string TargetDirectoryShort => NeedsFolder ? TargetDirectoryText : ShortDirectory(TargetDirectoryText);

    /// <summary>"Ändern" stands in the action column until the file is done or failed.</summary>
    public bool ShowChange => State is not (JobState.Completed or JobState.Failed);

    public bool IsOwnSame => IsOwnLocation && IsSame;

    public bool IsOwnSub => IsOwnLocation && IsSub;

    public bool IsOwnCustom => IsOwnLocation && IsCustom;

    public bool HasError => !string.IsNullOrEmpty(ErrorTitle);

    public bool IsWaiting => State is null or JobState.Queued or JobState.Paused;

    public bool IsRunning => State == JobState.Running;

    public bool IsDone => State == JobState.Completed;

    public bool IsFailed => State == JobState.Failed;

    public bool IsFinished => State is JobState.Completed or JobState.Failed or JobState.Cancelled;

    public bool ShowProgress => State is JobState.Running or JobState.Paused;

    /// <summary>The path is shown as soon as it is known; a missing folder shows the hint instead.</summary>
    public bool ShowPath => !NeedsFolder;

    /// <summary>"Ändern" is only offered while the file has not started.</summary>
    public bool CanChangeTarget => State is null or JobState.Queued or JobState.Paused;

    public bool UsesShared => !IsOwnLocation;

    public bool IsSame { get; private set; }

    public bool IsSub { get; private set; }

    public bool IsCustom { get; private set; }

    /// <summary>"foto.heic, HEIC nach JPG, wird gespeichert als …, wartend".</summary>
    public string AutomationName => _loc.Format(
        "Convert_Row_AutomationName",
        FileName,
        FormatsText,
        string.IsNullOrEmpty(TargetFileName) ? _loc.Get("Convert_Row_NeedsFolder") : FullPath,
        StateText);

    /// <summary>Takes over the computed target path (before the start and after every location change).</summary>
    public void ApplyPreview(TargetPathPreview preview)
    {
        ArgumentNullException.ThrowIfNull(preview);
        NeedsFolder = preview.NeedsFolder;
        IsOwnLocation = preview.IsOwnLocation;
        TargetDirectoryText = preview.Directory ?? _loc.Get("Convert_Row_NeedsFolder");
        TargetFileName = preview.FileName ?? string.Empty;
        FullPath = preview.FullPath ?? string.Empty;
        TargetMarkText = _loc.Get(preview.IsOwnLocation ? "Convert_Row_OwnTarget" : "Convert_Row_SharedTarget");
        IsSame = preview.Location is OutputLocation.SameFolderLocation;
        IsSub = preview.Location is OutputLocation.SubFolderLocation;
        IsCustom = preview.Location is OutputLocation.CustomLocation;
        OnPropertyChanged(nameof(IsSame));
        OnPropertyChanged(nameof(IsSub));
        OnPropertyChanged(nameof(IsCustom));
        OnPropertyChanged(nameof(UsesShared));
        OnPropertyChanged(nameof(IsOwnSame));
        OnPropertyChanged(nameof(IsOwnSub));
        OnPropertyChanged(nameof(IsOwnCustom));
        OnPropertyChanged(nameof(TargetDirectoryShort));
        OnPropertyChanged(nameof(AutomationName));
    }

    /// <summary>Copies the queue's view of the job. Called on the UI thread.</summary>
    public void ApplyJob(ConversionJob job)
    {
        ArgumentNullException.ThrowIfNull(job);
        var progress = job.Progress;
        var fraction = Math.Clamp(progress.Fraction, 0, 1);
        ProgressValue = fraction * 100;
        PercentText = _loc.Format("Convert_Row_Percent", (int)Math.Round(fraction * 100));
        RemainingText = job.State == JobState.Running && progress.Remaining is { } remaining
            ? Formatting.Duration(_loc, remaining)
            : string.Empty;

        if (job.OutputPath is { Length: > 0 } path)
        {
            // The queue resolved the final path; it wins over the preview.
            FullPath = path;
            TargetFileName = Path.GetFileName(path);
            TargetDirectoryText = Path.GetDirectoryName(path) ?? TargetDirectoryText;
            NeedsFolder = false;
        }

        switch (job.State)
        {
            case JobState.Completed when job.Result is { } result:
                OutputBytes = result.OutputBytes;
                SizesText = _loc.Format(
                    "Convert_Row_SizesFinal",
                    Formatting.Bytes(_loc, result.InputBytes),
                    Formatting.Bytes(_loc, result.OutputBytes));
                ErrorTitle = string.Empty;
                ErrorBody = string.Empty;
                break;
            case JobState.Failed:
                var message = _errors.Map(job.Error);
                ErrorTitle = message.Title;
                ErrorBody = message.Body;
                break;
            default:
                ErrorTitle = string.Empty;
                ErrorBody = string.Empty;
                break;
        }

        State = job.State;
        StateText = _loc.Get(job.State switch
        {
            JobState.Queued => "Convert_Row_State_Waiting",
            JobState.Running => "Convert_Row_State_Running",
            JobState.Paused => "Convert_Row_State_Paused",
            JobState.Completed => "Convert_Row_State_Done",
            JobState.Failed => "Convert_Row_State_Failed",
            _ => "Convert_Row_State_Cancelled",
        });
        StatusText = job.State == JobState.Running
            ? PercentText
            : _loc.Get(job.State switch
            {
                JobState.Queued => "Convert_Row_Status_Waiting",
                JobState.Paused => "Convert_Row_Status_Paused",
                JobState.Completed => "Convert_Row_Status_Done",
                JobState.Failed => "Convert_Row_Status_Failed",
                _ => "Convert_Row_Status_Cancelled",
            });
    }

    /// <summary>Back to "not started yet" (new round).</summary>
    public void ResetToWaiting()
    {
        State = null;
        OutputBytes = null;
        StateText = _loc.Get("Convert_Row_State_Waiting");
        StatusText = _loc.Get("Convert_Row_Status_Ready");
        ProgressValue = 0;
        PercentText = string.Empty;
        RemainingText = string.Empty;
        ErrorTitle = string.Empty;
        ErrorBody = string.Empty;
        SizesText = _loc.Format(
            "Convert_Row_Sizes", Formatting.Bytes(_loc, _inputBytes), Formatting.Bytes(_loc, _estimatedBytes));
    }

    [RelayCommand]
    private Task Open() => _host.OpenAsync(this);

    [RelayCommand]
    private Task ShowInFolder() => _host.ShowInFolderAsync(this);

    [RelayCommand]
    private Task UseShared() => _host.SetOwnLocationAsync(this, null);

    [RelayCommand]
    private Task UseSameFolder() => _host.SetOwnLocationAsync(this, OutputLocationKind.SameFolder);

    [RelayCommand]
    private Task UseSubFolder() => _host.SetOwnLocationAsync(this, OutputLocationKind.SubFolder);

    [RelayCommand]
    private Task UseCustomFolder() => _host.SetOwnLocationAsync(this, OutputLocationKind.Custom);
}
