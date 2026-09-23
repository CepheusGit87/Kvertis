using System.ComponentModel;
using Kvertis.App.Animations;
using Kvertis.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Kvertis.App.Views;

/// <summary>One file card (docs/06-design.md, "Job-Karte"). Plays the start tilt, progress glow and check mark.</summary>
public sealed partial class JobCardControl : UserControl
{
    public static readonly DependencyProperty ItemProperty = DependencyProperty.Register(
        nameof(Item), typeof(JobItemViewModel), typeof(JobCardControl), new PropertyMetadata(null, OnItemChanged));

    private JobItemViewModel? _subscribed;
    private JobItemState _lastState = JobItemState.Detecting;

    public JobCardControl()
    {
        InitializeComponent();
        Unloaded += (_, _) => Subscribe(null);
        Loaded += (_, _) => Subscribe(Item);
    }

    public JobItemViewModel? Item
    {
        get => (JobItemViewModel?)GetValue(ItemProperty);
        set => SetValue(ItemProperty, value);
    }

    private static void OnItemChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var card = (JobCardControl)d;
        card.Subscribe(e.NewValue as JobItemViewModel);
    }

    private void Subscribe(JobItemViewModel? item)
    {
        if (_subscribed is not null)
        {
            _subscribed.PropertyChanged -= OnItemPropertyChanged;
        }
        _subscribed = item;
        CardAnimations.StopProgressGlow(GlowBar);
        if (item is null)
        {
            return;
        }
        item.PropertyChanged += OnItemPropertyChanged;
        _lastState = item.State;
        if (item.IsRunning)
        {
            CardAnimations.StartProgressGlow(GlowBar, CardRoot);
        }
    }

    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(JobItemViewModel.State) || sender is not JobItemViewModel item)
        {
            return;
        }
        var previous = _lastState;
        _lastState = item.State;
        if (item.State == JobItemState.Running && previous != JobItemState.Running)
        {
            CardAnimations.StartTilt(CardRoot);
            CardAnimations.StartProgressGlow(GlowBar, CardRoot);
        }
        else if (item.State != JobItemState.Running && previous == JobItemState.Running)
        {
            CardAnimations.StopProgressGlow(GlowBar);
        }
        if (item.State == JobItemState.Completed && previous != JobItemState.Completed)
        {
            CardAnimations.CheckMarkPop(CheckIcon);
        }
    }

    private void OnFormatPicked(object? sender, EventArgs e) => FormatFlyout.Hide();

    private void OnPresetClicked(object sender, ItemClickEventArgs e)
    {
        if (Item is { } item && e.ClickedItem is PresetOption preset)
        {
            item.SelectedPreset = preset;
        }
        PresetFlyout.Hide();
    }
}
