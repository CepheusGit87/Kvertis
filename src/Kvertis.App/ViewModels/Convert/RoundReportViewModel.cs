using CommunityToolkit.Mvvm.Input;
using Kvertis.App.Helpers;
using Kvertis.App.Services;

namespace Kvertis.App.ViewModels.Convert;

/// <summary>
/// The closing report in the middle of step 3 (docs/entwuerfe/schritt-3-umwandeln.md, "Abschluss").
/// Static by design: the animated version follows with the drawing layer (ADR-018).
/// </summary>
public sealed partial class RoundReportViewModel
{
    private readonly Func<Task> _openFolder;

    public RoundReportViewModel(
        RoundReport report,
        ILocalizer loc,
        ErrorMessageMapper errors,
        Func<Task> openFolder,
        bool canOpenFolder)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(loc);
        ArgumentNullException.ThrowIfNull(errors);
        _openFolder = openFolder ?? throw new ArgumentNullException(nameof(openFolder));

        IsPartial = report.Completed < report.Total;
        TitleText = IsPartial
            ? loc.Format("Convert_Report_Partial", report.Completed, report.Total)
            : loc.Format("Convert_Report_AllDone", report.Total);
        Glyph = IsPartial ? "" : "";
        GlyphBrushKey = IsPartial ? "KvVideoBrush" : "KvMintBrush";
        SummaryText = loc.Format(
            "Convert_Report_Summary",
            Formatting.Bytes(loc, report.BytesIn),
            Formatting.Bytes(loc, report.BytesOut),
            Formatting.Change(loc, report.BytesIn, report.BytesOut),
            Formatting.Duration(loc, report.Elapsed));
        Failures = report.Failures
            .Select(f => loc.Format(
                "Convert_Report_Failure",
                Path.GetFileName(f.Job.Input.Path),
                errors.Map(f.Error).Title))
            .ToList();
        CanOpenFolder = canOpenFolder;
    }

    public string TitleText { get; }

    public string SummaryText { get; }

    public string Glyph { get; }

    public string GlyphBrushKey { get; }

    public bool IsPartial { get; }

    public IReadOnlyList<string> Failures { get; }

    public bool HasFailures => Failures.Count > 0;

    public bool CanOpenFolder { get; }

    /// <summary>What the screen reader says once, when the round is over.</summary>
    public string AnnouncementText => TitleText + " " + SummaryText;

    [RelayCommand]
    private Task OpenFolder() => _openFolder();
}
