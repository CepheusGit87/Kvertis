using Kvertis.App.Animations;
using Kvertis.App.Helpers;
using Kvertis.App.ViewModels.Drop;
using Kvertis.Engine.Abstractions;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Kvertis.App.Views;

/// <summary>
/// One tray of step 1. The control owns no logic: it shows what <see cref="TrayViewModel"/> counted and hands
/// a click on its head to the page, which drives the zoom.
/// </summary>
public sealed partial class TrayControl : UserControl
{
    public static readonly DependencyProperty TrayProperty = DependencyProperty.Register(
        nameof(Tray), typeof(TrayViewModel), typeof(TrayControl), new PropertyMetadata(null, OnTrayChanged));

    private TrayViewModel? _attached;
    private int _lastCount;
    private bool _hover;

    public TrayControl()
    {
        InitializeComponent();
        // The trays live in a ScrollViewer and can be unloaded and loaded again; the subscription has to
        // follow, otherwise the tray stops reacting to its view model.
        Loaded += (_, _) => Attach(Tray);
        Unloaded += (_, _) => Attach(null);
        ActualThemeChanged += (_, _) => { FillExampleChips(); ApplyFrame(); };
    }

    /// <summary>Raised when the head was activated (mouse, Enter or space).</summary>
    public event EventHandler<MediaKind>? HeadInvoked;

    /// <summary>Raised by Enter on one of the rows; the page carries on to step 2.</summary>
    public event EventHandler? AcceptRequested;

    public TrayViewModel? Tray
    {
        get => (TrayViewModel?)GetValue(TrayProperty);
        set => SetValue(TrayProperty, value);
    }

    /// <summary>The head of the tray: what the transition overlay measures and stands in for (ADR-023).</summary>
    public FrameworkElement Head => HeadButton;

    /// <summary>Hides or shows the head while the overlay draws its ghost. The head keeps its place and its focus.</summary>
    public void SetHeadVisible(bool visible) => HeadButton.Opacity = visible ? 1 : 0;

    private static void OnTrayChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) =>
        ((TrayControl)sender).Attach(args.NewValue as TrayViewModel);

    private void Attach(TrayViewModel? tray)
    {
        if (_attached is { } old)
        {
            old.PropertyChanged -= OnTrayPropertyChanged;
        }

        _attached = tray;
        _lastCount = tray?.Count ?? 0;
        if (tray is not null)
        {
            tray.PropertyChanged += OnTrayPropertyChanged;
        }

        FillExampleChips();
        ApplyZoomState();
    }

    private void OnTrayPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(TrayViewModel.IsBar) or nameof(TrayViewModel.IsZoomed))
        {
            ApplyZoomState();
            return;
        }

        if (e.PropertyName != nameof(TrayViewModel.Count) || _attached is not { } tray)
        {
            return;
        }

        ApplyFrame();

        // A new file made the count grow: the number hops once (0.45 s spring, plain fade without animations).
        var grew = tray.Count > _lastCount;
        _lastCount = tray.Count;
        if (grew)
        {
            CardAnimations.CountHop(CountBig);
        }
    }

    private void ApplyZoomState()
    {
        VisualStateManager.GoToState(this, Tray?.IsBar == true ? "Zoomed" : "Overview", useTransitions: true);
        ApplyFrame();
    }

    private void OnTrayPointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        _hover = true;
        ApplyFrame();
    }

    private void OnTrayPointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        _hover = false;
        ApplyFrame();
    }

    /// <summary>
    /// The frame of the draft's .fach: top edge kind 70 % (empty 30 %); hover = border kind 55 %, top edge full,
    /// 1 px ring kind 25 %; zoomed to this kind (.fach.an) = border and top edge full, background kind 12 %.
    /// The kind variants are looked up here because they depend on the tray, not on the theme alone.
    /// </summary>
    private void ApplyFrame()
    {
        var key = Tray?.BrushKey;
        var zoomed = Tray?.IsZoomed == true;
        var empty = Tray?.IsEmpty != false;
        TopEdge.Opacity = zoomed || _hover ? 1.0 : empty ? 0.3 : 0.7;
        HoverRing.Visibility = _hover && !zoomed ? Visibility.Visible : Visibility.Collapsed;
        Frame.BorderBrush = zoomed ? Ui.Brush(key) : _hover ? Ui.KindVariant(key, "Frame60") : Ui.Brush("KvLineBrush");
        Frame.Background = zoomed ? Ui.KindVariant(key, "Tint12") : Ui.Brush("KvBg86Brush");
    }

    /// <summary>The example formats of the empty tray as chips in the kind colour (.fach-leer .chips .fc, 9.5 px).</summary>
    private void FillExampleChips()
    {
        ExampleChips.Children.Clear();
        if (Tray is not { } tray)
        {
            return;
        }

        foreach (var format in tray.ExampleFormats)
        {
            ExampleChips.Children.Add(FormatChip.Create(format, tray.BrushKey, small: true));
        }
    }

    private void OnRowAccepted(object? sender, EventArgs e) => AcceptRequested?.Invoke(this, EventArgs.Empty);

    private void OnHeadClick(object sender, RoutedEventArgs e)
    {
        // Read the property, not the field: the tray lives in a ScrollViewer and may be unloaded and loaded
        // again, which detaches the field while the dependency property keeps its value.
        if (Tray is { } tray)
        {
            HeadInvoked?.Invoke(this, tray.Kind);
        }
    }
}
