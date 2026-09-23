using Microsoft.Extensions.Logging;
using Windows.Services.Store;

namespace Kvertis.App.Services;

public enum PurchaseOutcome
{
    Purchased = 0,
    AlreadyOwned,
    Cancelled,
    StoreUnavailable,
    Failed,
}

/// <summary>Kvertis Pro status (docs/07-store.md, "In-App-Kauf").</summary>
public interface ILicenseService
{
    bool IsPro { get; }

    /// <summary>Raised when <see cref="IsPro"/> changes. May be raised on a worker thread.</summary>
    event EventHandler? StatusChanged;

    /// <summary>Asks the Store for the current license. Keeps the last known status when the Store cannot answer.</summary>
    Task RefreshAsync();

    Task<PurchaseOutcome> PurchaseAsync();
}

/// <summary>
/// Store-backed license. The Store client is part of Windows; Kvertis itself contains no network code.
/// In a desktop (WinUI 3) app the <see cref="StoreContext"/> must be tied to the main window handle.
/// </summary>
public sealed class StoreLicenseService : ILicenseService
{
    /// <summary>
    /// Store ID of the durable add-on "Kvertis Pro". Placeholder until the add-on exists in Partner Center.
    /// </summary>
    public const string KVERTIS_PRO_STORE_ID = "9NXXXXXXXXXX";

    private readonly IWindowContext _window;
    private readonly ISettingsService _settings;
    private readonly ILogger<StoreLicenseService> _logger;
    private StoreContext? _context;
    private bool _isPro;

    public StoreLicenseService(IWindowContext window, ISettingsService settings, ILogger<StoreLicenseService> logger)
    {
        _window = window;
        _settings = settings;
        _logger = logger;
        _isPro = settings.Current.LastKnownPro;
    }

    public bool IsPro => _isPro;

    public event EventHandler? StatusChanged;

    public async Task RefreshAsync()
    {
        try
        {
            var context = GetContext();
            var license = await context.GetAppLicenseAsync();
            var owned = false;
            foreach (var addOn in license.AddOnLicenses)
            {
                if (addOn.Value.IsActive && addOn.Value.SkuStoreId.StartsWith(KVERTIS_PRO_STORE_ID, StringComparison.OrdinalIgnoreCase))
                {
                    owned = true;
                    break;
                }
            }
            await SetStatusAsync(owned).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Offline or not installed from the Store: keep the last known status (docs/07).
            _logger.LogInformation(ex, "Store license query failed; keeping last known status");
        }
    }

    public async Task<PurchaseOutcome> PurchaseAsync()
    {
        try
        {
            var context = GetContext();
            var result = await context.RequestPurchaseAsync(KVERTIS_PRO_STORE_ID);
            switch (result.Status)
            {
                case StorePurchaseStatus.Succeeded:
                    await SetStatusAsync(true).ConfigureAwait(false);
                    return PurchaseOutcome.Purchased;
                case StorePurchaseStatus.AlreadyPurchased:
                    await SetStatusAsync(true).ConfigureAwait(false);
                    return PurchaseOutcome.AlreadyOwned;
                case StorePurchaseStatus.NotPurchased:
                    return PurchaseOutcome.Cancelled;
                case StorePurchaseStatus.NetworkError:
                case StorePurchaseStatus.ServerError:
                    return PurchaseOutcome.StoreUnavailable;
                default:
                    return PurchaseOutcome.Failed;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Store purchase failed");
            return PurchaseOutcome.StoreUnavailable;
        }
    }

    private StoreContext GetContext()
    {
        if (_context is not null)
        {
            return _context;
        }
        var context = StoreContext.GetDefault();
        if (_window.Handle != 0)
        {
            WinRT.Interop.InitializeWithWindow.Initialize(context, _window.Handle);
        }
        _context = context;
        return context;
    }

    private async Task SetStatusAsync(bool isPro)
    {
        if (_isPro == isPro && _settings.Current.LastKnownPro == isPro)
        {
            return;
        }
        _isPro = isPro;
        await _settings.UpdateAsync(s => s.LastKnownPro = isPro).ConfigureAwait(false);
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }
}

#if DEBUG
/// <summary>Development builds unlock everything so video can be tested without a Store account.</summary>
public sealed class DebugLicenseService : ILicenseService
{
    public bool IsPro => true;

    public event EventHandler? StatusChanged
    {
        add { }
        remove { }
    }

    public Task RefreshAsync() => Task.CompletedTask;

    public Task<PurchaseOutcome> PurchaseAsync() => Task.FromResult(PurchaseOutcome.AlreadyOwned);
}
#endif
