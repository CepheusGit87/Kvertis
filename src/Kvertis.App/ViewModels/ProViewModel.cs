using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kvertis.App.Services;

namespace Kvertis.App.ViewModels;

/// <summary>Kvertis Pro status, purchase and restore (docs/07-store.md, "In-App-Kauf").</summary>
public sealed partial class ProViewModel : ObservableObject, IDisposable
{
    private readonly ILicenseService _license;
    private readonly ILocalizer _loc;
    private readonly IUiDispatcher _ui;

    public ProViewModel(ILicenseService license, ILocalizer loc, IUiDispatcher ui)
    {
        _license = license;
        _loc = loc;
        _ui = ui;
        _license.StatusChanged += OnStatusChanged;
        UpdateStatus();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFree))]
    private bool isPro;

    [ObservableProperty]
    private string statusText = string.Empty;

    [ObservableProperty]
    private string messageText = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PurchaseCommand), nameof(RestoreCommand))]
    private bool isBusy;

    public bool IsFree => !IsPro;

    private bool CanAct() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanAct))]
    private async Task PurchaseAsync()
    {
        IsBusy = true;
        try
        {
            var outcome = await _license.PurchaseAsync();
            MessageText = _loc.Get("Pro_Purchase_" + outcome.ToString());
            UpdateStatus();
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanAct))]
    private async Task RestoreAsync()
    {
        IsBusy = true;
        try
        {
            await _license.RefreshAsync();
            UpdateStatus();
            MessageText = _loc.Get(IsPro ? "Pro_Restore_Found" : "Pro_Restore_NotFound");
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void Dispose() => _license.StatusChanged -= OnStatusChanged;

    private void OnStatusChanged(object? sender, EventArgs e) => _ui.Post(UpdateStatus);

    private void UpdateStatus()
    {
        IsPro = _license.IsPro;
        StatusText = _loc.Get(IsPro ? "Pro_Status_Active" : "Pro_Status_Free");
    }
}
