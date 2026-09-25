using Kvertis.App.Animations;
using Kvertis.App.Helpers;
using Kvertis.App.Services;
using Kvertis.App.ViewModels;
using Kvertis.App.ViewModels.Drop;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;

namespace Kvertis.App.Views;

/// <summary>One row of a tray in step 1. Pure presentation; it only plays its own entrance.</summary>
public sealed partial class TrayFileControl : UserControl
{
    public static readonly DependencyProperty ItemProperty = DependencyProperty.Register(
        nameof(Item), typeof(JobItemViewModel), typeof(TrayFileControl), new PropertyMetadata(null, OnItemChanged));

    private readonly IMotionSettings _motion;

    private bool _entered;
    private bool _hover;

    public TrayFileControl()
    {
        _motion = App.Services.GetRequiredService<IMotionSettings>();
        InitializeComponent();
        Loaded += OnLoaded;
        ActualThemeChanged += (_, _) => ApplyLook();
    }

    /// <summary>The row fades in from 12 px below (250 ms); with reduced motion only the fade remains.</summary>
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ApplyLook();
        if (_entered)
        {
            return;
        }
        _entered = true;
        CardAnimations.CardEntrance(this, index: 0);
    }

    public JobItemViewModel? Item
    {
        get => (JobItemViewModel?)GetValue(ItemProperty);
        set => SetValue(ItemProperty, value);
    }

    private static void OnItemChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) =>
        ((TrayFileControl)sender).ApplyLook();

    private void OnRowPointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        _hover = true;
        ApplyLook();
    }

    private void OnRowPointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        _hover = false;
        ApplyLook();
    }

    private void OnFocusChanged(object sender, RoutedEventArgs e) => ApplyLook();

    /// <summary>
    /// Kind colours of icon and chip, the hover frame of the row (.fz:hover) and the remove cross: hidden until
    /// the row is hovered or the row or the cross has focus (.fz:hover .fz-weg, .fz-weg:focus-visible); in high
    /// contrast it always shows.
    /// </summary>
    private void ApplyLook()
    {
        var key = TrayViewModel.BrushKeyOf(Item?.Kind ?? Kvertis.Engine.Abstractions.MediaKind.Unknown);
        IconBox.Background = Ui.KindVariant(key, "Tint12");
        KindIcon.Foreground = Ui.Brush(key);
        FormatChipBox.Background = Ui.KindVariant(key, "Tint12");
        FormatChipBox.BorderBrush = Ui.KindVariant(key, "Frame40");
        FormatChipText.Foreground = Ui.Brush(key);

        RowFrame.BorderBrush = _hover ? Ui.KindVariant(key, "Frame60") : new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
        RowFrame.Background = _hover ? Ui.KindVariant(key, "Tint12") : Ui.Brush("KvPanel80Brush");

        var focused = FocusState != FocusState.Unfocused || RemoveButton.FocusState != FocusState.Unfocused;
        RemoveButton.Opacity = _hover || focused || _motion.IsHighContrast ? 1.0 : 0.0;
    }

    /// <summary>
    /// A plain UserControl has no automation peer, so the focused row would be silent. The peer names the
    /// row after its file and state, like the old job card.
    /// </summary>
    protected override AutomationPeer OnCreateAutomationPeer() => new RowAutomationPeer(this);

    /// <summary>Raised by Enter on a focused row: the page then moves on to step 2.</summary>
    public event EventHandler? AcceptRequested;

    /// <summary>Delete removes the file, Enter carries on, as on the old job card.</summary>
    private void OnRowKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case Windows.System.VirtualKey.Delete when Item is { CanRemove: true } item:
                item.RemoveCommand.Execute(null);
                e.Handled = true;
                break;
            case Windows.System.VirtualKey.Enter:
                AcceptRequested?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
                break;
        }
    }

    private sealed partial class RowAutomationPeer(TrayFileControl owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.ListItem;

        protected override string GetClassNameCore() => nameof(TrayFileControl);

        protected override string GetNameCore() => owner.Item?.AutomationName ?? base.GetNameCore();

        protected override bool IsKeyboardFocusableCore() => true;
    }
}
