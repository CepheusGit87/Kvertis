using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kvertis.App.Services;
using Kvertis.Engine.Abstractions;
using Kvertis.Engine.Naming;
using Microsoft.UI.Xaml.Media;

namespace Kvertis.App.ViewModels;

/// <summary>
/// Where a card of step 1 is in its life. The conversion states live in step 3 now (ADR-021); a card never
/// belongs to the queue any more.
/// </summary>
public enum JobItemState
{
    /// <summary>Format detection is running.</summary>
    Detecting = 0,
    /// <summary>Detected; the file goes on to step 2.</summary>
    Ready,
    /// <summary>Cannot be converted (unsupported, corrupt, protected).</summary>
    Rejected,
}

/// <summary>Actions a card delegates to the page view model.</summary>
public interface IJobItemHost
{
    ILocalizer Localizer { get; }

    void Remove(JobItemViewModel item);
}

/// <summary>
/// One file of step 1 (docs/06-design.md): what was dropped in and what it is. Since the trays replaced the
/// job cards (ADR-022) this is the model of one tray row and of one planet in the galaxy.
/// </summary>
public sealed partial class JobItemViewModel : ObservableObject, Drop.ITrayFile
{
    private const string GlyphImage = "";
    private const string GlyphAudio = "";
    private const string GlyphVideo = "";
    private const string GlyphDocument = "";
    private const string GlyphModel3D = "";
    private const string GlyphUnknown = "";

    private readonly IJobItemHost _host;
    private readonly ILocalizer _loc;

    public JobItemViewModel(IJobItemHost host, string path, bool isFromClipboard, string namePattern, bool stripMetadata)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _loc = host.Localizer;
        FilePath = path;
        FileName = Path.GetFileName(path);
        IsFromClipboard = isFromClipboard;
        NamePattern = string.IsNullOrWhiteSpace(namePattern) ? OutputNamePattern.Default : namePattern;
        StripMetadata = stripMetadata;
        StatusText = _loc.Get("Card_State_Detecting");
    }

    public Guid Id { get; } = Guid.NewGuid();

    public string FilePath { get; }

    public string FileName { get; }

    /// <summary>The image came from the clipboard and lives in the app's cache; step 3 asks for a folder then.</summary>
    public bool IsFromClipboard { get; }

    public InputInfo? Input { get; private set; }

    public ObservableCollection<FormatOption> FormatOptions { get; } = [];

    public MediaKind Kind => Input?.Kind ?? MediaKind.Unknown;

    public bool IsVideo => Kind == MediaKind.Video;

    public string NamePattern { get; }

    public bool StripMetadata { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDetecting), nameof(IsReady), nameof(IsRejected), nameof(CanRemove),
        nameof(AutomationName))]
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
    private FormatOption? selectedFormat;

    [ObservableProperty]
    private string warningText = string.Empty;

    /// <summary>"Endung .jpg, Inhalt ist PNG" — replaces the size in the tray row when the extension lies.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMismatch))]
    private string mismatchText = string.Empty;

    [ObservableProperty]
    private string errorTitle = string.Empty;

    [ObservableProperty]
    private string errorBody = string.Empty;

    [ObservableProperty]
    private string statusText = string.Empty;

    public bool IsDetecting => State == JobItemState.Detecting;

    public bool IsReady => State == JobItemState.Ready;

    public bool IsRejected => State == JobItemState.Rejected;

    public bool CanRemove => State != JobItemState.Detecting;

    public bool HasThumbnail => ThumbnailLoaded;

    public bool HasMismatch => MismatchText.Length > 0;

    /// <summary>Screen reader name of the whole card: "photo.heic, ready".</summary>
    public string AutomationName => _loc.Format("Card_AutomationName", FileName, StatusText);

    /// <summary>List items report this to screen readers when no explicit name is set.</summary>
    public override string ToString() => AutomationName;

    /// <summary>Called once detection succeeded.</summary>
    public void Initialize(InputInfo input, string inputFormatLabel, IEnumerable<FormatOption> options, string sizeText, string detailsText, string warningText)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(options);
        Input = input;
        FormatOptions.Clear();
        foreach (var option in options)
        {
            FormatOptions.Add(option);
        }
        SelectedFormat = FormatOptions.FirstOrDefault(o => o.IsSuggested) ?? FormatOptions.FirstOrDefault();
        InputFormatLabel = inputFormatLabel;
        KindGlyph = input.Kind switch
        {
            MediaKind.Image => GlyphImage,
            MediaKind.Audio => GlyphAudio,
            MediaKind.Video => GlyphVideo,
            MediaKind.Document => GlyphDocument,
            MediaKind.Model3D => GlyphModel3D,
            _ => GlyphUnknown,
        };
        SizeText = sizeText;
        DetailsText = detailsText;
        WarningText = warningText;
        MismatchText = input.Warnings.Contains(InputWarning.ExtensionMismatch)
            ? _loc.Format("Tray_ExtensionMismatch_Text", Path.GetExtension(FilePath).TrimStart('.'), inputFormatLabel)
            : string.Empty;
        SetState(JobItemState.Ready);
        OnPropertyChanged(nameof(Kind));
        OnPropertyChanged(nameof(IsVideo));
    }

    public void SetRejected(ErrorMessage error)
    {
        ArgumentNullException.ThrowIfNull(error);
        ErrorTitle = error.Title;
        ErrorBody = error.Body;
        SetState(JobItemState.Rejected);
    }

    /// <summary>
    /// Settings for the preview dialog. The real settings of a conversion come from step 2
    /// (<c>TargetPlan</c>); this is only the starting point a preview is built from.
    /// </summary>
    public ConversionSettings BuildSettings()
    {
        var output = SelectedFormat?.Id ?? throw new InvalidOperationException("No output format selected.");
        var metadata = StripMetadata ? MetadataPolicy.Strip : MetadataPolicy.Keep;
        return new ConversionSettings(output, Metadata: metadata);
    }

    [RelayCommand]
    private void Remove() => _host.Remove(this);

    partial void OnStatusTextChanged(string value) => OnPropertyChanged(nameof(AutomationName));

    private void SetState(JobItemState value)
    {
        State = value;
        StatusText = _loc.Get("Card_State_" + value.ToString());
    }
}
