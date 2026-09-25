using System.ComponentModel;
using Kvertis.App.Services;
using Kvertis.App.ViewModels.Convert;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace Kvertis.App.Views;

/// <summary>
/// Step 3 "Umwandeln". The code-behind only handles the folder drop on the location card and the live
/// region; everything else is in <see cref="ConvertPageViewModel"/>. The swirl surface stays empty until the
/// drawing layer arrives (ADR-018).
/// </summary>
public sealed partial class ConvertPage : Page
{
    private readonly ILocalizer _loc;
    private readonly ILogger<ConvertPage> _logger;

    public ConvertPage()
    {
        ViewModel = App.Services.GetRequiredService<ConvertPageViewModel>();
        _loc = App.Services.GetRequiredService<ILocalizer>();
        _logger = App.Services.GetRequiredService<ILoggerFactory>().CreateLogger<ConvertPage>();
        InitializeComponent();
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    public ConvertPageViewModel ViewModel { get; }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        // LoadAsync never throws; it turns a failure into ViewModel.ErrorText. The guard is the last net.
        _ = LoadAsync();
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

    private void OnLocationDragOver(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = DataPackageOperation.None;
            return;
        }
        e.AcceptedOperation = DataPackageOperation.Link;
        e.DragUIOverride.Caption = _loc.Get("Convert_Location_DropCaption");
    }

    // Drag-and-drop handlers are events; async void is intended here.
    private async void OnLocationDrop(object sender, DragEventArgs e)
    {
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
        if (e.PropertyName == nameof(ConvertPageViewModel.Announcement) && !string.IsNullOrEmpty(ViewModel.Announcement))
        {
            FrameworkElementAutomationPeer.FromElement(AnnouncementText)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }
        else if (e.PropertyName == nameof(ConvertPageViewModel.OverallAnnouncement))
        {
            FrameworkElementAutomationPeer.FromElement(OverallText)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }
    }
}
