using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kvertis.App.Services;

namespace Kvertis.App.ViewModels.Drop;

/// <summary>
/// The card "Nicht umwandelbar" in the top right corner of the galaxy: one row per file that cannot be
/// converted at all, with the reason from the error mapper. At most three rows are shown, the rest hide
/// behind "und n weitere".
/// </summary>
public sealed partial class RejectedListViewModel : ObservableObject
{
    /// <summary>How many rows are visible before the list has to be unfolded.</summary>
    public const int CollapsedLimit = 3;

    private readonly ILocalizer _loc;

    public RejectedListViewModel(ILocalizer loc)
    {
        _loc = loc ?? throw new ArgumentNullException(nameof(loc));
    }

    /// <summary>Every rejected file. Runtime type is <see cref="JobItemViewModel"/>.</summary>
    public ObservableCollection<ITrayFile> Items { get; } = [];

    /// <summary>The rows the card actually shows.</summary>
    public ObservableCollection<ITrayFile> Visible { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMore), nameof(MoreText))]
    private int hiddenCount;

    [ObservableProperty]
    private bool hasAny;

    [ObservableProperty]
    private bool isExpanded;

    public bool HasMore => HiddenCount > 0;

    /// <summary>"und 4 weitere".</summary>
    public string MoreText => _loc.Format("Galaxy_Rejected_More_Text", HiddenCount);

    /// <summary>Rebuilds the list from all staged files.</summary>
    public void Sync(IReadOnlyList<ITrayFile> all)
    {
        ArgumentNullException.ThrowIfNull(all);
        var rejected = all.Where(f => f.IsRejected).ToList();

        Items.Clear();
        foreach (var file in rejected)
        {
            Items.Add(file);
        }

        HasAny = rejected.Count > 0;
        if (!HasAny)
        {
            IsExpanded = false;
        }

        UpdateVisible();
    }

    partial void OnIsExpandedChanged(bool value) => UpdateVisible();

    private void UpdateVisible()
    {
        var limit = IsExpanded ? Items.Count : Math.Min(CollapsedLimit, Items.Count);
        Visible.Clear();
        for (var i = 0; i < limit; i++)
        {
            Visible.Add(Items[i]);
        }

        HiddenCount = Items.Count - limit;
    }

    [RelayCommand]
    private void ToggleExpand() => IsExpanded = !IsExpanded;
}
