using CommunityToolkit.Mvvm.ComponentModel;

namespace Kvertis.App.ViewModels.Target;

/// <summary>
/// One line of the middle column: "foto.heic · HEIC → JPG · ≈ 1,2 MB". Stage C shows it as a plain list; the
/// drawn orbit through the hole follows with Win2D (ADR-018).
/// </summary>
public sealed partial class PathRowViewModel : ObservableObject
{
    public PathRowViewModel(TargetFileViewModel file)
    {
        File = file ?? throw new ArgumentNullException(nameof(file));
    }

    public TargetFileViewModel File { get; }

    public string FileName => File.FileName;

    public string SourceLabel => File.SourceLabel;

    public string TargetLabel => File.TargetLabel;

    public string EstimateText => File.EstimateText;

    public string AutomationName => string.Join(", ", FileName, SourceLabel, TargetLabel, EstimateText);

    /// <summary>The row mirrors the file card; the page calls this when the file changed.</summary>
    public void Refresh()
    {
        OnPropertyChanged(nameof(TargetLabel));
        OnPropertyChanged(nameof(EstimateText));
        OnPropertyChanged(nameof(AutomationName));
    }
}
