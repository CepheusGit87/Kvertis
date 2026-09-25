using System.ComponentModel;
using System.Numerics;
using Kvertis.App.Animations;
using Kvertis.App.Helpers;
using Kvertis.App.Scenes;
using Kvertis.App.Services;
using Kvertis.App.ViewModels.Convert;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace Kvertis.App.Views;

/// <summary>
/// Step 3 "Umwandeln". The code-behind handles the folder drop on the location card, the live region, the
/// life of the swirl surface (resume, suspend, pause), the transition anchors and the XAML side of the
/// finale: the page shake, the report entrance and the fading of inbox and location card, and the layout of the
/// stage (location width, narrow stacking). Everything else is in <see cref="ConvertPageViewModel"/>.
/// </summary>
public sealed partial class ConvertPage : Page, ITransitionAnchors
{
    private readonly ILocalizer _loc;
    private readonly ILogger<ConvertPage> _logger;
    private readonly ITransitionService _transitions;
    private bool _windowVisible = true;
    private bool _cardsHidden;
    private bool? _narrow;
    private FlyoutBase? _openMenu;
    private string _lastBagText = string.Empty;

    /// <summary>Below this width the stage blocks stack under the stage (worksheet 1.1, 640–899 px).</summary>
    private const double NarrowWidth = 900;

    /// <summary>Smallest width of the list with its fixed columns; below it the list scrolls sideways.</summary>
    private const double ListMinWidth = 822;

    /// <summary>The location block is as wide as the white hole above it: min(300, 30 %) (draft .w5-gross).</summary>
    private const double LocationMaxWidth = 300;

    /// <summary>Drop ring (2) and padding (6) on both sides of the location block.</summary>
    private const double LocationChrome = 16;

    public ConvertPage()
    {
        ViewModel = App.Services.GetRequiredService<ConvertPageViewModel>();
        _loc = App.Services.GetRequiredService<ILocalizer>();
        _logger = App.Services.GetRequiredService<ILoggerFactory>().CreateLogger<ConvertPage>();
        _transitions = App.Services.GetRequiredService<ITransitionService>();
        InitializeComponent();
        SwirlHost.ViewModel = ViewModel;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        // Only one drawing loop runs at a time (ADR-023): the swirl stands still while the overlay flies.
        _transitions.Changed += (_, _) => UpdatePaused();

        // A minimised or hidden window must not keep the drawing thread busy (ADR-018).
        if (App.Services.GetRequiredService<IWindowContext>().Window is { } window)
        {
            window.VisibilityChanged += OnWindowVisibilityChanged;
        }
    }

    public ConvertPageViewModel ViewModel { get; }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        // The page is cached, so the scene in the view model kept its files; only the surface is new.
        SwirlHost.Resume();
        UpdatePaused();
        // LoadAsync never throws; it turns a failure into ViewModel.ErrorText. The guard is the last net.
        _ = LoadAsync();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        SwirlHost.Suspend();
    }

    private async Task LoadAsync()
    {
        try
        {
            await ViewModel.LoadAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Showing the convert page failed");
        }
    }

    private void OnWindowVisibilityChanged(object sender, WindowVisibilityChangedEventArgs args)
    {
        _windowVisible = args.Visible;
        UpdatePaused();
    }

    /// <summary>The swirl only runs while the window is visible and no overlay flies (worksheet "Leistung").</summary>
    private void UpdatePaused() => SwirlHost.SetPaused(!_windowVisible || _transitions.IsTransitioning);

    private void OnLocationDragOver(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems) || !ViewModel.Location.IsEnabled)
        {
            e.AcceptedOperation = DataPackageOperation.None;
            SetDropHighlight(false);
            return;
        }
        e.AcceptedOperation = DataPackageOperation.Link;
        e.DragUIOverride.Caption = _loc.Get("Convert_Location_DropCaption");
        SetDropHighlight(true);
    }

    private void OnLocationDragLeave(object sender, DragEventArgs e) => SetDropHighlight(false);

    /// <summary>.w5-gross.drop: mint surface with a 2 px mint ring while a folder hovers over the block.</summary>
    private void SetDropHighlight(bool on)
    {
        LocationCard.Background = on ? Ui.Brush("KvMintSurfaceBrush") : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        LocationCard.BorderBrush = on ? Ui.Brush("KvMintBrush") : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
    }

    // ---- Menus ------------------------------------------------------------------------------------

    private void OnMenuOpened(object? sender, object e) => _openMenu = sender as FlyoutBase;

    /// <summary>A choice in a location menu closes the menu, like the draft's .w5-menue.</summary>
    private void OnMenuOptionClick(object sender, RoutedEventArgs e)
    {
        _openMenu?.Hide();
        _openMenu = null;
    }

    private void OnBagPointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e) =>
        BagFrame.Stroke = Ui.Brush("KvMintBrush");

    private void OnBagPointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e) =>
        BagFrame.Stroke = Ui.Brush("KvLineStrongBrush");

    // ---- Layout -----------------------------------------------------------------------------------

    private void OnPageSizeChanged(object sender, SizeChangedEventArgs e)
    {
        ApplyLayout(e.NewSize.Width);
        UpdateBagAnchor();
    }

    /// <summary>
    /// From 900 px the inbox and the location lie on the stage (bottom left, bottom right) and the location is
    /// min(300, 30 %) wide like the white hole; below that both stack under the stage and the list keeps its fixed
    /// columns and scrolls sideways. Done in code: setters on grid definitions break the XAML compiler (docs/06).
    /// </summary>
    private void ApplyLayout(double width)
    {
        var narrow = width < NarrowWidth;
        LocationCard.Width = narrow ? double.NaN : Math.Min(LocationMaxWidth, width * 0.3) + LocationChrome;
        if (_narrow == narrow)
        {
            return;
        }
        _narrow = narrow;

        Grid.SetRow(LeftPanel, narrow ? 1 : 0);
        Grid.SetRow(LocationCard, narrow ? 2 : 0);
        LeftPanel.Margin = narrow ? new Thickness(16, 8, 16, 8) : new Thickness(22, 0, 0, 16);
        LocationCard.Margin = narrow ? new Thickness(8, 0, 8, 8) : new Thickness(0, 0, 6, 10);
        LocationCard.HorizontalAlignment = narrow ? HorizontalAlignment.Stretch : HorizontalAlignment.Right;
        ListPanel.MinWidth = narrow ? ListMinWidth : 0;
        ListSideScroller.HorizontalScrollMode = narrow ? ScrollMode.Enabled : ScrollMode.Disabled;
        ListSideScroller.HorizontalScrollBarVisibility = narrow ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
    }

    /// <summary>Without the moving surface nothing draws "n von N" under the white hole; the text stands above the location then.</summary>
    private void OnSwirlSurfaceModeChanged(object? sender, EventArgs e) =>
        DoneText.Visibility = SwirlHost.IsAnimated ? Visibility.Collapsed : Visibility.Visible;

    // Drag-and-drop handlers are events; async void is intended here.
    private async void OnLocationDrop(object sender, DragEventArgs e)
    {
        SetDropHighlight(false);
        if (!e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            return;
        }
        var deferral = e.GetDeferral();
        IReadOnlyList<IStorageItem> items;
        try
        {
            items = await e.DataView.GetStorageItemsAsync();
        }
        finally
        {
            deferral.Complete();
        }
        // Exactly one folder; anything else is not a target.
        var folders = items.OfType<StorageFolder>().ToList();
        if (folders.Count == 1)
        {
            await ViewModel.DropFolderAsync(folders[0]);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ConvertPageViewModel.Announcement):
                if (!string.IsNullOrEmpty(ViewModel.Announcement))
                {
                    FrameworkElementAutomationPeer.FromElement(AnnouncementText)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
                }
                break;
            case nameof(ConvertPageViewModel.OverallAnnouncement):
                FrameworkElementAutomationPeer.FromElement(OverallText)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
                break;
            case nameof(ConvertPageViewModel.IsFinaleShowing):
                ApplyFinaleCards(ViewModel.IsFinaleShowing);
                break;
            case nameof(ConvertPageViewModel.BagText):
                // The bag hops once when a file gets or loses a target of its own (draft .w5-tasche.hupf).
                if (!string.IsNullOrEmpty(_lastBagText) && _lastBagText != ViewModel.BagText)
                {
                    CardAnimations.CountHop(BagBox);
                }
                _lastBagText = ViewModel.BagText;
                break;
            case nameof(ConvertPageViewModel.ShowReportCard):
                if (ViewModel.ShowReportCard && SwirlHost.IsAnimated)
                {
                    CardAnimations.ReportEntrance(SwirlHost.ReportCardElement);
                }
                break;
        }
    }

    // ---- Finale (ADR-023) ---------------------------------------------------------------------------

    /// <summary>Inbox and location card leave while the finale runs and come back for the next round (300 ms).</summary>
    private void ApplyFinaleCards(bool hidden)
    {
        if (_cardsHidden == hidden)
        {
            return;
        }
        _cardsHidden = hidden;
        foreach (var element in new UIElement[] { LeftPanel, LocationCard })
        {
            element.IsHitTestVisible = !hidden;
            if (hidden)
            {
                CardAnimations.FadeOut(element);
                // After the fade the cards leave the tree for keyboard and narrator too; the fixed columns
                // keep their width, so nothing else moves.
                var timer = DispatcherQueue.CreateTimer();
                timer.Interval = CardAnimations.CardFadeDuration;
                timer.IsRepeating = false;
                timer.Tick += (_, _) =>
                {
                    if (_cardsHidden)
                    {
                        element.Visibility = Visibility.Collapsed;
                    }
                };
                timer.Start();
            }
            else
            {
                element.Visibility = Visibility.Visible;
                CardAnimations.FadeIn(element);
            }
        }
    }

    /// <summary>
    /// The supernova: window content, step header and the rows of the list shake once (worksheet "Beben");
    /// the drawing surface is never moved by XAML. The frame and the header are found from the page's own
    /// position in the tree, so the window keeps no knowledge of the finale.
    /// </summary>
    private void OnSwirlShakeRequested(object? sender, EventArgs e)
    {
        try
        {
            var frame = FindAncestor<Frame>(this);
            UIElement? header = null;
            if (frame?.Parent is Panel panel)
            {
                header = panel.Children.OfType<StepHeader>().FirstOrDefault();
            }

            var rows = new List<UIElement>();
            for (var i = 0; i < ViewModel.Rows.Count; i++)
            {
                if (RowsList.ContainerFromIndex(i) is UIElement row)
                {
                    rows.Add(row);
                }
            }

            CardAnimations.PageShake(frame ?? (UIElement)PageRoot, header, rows);
        }
        catch (Exception ex)
        {
            // A missing composition target costs the shake, never the finale.
            _logger.LogDebug(ex, "The page shake could not run");
        }
    }

    private static T? FindAncestor<T>(DependencyObject start) where T : DependencyObject
    {
        var current = VisualTreeHelper.GetParent(start);
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    private void OnBagSizeChanged(object sender, SizeChangedEventArgs e) => UpdateBagAnchor();

    /// <summary>Tells the scene where the bag sits, in surface coordinates; pixels of files with an own target land there.</summary>
    private void UpdateBagAnchor()
    {
        if (BagButton.Visibility != Visibility.Visible || BagButton.ActualWidth <= 0 || SwirlHost.ActualWidth <= 0)
        {
            return;
        }
        try
        {
            var transform = BagButton.TransformToVisual(SwirlHost.Surface);
            var centre = transform.TransformPoint(new Windows.Foundation.Point(BagButton.ActualWidth / 2, BagButton.ActualHeight / 2));
            ViewModel.Scene.Enqueue(new SetBagAnchor(new Vector2((float)centre.X, (float)centre.Y)));
        }
        catch (ArgumentException)
        {
            // Not in the same tree yet (the page is being built); the next layout pass measures again.
        }
    }

    // ---- Transition anchors (ADR-023) ---------------------------------------------------------------

    /// <summary>
    /// The inbox card, the swirl surface (its centre is where the overlay's hole lands; its size places the
    /// sheets on the stack) and the bag. Nothing on this page is hidden during the flight: the page fades in
    /// as a whole.
    /// </summary>
    public TransitionAnchorSet? MeasureAnchors(UIElement reference)
    {
        if (!IsLoaded)
        {
            return null;
        }

        var anchors = new List<TransitionAnchor>();
        if (TransitionMeasure.Of(LeftPanel, reference, TransitionAnchorKind.Inbox) is { } inbox)
        {
            anchors.Add(inbox);
        }

        if (TransitionMeasure.Of(SwirlHost, reference, TransitionAnchorKind.SwirlCentre) is { } swirl)
        {
            anchors.Add(swirl);
        }

        if (BagButton.Visibility == Visibility.Visible && TransitionMeasure.Of(BagButton, reference, TransitionAnchorKind.Bag) is { } bag)
        {
            anchors.Add(bag);
        }

        return new TransitionAnchorSet(WorkflowStep.Convert, anchors);
    }

    public void SetFlightVisibility(bool visible, IReadOnlySet<Kvertis.Engine.Abstractions.MediaKind> kinds)
    {
    }
}
