using CommunityToolkit.Mvvm.ComponentModel;
using Kvertis.App.Helpers;
using Kvertis.App.Services;
using Kvertis.Engine.Abstractions;

namespace Kvertis.App.ViewModels.Target;

/// <summary>
/// One row of the target list "WIRD ZU" (draft <c>.zr</c>): the format name, its short description, the
/// estimated size of the whole group in that format and the "Empfohlen" mark. The list is a single-selection
/// <c>ListView</c> that reads like a radio group; <see cref="IsSelected"/> mirrors the group's choice.
/// </summary>
public sealed partial class TargetFormatViewModel : ObservableObject
{
    private readonly ILocalizer _loc;

    public TargetFormatViewModel(ILocalizer loc, FormatOption option, string hint)
    {
        _loc = loc ?? throw new ArgumentNullException(nameof(loc));
        Option = option ?? throw new ArgumentNullException(nameof(option));
        Hint = hint ?? string.Empty;
        ItemTypeText = loc.Get("Target_Format_ItemType");
    }

    public FormatOption Option { get; }

    public FormatId Id => Option.Id;

    /// <summary>Format name such as "JPG" (not translated).</summary>
    public string Label => Option.Label;

    /// <summary>Short explanation from <c>Format_&lt;id&gt;_Hint</c>, for example "Fotos, klein".</summary>
    public string Hint { get; }

    public bool IsRecommended => Option.IsSuggested;

    /// <summary>"Optionsfeld": the row announces itself like a radio button.</summary>
    public string ItemTypeText { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AutomationName))]
    private bool isSelected;

    /// <summary>"≈ 6,1 MB", empty until the estimate for this format is in.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AutomationName))]
    private string sizeText = string.Empty;

    public long EstimatedBytes { get; private set; }

    public string AutomationName => _loc.Format(
        IsRecommended ? "Target_Format_AutomationName_Recommended" : "Target_Format_AutomationName",
        Label,
        Hint,
        SizeText);

    /// <summary>Shows the estimated size of the group in this format; 0 clears the text.</summary>
    public void SetEstimate(long bytes)
    {
        EstimatedBytes = bytes;
        SizeText = bytes > 0 ? _loc.Format("Target_Size_Approx", Formatting.Bytes(_loc, bytes)) : string.Empty;
    }
}
