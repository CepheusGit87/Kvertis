using Kvertis.App.Animations;
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

    public TrayControl()
    {
        InitializeComponent();
        // The trays live in a ScrollViewer and can be unloaded and loaded again; the subscription has to
        // follow, otherwise the tray stops reacting to its view model.
        Loaded += (_, _) => Attach(Tray);
        Unloaded += (_, _) => Attach(null);
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

        // A new file made the count grow: the number hops once (0.45 s spring, plain fade without animations).
        var grew = tray.Count > _lastCount;
        _lastCount = tray.Count;
        if (grew)
        {
            CardAnimations.CountHop(CountBig);
        }
    }

    private void ApplyZoomState() =>
        VisualStateManager.GoToState(this, Tray?.IsBar == true ? "Zoomed" : "Overview", useTransitions: true);

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
