using CommunityToolkit.Mvvm.ComponentModel;
using Kvertis.App.Services;
using Kvertis.Engine.Tuning;

namespace Kvertis.App.ViewModels.Target;

/// <summary>
/// One zone of the tuning panel (Schärfe, Details, Bewegung, Klang). The value is the same 0..100 scale as
/// the ring; a zone the output format fixes is shown but disabled.
/// </summary>
public sealed partial class ZoneViewModel : ObservableObject
{
    private readonly ILocalizer _loc;
    private readonly Action<TuningAspect, int> _changed;
    private bool _silent;

    public ZoneViewModel(ILocalizer loc, AspectLevel level, Action<TuningAspect, int> changed)
    {
        ArgumentNullException.ThrowIfNull(loc);
        ArgumentNullException.ThrowIfNull(level);
        _loc = loc;
        _changed = changed ?? throw new ArgumentNullException(nameof(changed));
        Aspect = level.Aspect;
        NameText = loc.Get("Zone_" + level.Aspect.ToString() + "_Name");
        Apply(level);
    }

    public TuningAspect Aspect { get; }

    public string NameText { get; }

    [ObservableProperty]
    private int value;

    [ObservableProperty]
    private bool adjustable;

    [ObservableProperty]
    private string detailText = string.Empty;

    [ObservableProperty]
    private bool isExpanded;

    /// <summary>"Schärfe, 70 von 100, 1920 statt 4032 px" for the screen reader.</summary>
    public string AutomationName => _loc.Format("Target_Zone_AutomationName", NameText, Value, DetailText);

    /// <summary>Colour of the small bar in the header; never the only carrier of the information.</summary>
    public string BrushKey => Value >= 65 ? "KvMintBrush" : Value >= 45 ? "KvVideoBrush" : "KvErrorBrush";

    /// <summary>Takes over a freshly computed level without reporting the change back.</summary>
    public void Apply(AspectLevel level)
    {
        ArgumentNullException.ThrowIfNull(level);
        _silent = true;
        try
        {
            Value = level.Value;
            Adjustable = level.Adjustable;
            DetailText = Describe(level.Detail);
        }
        finally
        {
            _silent = false;
        }
        OnPropertyChanged(nameof(AutomationName));
        OnPropertyChanged(nameof(BrushKey));
    }

    partial void OnValueChanged(int value)
    {
        OnPropertyChanged(nameof(AutomationName));
        OnPropertyChanged(nameof(BrushKey));
        if (!_silent)
        {
            _changed(Aspect, value);
        }
    }

    partial void OnDetailTextChanged(string value) => OnPropertyChanged(nameof(AutomationName));

    private string Describe(AspectDetail detail)
    {
        if (detail.Lossless)
        {
            return _loc.Get("Target_Zone_Detail_Lossless");
        }
        if (detail.Kbps is { } kbps)
        {
            return _loc.Format("Target_Zone_Detail_Kbps", kbps);
        }
        if (detail.FrameRate is { } fps)
        {
            return _loc.Format("Target_Zone_Detail_FrameRate", fps);
        }
        if (detail.Pixels is { } pixels)
        {
            return detail.SourcePixels is { } source && source > pixels
                ? _loc.Format("Target_Zone_Detail_PixelsFrom", pixels, source)
                : _loc.Format("Target_Zone_Detail_Pixels", pixels);
        }
        if (detail.Quality is { } quality)
        {
            return _loc.Format("Target_Zone_Detail_Quality", quality);
        }
        return string.Empty;
    }
}
