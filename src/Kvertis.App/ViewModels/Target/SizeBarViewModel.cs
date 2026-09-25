using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Kvertis.App.Helpers;
using Kvertis.App.Services;
using Kvertis.Engine.Tuning;

namespace Kvertis.App.ViewModels.Target;

/// <summary>One mark on the size bar ("Für E-Mail"), placed at its own size.</summary>
public sealed class SizeMarkViewModel
{
    public SizeMarkViewModel(string label, double position, double left)
    {
        Label = label;
        Position = position;
        Left = left;
    }

    public string Label { get; }

    /// <summary>Position on the slider scale 0..<see cref="TargetPlanner.BarSteps"/>.</summary>
    public double Position { get; }

    /// <summary>Offset in pixels on the caption canvas under the bar.</summary>
    public double Left { get; }
}

/// <summary>One of the 24 colour segments behind the track. In high contrast the segments are hidden.</summary>
public sealed class SizeSegmentViewModel
{
    public SizeSegmentViewModel(string brushKey)
    {
        BrushKey = brushKey;
    }

    public string BrushKey { get; }
}

/// <summary>
/// The size bar: a second view of the grade (ADR-019). Dragging it turns a position into bytes and the bytes
/// back into a grade through the table of the largest file; it never sets a target size.
/// </summary>
public sealed partial class SizeBarViewModel : ObservableObject
{
    /// <summary>Colour segments behind the track (design sketch).</summary>
    public const int SegmentCount = 24;

    /// <summary>Width of the bar in the right column; the marks and segments are placed against it.</summary>
    public const double BarWidth = 320;

    public const double SegmentWidth = BarWidth / SegmentCount;

    private readonly ILocalizer _loc;

    public SizeBarViewModel(ILocalizer loc)
    {
        _loc = loc ?? throw new ArgumentNullException(nameof(loc));
    }

    public ObservableCollection<SizeMarkViewModel> Marks { get; } = [];

    public ObservableCollection<SizeSegmentViewModel> Segments { get; } = [];

    public double Maximum => TargetPlanner.BarSteps;

    [ObservableProperty]
    private double position;

    [ObservableProperty]
    private string minText = string.Empty;

    [ObservableProperty]
    private string maxText = string.Empty;

    [ObservableProperty]
    private string valueText = string.Empty;

    [ObservableProperty]
    private bool hasScale;

    public long MinBytes { get; private set; } = 1;

    public long MaxBytes { get; private set; } = 2;

    /// <summary>Raised when the user moved the bar; the panel turns the bytes into a grade.</summary>
    public event EventHandler<long>? Dragged;

    public string AutomationName => _loc.Format("Target_SizeBar_AutomationName", ValueText);

    /// <summary>Sets the range from the tables and paints the segments from their worst severity.</summary>
    public void SetScale(long minBytes, long maxBytes, GradeSizeTable? largest, IReadOnlyList<SizeMark> marks)
    {
        ArgumentNullException.ThrowIfNull(marks);
        MinBytes = Math.Max(1, minBytes);
        MaxBytes = Math.Max(MinBytes * 2, maxBytes);
        HasScale = true;
        MinText = Formatting.Bytes(_loc, MinBytes);
        MaxText = Formatting.Bytes(_loc, MaxBytes);

        Segments.Clear();
        for (var i = 0; i < SegmentCount; i++)
        {
            var at = (i + 0.5) / SegmentCount * TargetPlanner.BarSteps;
            var bytes = TargetPlanner.BytesAt(at, MinBytes, MaxBytes);
            var worst = largest is null ? EffectSeverity.Fine : largest.WorstAt(largest.GradeForBytes(bytes));
            Segments.Add(new SizeSegmentViewModel(worst switch
            {
                EffectSeverity.Warning => "KvErrorBrush",
                EffectSeverity.Notice => "KvVideoBrush",
                _ => "KvMintBrush",
            }));
        }

        Marks.Clear();
        foreach (var mark in marks)
        {
            if (mark.Bytes < MinBytes || mark.Bytes > MaxBytes)
            {
                // A mark outside the scale would sit on the edge and lie about the size.
                continue;
            }
            var at = TargetPlanner.PositionOf(mark.Bytes, MinBytes, MaxBytes);
            Marks.Add(new SizeMarkViewModel(
                _loc.Get("Preset_" + mark.Preset.ToString()),
                at,
                at / TargetPlanner.BarSteps * BarWidth));
        }
    }

    /// <summary>Moves the handle without reporting the move back (the ring or a zone changed).</summary>
    public void ShowBytes(long bytes, bool approximate)
    {
        // While the user drags the bar the handle must stay where the pointer is. Moving it to the quantised
        // position of the grade would pull it back under the finger on every frame.
        if (!_dragging)
        {
            SetPositionSilently(TargetPlanner.PositionOf(bytes, MinBytes, MaxBytes));
        }
        ValueText = approximate
            ? _loc.Format("Target_Size_Approx", Formatting.Bytes(_loc, bytes))
            : Formatting.Bytes(_loc, bytes);
        OnPropertyChanged(nameof(AutomationName));
    }

    private bool _silent;
    private bool _dragging;

    private void SetPositionSilently(double value)
    {
        _silent = true;
        try
        {
            Position = value;
        }
        finally
        {
            _silent = false;
        }
    }

    partial void OnPositionChanged(double value)
    {
        if (_silent || !HasScale)
        {
            return;
        }
        _dragging = true;
        try
        {
            Dragged?.Invoke(this, TargetPlanner.BytesAt(value, MinBytes, MaxBytes));
        }
        finally
        {
            _dragging = false;
        }
    }

    /// <summary>Puts the handle back on the grade the panel now holds (called when the drag ended).</summary>
    public void Settle(long bytes) => SetPositionSilently(TargetPlanner.PositionOf(bytes, MinBytes, MaxBytes));

    partial void OnValueTextChanged(string value) => OnPropertyChanged(nameof(AutomationName));
}
