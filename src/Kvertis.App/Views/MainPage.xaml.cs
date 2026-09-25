using Kvertis.App.Services;
using Kvertis.App.ViewModels;
using Kvertis.Engine.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using Windows.ApplicationModel.DataTransfer;

namespace Kvertis.App.Views;

/// <summary>
/// Step 1 "Hineinwerfen". Code-behind only handles drag-and-drop, the keyboard and the lifetime of the
/// drawing surface; everything else is in the view model (ADR-022).
/// </summary>
public sealed partial class MainPage : Page, ITransitionAnchors
{
    /// <summary>Below this window width the trays get fixed columns and scroll sideways.</summary>
    private const double NarrowWidth = 900;

    /// <summary>Smallest height the galaxy keeps in a normal window.</summary>
    private const double GalaxyMinHeight = 240;

    /// <summary>Tallest the galaxy grows in the overview; in the zoom it takes the whole page.</summary>
    private const double GalaxyMaxHeight = 520;

    private readonly ILocalizer _loc;
    private readonly ITransitionService _transitions;

    private bool _narrow;
    private bool _widthApplied;
    private bool _windowVisible = true;

    public MainPage()
    {
        ViewModel = App.Services.GetRequiredService<MainViewModel>();
        History = App.Services.GetRequiredService<HistoryViewModel>();
        _loc = App.Services.GetRequiredService<ILocalizer>();
        _transitions = App.Services.GetRequiredService<ITransitionService>();
        InitializeComponent();
        Galaxy.ViewModel = ViewModel;
        // Set in code: x:Bind cannot index an IReadOnlyList in a path.
        Tray0.Tray = ViewModel.Trays[0];
        Tray1.Tray = ViewModel.Trays[1];
        Tray2.Tray = ViewModel.Trays[2];
        Tray3.Tray = ViewModel.Trays[3];
        Tray4.Tray = ViewModel.Trays[4];
        Paths.Paths = ViewModel.Paths;
        Paths.Scene = ViewModel.Scene;
        Paths.AnchorRoot = Galaxy;
        Galaxy.SurfaceVisibilityChanged += OnSurfaceVisibilityChanged;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        ApplySurfaceVisibility();
        // Only one drawing loop runs at a time (ADR-023): the galaxy stands still while the overlay flies.
        _transitions.Changed += (_, _) => UpdatePaused();

        // A minimised or hidden window must not keep the drawing thread busy (ADR-018).
        if (App.Services.GetRequiredService<IWindowContext>().Window is { } window)
        {
            window.VisibilityChanged += OnWindowVisibilityChanged;
        }
    }

    private void OnWindowVisibilityChanged(object sender, WindowVisibilityChangedEventArgs args)
    {
        _windowVisible = args.Visible;
        UpdatePaused();
    }

    public MainViewModel ViewModel { get; }

    public HistoryViewModel History { get; }

    // ---- Page lifetime ------------------------------------------------------------------------------

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        // The page is cached, so the scene in the view model kept its planets; only the surface is new.
        Galaxy.Resume();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        Galaxy.Suspend();
    }

    /// <summary>The history pane covers the galaxy: pausing is enough, the surface may stay.</summary>
    private void OnHistoryPaneChanged(SplitView sender, object args) => UpdatePaused();

    private void OnSurfaceVisibilityChanged(object? sender, EventArgs e) => ApplySurfaceVisibility();

    /// <summary>
    /// In high contrast there is no galaxy at all (ADR-017): the row collapses and the trays get the whole
    /// height.
    /// </summary>
    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsZoomed))
        {
            ApplySurfaceVisibility();
            Paths.NotifyZoomChanged();
        }
    }

    /// <summary>
    /// The galaxy only runs while the window is visible, the page is the current step and the history pane is
    /// closed; all three reasons meet here.
    /// </summary>
    private void UpdatePaused() => Galaxy.SetPaused(!_windowVisible || HistorySplitView.IsPaneOpen || _transitions.IsTransitioning);

    // ---- Transition anchors (ADR-023) ---------------------------------------------------------------

    private IEnumerable<TrayControl> Trays => [Tray0, Tray1, Tray2, Tray3, Tray4];

    /// <summary>The heads of the trays with files and the hole of the galaxy; the overlay flies stand-ins of them.</summary>
    public TransitionAnchorSet? MeasureAnchors(UIElement reference)
    {
        if (!IsLoaded)
        {
            return null;
        }

        var anchors = new List<TransitionAnchor>();
        foreach (var tray in Trays)
        {
            if (tray.Tray is { Count: > 0 } vm && TransitionMeasure.Of(tray.Head, reference, TransitionAnchorKind.Tray, vm.Kind) is { } anchor)
            {
                anchors.Add(anchor);
            }
        }

        if (Galaxy.TryGetHole(reference, out var centre, out var radius))
        {
            anchors.Add(new TransitionAnchor(TransitionAnchorKind.Hole, null, centre, System.Numerics.Vector2.Zero, radius));
        }

        return new TransitionAnchorSet(WorkflowStep.Drop, anchors);
    }

    /// <summary>The heads of the kinds in flight vanish; the galaxy is paused by <see cref="UpdatePaused"/>, the overlay draws its hole.</summary>
    public void SetFlightVisibility(bool visible, IReadOnlySet<MediaKind> kinds)
    {
        ArgumentNullException.ThrowIfNull(kinds);
        foreach (var tray in Trays)
        {
            if (tray.Tray is { } vm && kinds.Contains(vm.Kind))
            {
                tray.SetHeadVisible(visible);
            }
        }
    }

    private void ApplySurfaceVisibility()
    {
        var visible = Galaxy.SurfaceVisible;
        GalaxyRow.MinHeight = visible && !_narrow ? GalaxyMinHeight : 0;
        GalaxyRow.Height = visible ? new GridLength(2, GridUnitType.Star) : GridLength.Auto;
        // In the zoom the trays are only a bar of heads, so the galaxy takes the rest of the page.
        TrayRow.Height = ViewModel.IsZoomed ? GridLength.Auto : new GridLength(3, GridUnitType.Star);
        GalaxyRow.MaxHeight = ViewModel.IsZoomed ? double.PositiveInfinity : GalaxyMaxHeight;
    }

    /// <summary>
    /// Under 900 px the five trays keep 200 px each and the row scrolls sideways; under that width the
    /// galaxy also gives up its minimum height. Done in code because a VisualState setter cannot target a
    /// RowDefinition or a ColumnDefinition.
    /// </summary>
    private void OnContentSizeChanged(object sender, SizeChangedEventArgs e)
    {
        var narrow = e.NewSize.Width < NarrowWidth;
        if (narrow == _narrow && _widthApplied)
        {
            return;
        }

        _narrow = narrow;
        _widthApplied = true;
        var width = narrow ? new GridLength(200) : new GridLength(1, GridUnitType.Star);
        TrayColumn0.Width = width;
        TrayColumn1.Width = width;
        TrayColumn2.Width = width;
        TrayColumn3.Width = width;
        TrayColumn4.Width = width;
        TrayScroll.HorizontalScrollMode = narrow ? ScrollMode.Auto : ScrollMode.Disabled;
        TrayScroll.HorizontalScrollBarVisibility = narrow ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
        ApplySurfaceVisibility();
    }

    // ---- Drag and drop ------------------------------------------------------------------------------

    private void OnPageDragOver(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = DataPackageOperation.None;
            return;
        }
        e.AcceptedOperation = DataPackageOperation.Copy;
        e.DragUIOverride.Caption = _loc.Get("Main_DropZone_DragCaption");
        ViewModel.Scene.Enqueue(new Scenes.SetDragOver(true));
    }

    private void OnPageDragLeave(object sender, DragEventArgs e) =>
        ViewModel.Scene.Enqueue(new Scenes.SetDragOver(false));

    private async void OnPageDrop(object sender, DragEventArgs e)
    {
        ViewModel.Scene.Enqueue(new Scenes.SetDragOver(false));
        if (!e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            return;
        }
        var deferral = e.GetDeferral();
        IReadOnlyList<Windows.Storage.IStorageItem> items;
        try
        {
            items = await e.DataView.GetStorageItemsAsync();
        }
        finally
        {
            deferral.Complete();
        }
        // A file thrown in while zoomed brings the camera back to the overview, as in the draft.
        ViewModel.ZoomKind = null;
        await ViewModel.AddStorageItemsAsync(items);
    }

    // ---- Zoom ---------------------------------------------------------------------------------------

    private void OnTrayHeadInvoked(object? sender, MediaKind kind) => ViewModel.ToggleZoom(kind);

    /// <summary>Enter on a tray row does what the mint button does: on to step 2.</summary>
    private void OnTrayAccepted(object? sender, EventArgs e)
    {
        if (ViewModel.GoToTargetCommand.CanExecute(null))
        {
            ViewModel.GoToTargetCommand.Execute(null);
        }
    }

    private void OnGalaxyZoomRequested(object? sender, MediaKind? kind) =>
        ViewModel.ZoomKind = kind is { } value && ViewModel.ZoomKind != value ? value : null;

    private void OnPathsBackRequested(object? sender, EventArgs e) => ViewModel.ZoomKind = null;

    // ---- Keyboard -----------------------------------------------------------------------------------

    private void OnOpenAcceleratorInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        // While the overlay flies, the page is not yet the user's: the shortcut does nothing (ADR-023).
        if (_transitions.IsTransitioning)
        {
            return;
        }
        ViewModel.AddFilesCommand.Execute(null);
    }

    private void OnPasteAcceleratorInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        // Text fields keep their own paste.
        if (FocusManager.GetFocusedElement(XamlRoot) is TextBox or NumberBox or PasswordBox)
        {
            return;
        }
        args.Handled = true;
        if (_transitions.IsTransitioning)
        {
            return;
        }
        ViewModel.PasteCommand.Execute(null);
    }

    private void OnEscapeAcceleratorInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (ViewModel.IsZoomed)
        {
            ViewModel.ZoomKind = null;
            args.Handled = true;
        }
    }
}
