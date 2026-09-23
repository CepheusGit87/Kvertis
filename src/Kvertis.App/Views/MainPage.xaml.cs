using System.ComponentModel;
using Kvertis.App.Animations;
using Kvertis.App.Services;
using Kvertis.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;

namespace Kvertis.App.Views;

/// <summary>The main screen. Code-behind only handles drag-and-drop, keyboard and animations; logic is in the view model.</summary>
public sealed partial class MainPage : Page
{
    private static readonly TimeSpan EntranceBatchWindow = TimeSpan.FromMilliseconds(400);

    private readonly ILocalizer _loc;
    private readonly IDialogService _dialogs;
    private readonly HashSet<Guid> _animatedCards = [];
    private DateTime _lastEntrance = DateTime.MinValue;
    private int _entranceIndex;
    private bool _initialized;

    public MainPage()
    {
        ViewModel = App.Services.GetRequiredService<MainViewModel>();
        History = App.Services.GetRequiredService<HistoryViewModel>();
        _loc = App.Services.GetRequiredService<ILocalizer>();
        _dialogs = App.Services.GetRequiredService<IDialogService>();
        InitializeComponent();

        ViewModel.PreviewRequested += OnPreviewRequested;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        Loaded += OnLoaded;
    }

    public MainViewModel ViewModel { get; }

    public HistoryViewModel History { get; }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_initialized)
        {
            return;
        }
        _initialized = true;
        await ViewModel.InitializeAsync();
    }

    // ---- Drag and drop ------------------------------------------------------------------------------

    private void OnDropZoneDragOver(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = DataPackageOperation.None;
            return;
        }
        e.AcceptedOperation = DataPackageOperation.Copy;
        e.DragUIOverride.Caption = _loc.Get("Main_DropZone_DragCaption");
        CardAnimations.DropZoneHover(DropZone, isOver: true);
    }

    private void OnDropZoneDragLeave(object sender, DragEventArgs e) => CardAnimations.DropZoneHover(DropZone, isOver: false);

    private async void OnDropZoneDrop(object sender, DragEventArgs e)
    {
        CardAnimations.DropZoneHover(DropZone, isOver: false);
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
        await ViewModel.AddStorageItemsAsync(items);
    }

    // ---- Keyboard -----------------------------------------------------------------------------------

    private void OnOpenAcceleratorInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
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
        ViewModel.PasteCommand.Execute(null);
    }

    private void OnJobListPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.OriginalSource is TextBox)
        {
            return;
        }
        switch (e.Key)
        {
            case VirtualKey.Delete:
                ViewModel.RemoveSelected();
                e.Handled = true;
                break;
            case VirtualKey.Space:
                ViewModel.ToggleSelected();
                e.Handled = true;
                break;
            case VirtualKey.Enter when e.OriginalSource is ListViewItem:
                if (ViewModel.StartCommand.CanExecute(null))
                {
                    ViewModel.StartCommand.Execute(null);
                    e.Handled = true;
                }
                break;
        }
    }

    // ---- Animations and announcements ---------------------------------------------------------------

    private void OnJobListContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.InRecycleQueue || args.Item is not JobItemViewModel item || !_animatedCards.Add(item.Id))
        {
            return;
        }
        var now = DateTime.UtcNow;
        _entranceIndex = now - _lastEntrance > EntranceBatchWindow ? 0 : _entranceIndex + 1;
        _lastEntrance = now;
        CardAnimations.CardEntrance(args.ItemContainer, _entranceIndex);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.Announcement) && !string.IsNullOrEmpty(ViewModel.Announcement))
        {
            FrameworkElementAutomationPeer.FromElement(AnnouncementText)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }
    }

    // ---- Preview ------------------------------------------------------------------------------------

    private async void OnPreviewRequested(object? sender, PreviewViewModel preview)
    {
        using (preview)
        {
            var dialog = new PreviewDialog(preview);
            var load = dialog.LoadAsync();
            await _dialogs.ShowAsync(dialog);
            dialog.StopPlayback();
            preview.Cancel();
            await load;
        }
    }
}
