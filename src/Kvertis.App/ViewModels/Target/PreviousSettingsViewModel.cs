using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kvertis.App.Services;
using Kvertis.Queue;

namespace Kvertis.App.ViewModels.Target;

/// <summary>
/// The "Aus dem Verlauf · damals" card. It shows what the earlier conversion used and offers to take the
/// values over; when the old format does not fit the new files it says so and keeps the suggestion.
/// </summary>
public sealed partial class PreviousSettingsViewModel : ObservableObject
{
    private readonly Action _apply;

    public PreviousSettingsViewModel(ILocalizer loc, HistoryEntry entry, string formatLabel, bool formatPossible, Action apply)
    {
        ArgumentNullException.ThrowIfNull(loc);
        ArgumentNullException.ThrowIfNull(entry);
        _apply = apply ?? throw new ArgumentNullException(nameof(apply));
        Entry = entry;
        TitleText = loc.Format("Target_Previous_Title", entry.When.ToLocalTime().ToString("g", CultureInfo.CurrentCulture));
        SummaryText = formatPossible
            ? loc.Format("Target_Previous_Summary", formatLabel, entry.Settings.QualityClamped)
            : loc.Format("Target_Previous_NotPossible", formatLabel);
        CanApply = formatPossible;
    }

    public HistoryEntry Entry { get; }

    public string TitleText { get; }

    public string SummaryText { get; }

    [ObservableProperty]
    private bool canApply;

    [RelayCommand(CanExecute = nameof(CanApply))]
    private void Apply() => _apply();
}
