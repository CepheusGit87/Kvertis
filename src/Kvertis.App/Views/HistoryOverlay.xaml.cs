using System.ComponentModel;
using System.Numerics;
using Kvertis.App.Helpers;
using Kvertis.App.Services;
using Kvertis.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace Kvertis.App.Views;

/// <summary>
/// The history as a full-window layer over the steps (docs/06-design.md, Teil E Nachtrag). Fades in and out
/// in 240 ms (at once with reduced motion), keeps the keyboard inside like a dialog and hands the focus back
/// when it closes. Filter, search and selection are in <see cref="HistoryViewModel"/>.
/// </summary>
public sealed partial class HistoryOverlay : UserControl
{
    public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(
        nameof(ViewModel), typeof(HistoryViewModel), typeof(HistoryOverlay), new PropertyMetadata(null, OnViewModelChanged));

    /// <summary>Length of the cross-fade (task: 240 ms).</summary>
    private static readonly TimeSpan FadeDuration = TimeSpan.FromMilliseconds(240);

    /// <summary>Below this width the filters move under the title and the timeline narrows.</summary>
    private const double NarrowWidth = 1060;

    // One shared shadow for all cards (1.5: cards cast ThemeShadow at depth 8, never in high contrast).
    private readonly ThemeShadow _cardShadow = new();
    private readonly IMotionSettings _motion;
    private UIElement? _returnFocus;
    private bool _syncingSelection;
    private int _fadeVersion;

    public HistoryOverlay()
    {
        InitializeComponent();
        _motion = App.Services.GetRequiredService<IMotionSettings>();
    }

    /// <summary>Raised once the overlay is fully hidden again; the window restores its own state then.</summary>
    public event EventHandler? Hidden;

    public HistoryViewModel? ViewModel
    {
        get => (HistoryViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    /// <summary>Where the focus goes when the overlay closes and the element it came from is gone.</summary>
    public Control? FallbackFocusTarget { get; set; }

    // ---- x:Bind helpers ------------------------------------------------------------------------------

    /// <summary>
    /// The chosen card moves 5 px towards the detail (.k6-karte.an). The list item clips a transform, so the move
    /// is a margin inside a 12 px reserve on the right, which also holds the 7 px notch.
    /// </summary>
    public static Thickness CardMargin(bool selected) => selected ? new Thickness(5, 4, 7, 4) : new Thickness(0, 4, 12, 4);

    /// <summary>The chosen node is filled with its colour, the others show the background inside the ring.</summary>
    public static Brush NodeFill(bool selected, string key) => selected ? Ui.Brush(key) : Ui.Brush("KvBackgroundBrush");

    /// <summary>Filled part of the before/after bar; at least 2 % so a tiny result still shows.</summary>
    public static GridLength StarFilled(double ratio) => new(Math.Clamp(ratio, 0.02, 1), GridUnitType.Star);

    public static GridLength StarRest(double ratio) => new(1 - Math.Clamp(ratio, 0.02, 1), GridUnitType.Star);

    /// <summary>Width of the 90 px quality meter (.meter), inside its 1 px border.</summary>
    public static double MeterWidth(double ratio) => Math.Round(88 * Math.Clamp(ratio, 0, 1));

    // ---- Open and close ------------------------------------------------------------------------------

    private static void OnViewModelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var overlay = (HistoryOverlay)d;
        if (e.OldValue is HistoryViewModel old)
        {
            old.PropertyChanged -= overlay.OnViewModelPropertyChanged;
            old.Applied -= overlay.OnApplied;
        }
        ((CollectionViewSource)overlay.Resources["DayGroups"]).Source = (e.NewValue as HistoryViewModel)?.Groups;
        if (e.NewValue is HistoryViewModel vm)
        {
            vm.PropertyChanged += overlay.OnViewModelPropertyChanged;
            vm.Applied += overlay.OnApplied;
            overlay.Bindings.Update();
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (ViewModel is not { } vm)
        {
            return;
        }
        switch (e.PropertyName)
        {
            case nameof(HistoryViewModel.IsOpen):
                if (vm.IsOpen)
                {
                    Show();
                }
                else
                {
                    Hide();
                }
                break;
            case nameof(HistoryViewModel.Selected):
                SyncListSelection();
                break;
            case nameof(HistoryViewModel.MatchText):
                AnnounceMatches();
                break;
        }
    }

    private void Show()
    {
        _returnFocus = XamlRoot is { } root ? FocusManager.GetFocusedElement(root) as UIElement : null;
        if (Root.Visibility == Visibility.Collapsed)
        {
            // Start from transparent; a hidden layer keeps the opacity of its last fade otherwise.
            ElementCompositionPreview.GetElementVisual(Root).Opacity = 0f;
            Root.Visibility = Visibility.Visible;
        }
        Fade(1f, () => { });
        // The layer is visible now; the search box (or the close button when there is nothing to search) gets
        // the keyboard on the next tick, when it has been laid out.
        DispatcherQueue.TryEnqueue(() =>
        {
            Control target = ViewModel is { HasItems: true } ? SearchBox : CloseButton;
            target.Focus(FocusState.Programmatic);
        });
    }

    private void Hide()
    {
        Fade(0f, () =>
        {
            Root.Visibility = Visibility.Collapsed;
            Hidden?.Invoke(this, EventArgs.Empty);
            RestoreFocus();
        });
    }

    private void RestoreFocus()
    {
        var target = _returnFocus;
        _returnFocus = null;
        if (target is Control { IsLoaded: true, IsEnabled: true, Visibility: Visibility.Visible } control
            && control.Focus(FocusState.Programmatic))
        {
            return;
        }
        FallbackFocusTarget?.Focus(FocusState.Programmatic);
    }

    /// <summary>Cross-fades the layer; with reduced motion it jumps. A newer fade cancels the pending end of an older one.</summary>
    private void Fade(float to, Action done)
    {
        var version = ++_fadeVersion;
        var visual = ElementCompositionPreview.GetElementVisual(Root);
        if (_motion.ReducedMotion)
        {
            visual.StopAnimation("Opacity");
            visual.Opacity = to;
            done();
            return;
        }
        var compositor = visual.Compositor;
        var animation = compositor.CreateScalarKeyFrameAnimation();
        animation.InsertKeyFrame(1f, to, compositor.CreateCubicBezierEasingFunction(new Vector2(0.2f, 0.9f), new Vector2(0.25f, 1f)));
        animation.Duration = FadeDuration;
        var batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
        visual.StartAnimation("Opacity", animation);
        batch.End();
        batch.Completed += (_, _) =>
        {
            if (version == _fadeVersion)
            {
                done();
            }
        };
    }

    // ---- Keyboard --------------------------------------------------------------------------------------

    /// <summary>Esc clears a search first (like the draft), then closes the layer.</summary>
    private void OnRootKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Escape || ViewModel is not { } vm)
        {
            return;
        }
        e.Handled = true;
        if (SearchBox.FocusState != FocusState.Unfocused && vm.SearchText.Length > 0)
        {
            vm.ClearSearchCommand.Execute(null);
            return;
        }
        vm.IsOpen = false;
    }

    /// <summary>Enter filters at once instead of waiting for the pause.</summary>
    private void OnSearchKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            ViewModel?.ApplySearchNow();
            e.Handled = true;
        }
    }

    // ---- Selection -------------------------------------------------------------------------------------

    private void OnEntrySelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Regrouping empties the list for a moment; that is not the user's choice, the view model keeps its own.
        if (_syncingSelection || ViewModel is not { } vm || EntryList.SelectedItem is not HistoryItemViewModel item)
        {
            return;
        }
        vm.Selected = item;
    }

    private void OnApplied(object? sender, EventArgs e) => SyncListSelection();

    private void SyncListSelection()
    {
        if (ViewModel is not { } vm || ReferenceEquals(EntryList.SelectedItem, vm.Selected))
        {
            return;
        }
        _syncingSelection = true;
        try
        {
            EntryList.SelectedItem = vm.Selected;
        }
        finally
        {
            _syncingSelection = false;
        }
    }

    /// <summary>The match line is a polite live region: the screen reader hears the new count after each search.</summary>
    private void AnnounceMatches()
    {
        if (Root.Visibility == Visibility.Visible
            && FrameworkElementAutomationPeer.FromElement(MatchLine) is { } peer)
        {
            peer.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }
    }

    // ---- Layout ------------------------------------------------------------------------------------------

    /// <summary>
    /// Narrow windows: the filters move to a second head row, the local note goes, the timeline gets 340 px.
    /// Set in code, not in visual state setters on column definitions (see Schritt 1 in docs/06-design.md).
    /// </summary>
    private void OnRootSizeChanged(object sender, SizeChangedEventArgs e)
    {
        var narrow = e.NewSize.Width < NarrowWidth;
        Grid.SetRow(FilterBar, narrow ? 1 : 0);
        Grid.SetColumn(FilterBar, narrow ? 0 : 3);
        Grid.SetColumnSpan(FilterBar, narrow ? 5 : 1);
        FilterBar.HorizontalAlignment = narrow ? HorizontalAlignment.Left : HorizontalAlignment.Stretch;
        LocalNote.Visibility = narrow ? Visibility.Collapsed : Visibility.Visible;
        ListColumn.Width = new GridLength(narrow ? 340 : 410);
    }

    // ---- Cards -------------------------------------------------------------------------------------------

    private void OnCardLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Border card)
        {
            return;
        }
        if (_motion.IsHighContrast)
        {
            card.Shadow = null;
            card.Translation = Vector3.Zero;
            return;
        }
        card.Shadow = _cardShadow;
        card.Translation = new Vector3(0, 0, 8);
    }

    // .k6-karte:hover: the border turns line-stark unless the card is the chosen one (mint-rahmen).
    private void OnCardPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Border { DataContext: HistoryItemViewModel { IsSelected: false } } card)
        {
            card.BorderBrush = Ui.Brush("KvLineStrongBrush");
        }
    }

    private void OnCardPointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Border { DataContext: HistoryItemViewModel item } card)
        {
            card.BorderBrush = Ui.Brush(item.IsSelected ? "KvMintFrameBrush" : "KvLineBrush");
        }
    }
}
