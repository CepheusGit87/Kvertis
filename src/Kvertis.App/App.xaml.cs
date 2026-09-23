using Kvertis.App.Helpers;
using Kvertis.App.Services;
using Kvertis.Engine.Estimation;
using Kvertis.Engine.Windows;
using Kvertis.Engine.Windows.Codecs;
using Kvertis.Queue;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace Kvertis.App;

/// <summary>Application entry: loads settings and local profiles, builds the service container, opens the window.</summary>
public partial class App : Application
{
    private MainWindow? _window;
    private ServiceProvider? _services;

    public App()
    {
        InitializeComponent();
        UnhandledException += OnUnhandledException;
    }

    /// <summary>The service container. Available once <see cref="OnLaunched"/> has run.</summary>
    public static IServiceProvider Services =>
        (Current as App)?._services ?? throw new InvalidOperationException("Services are not ready yet.");

    // OnLaunched is the framework's launch event; async void is intended here (exceptions are caught).
    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            var settings = await JsonSettingsService.LoadAsync(AppPaths.SettingsFile);
            ThemeHelper.ApplyLanguage(settings.Current.Language);

            var speedStore = new JsonFileSpeedProfileStore(AppPaths.SpeedProfileFile);
            SpeedProfile profile = await speedStore.LoadAsync();

            var platform = WindowsPlatform.Create();
            if (platform.Codecs is MediaFoundationCapabilities codecs)
            {
                // Probe the system encoders in the background; the format list uses the result.
                _ = codecs.RefreshAsync();
            }

            var windowContext = new WindowContext();
            var dispatcher = new UiDispatcher(DispatcherQueue.GetForCurrentThread());
            _services = ServiceRegistration.Build(new StartupState(settings, speedStore, profile, platform, windowContext, dispatcher));

            await _services.GetRequiredService<JobHistory>().LoadAsync();
            ClipboardImageService.CleanupOldFiles();

            _window = new MainWindow();
            windowContext.Attach(_window);
            ThemeHelper.Apply(_window, settings.Current.Theme);
            _window.Closed += OnWindowClosed;
            _window.Activate();

            await _services.GetRequiredService<ILicenseService>().RefreshAsync();
        }
        catch (Exception ex)
        {
            Log(ex, "Start failed");
            throw;
        }
    }

    private async void OnWindowClosed(object sender, WindowEventArgs args)
    {
        if (_services is null)
        {
            return;
        }
        try
        {
            // Stop running conversions (external processes end, temp files are removed) and persist the speed profile.
            await _services.GetRequiredService<JobQueue>().StopAsync();
            await _services.GetRequiredService<JsonFileSpeedProfileStore>().FlushAsync();
        }
        catch (Exception ex)
        {
            Log(ex, "Shutdown cleanup failed");
        }
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e) =>
        Log(e.Exception, "Unhandled exception");

    private void Log(Exception exception, string message)
    {
        try
        {
            _services?.GetService<ILoggerFactory>()?.CreateLogger<App>().LogError(exception, "{Message}", message);
        }
        catch (Exception)
        {
            // Nothing more we can do.
        }
    }
}
