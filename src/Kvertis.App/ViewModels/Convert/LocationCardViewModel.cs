using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kvertis.App.Services;

namespace Kvertis.App.ViewModels.Convert;

/// <summary>
/// The "Speicherort" card on the right of step 3: where everything goes unless a file has its own target.
/// It only reports the wish; writing it into the session and the settings is the page's job.
/// </summary>
public sealed partial class LocationCardViewModel : ObservableObject
{
    private readonly ILocalizer _loc;
    private readonly Func<OutputLocationKind, Task> _apply;

    public LocationCardViewModel(ILocalizer loc, Func<OutputLocationKind, Task> apply)
    {
        _loc = loc ?? throw new ArgumentNullException(nameof(loc));
        _apply = apply ?? throw new ArgumentNullException(nameof(apply));
        Update(OutputLocationKind.SameFolder, null);
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AutomationName))]
    private string titleText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AutomationName))]
    private string detailText = string.Empty;

    [ObservableProperty]
    private bool isSame = true;

    [ObservableProperty]
    private bool isSub;

    [ObservableProperty]
    private bool isCustom;

    /// <summary>False once no job of the round is waiting any more; the card then only reports.</summary>
    [ObservableProperty]
    private bool isEnabled = true;

    public OutputLocationKind Kind { get; private set; } = OutputLocationKind.SameFolder;

    public string? CustomPath { get; private set; }

    public string AutomationName => _loc.Format("Convert_Location_AutomationName", TitleText, DetailText);

    /// <summary>Shows what the session currently holds.</summary>
    public void Update(OutputLocationKind kind, string? customPath)
    {
        Kind = kind;
        CustomPath = customPath;
        IsSame = kind == OutputLocationKind.SameFolder;
        IsSub = kind == OutputLocationKind.SubFolder;
        IsCustom = kind == OutputLocationKind.Custom;
        TitleText = kind switch
        {
            OutputLocationKind.SubFolder => _loc.Get("Convert_Location_Sub_Title"),
            OutputLocationKind.Custom when !string.IsNullOrEmpty(customPath) => Path.GetFileName(
                customPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
            OutputLocationKind.Custom => _loc.Get("Convert_Location_Custom_Title"),
            _ => _loc.Get("Convert_Location_Same_Title"),
        };
        if (string.IsNullOrEmpty(TitleText))
        {
            TitleText = _loc.Get("Convert_Location_Custom_Title");
        }
        DetailText = kind switch
        {
            OutputLocationKind.SubFolder => _loc.Get("Convert_Location_Sub_Detail"),
            OutputLocationKind.Custom => customPath ?? _loc.Get("Convert_Location_Custom_Detail"),
            _ => _loc.Get("Convert_Location_Same_Detail"),
        };
    }

    [RelayCommand]
    private Task UseSameFolder() => _apply(OutputLocationKind.SameFolder);

    [RelayCommand]
    private Task UseSubFolder() => _apply(OutputLocationKind.SubFolder);

    [RelayCommand]
    private Task UseCustomFolder() => _apply(OutputLocationKind.Custom);
}
