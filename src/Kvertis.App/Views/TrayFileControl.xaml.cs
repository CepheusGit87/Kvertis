using Kvertis.App.Animations;
using Kvertis.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;

namespace Kvertis.App.Views;

/// <summary>One row of a tray in step 1. Pure presentation; it only plays its own entrance.</summary>
public sealed partial class TrayFileControl : UserControl
{
    public static readonly DependencyProperty ItemProperty = DependencyProperty.Register(
        nameof(Item), typeof(JobItemViewModel), typeof(TrayFileControl), new PropertyMetadata(null));

    private bool _entered;

    public TrayFileControl()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    /// <summary>The row fades in from 12 px below (250 ms); with reduced motion only the fade remains.</summary>
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
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
