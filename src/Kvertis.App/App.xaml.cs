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

            Animations.CardAnimations.Use(_services.GetRequiredService<IMotionSettings>());

            await _services.GetRequiredService<JobHistory>().LoadAsync();
            ClipboardImageService.CleanupOldFiles();

            _window = new MainWindow();
            windowContext.Attach(_window);
            ThemeHelper.Apply(_window, settings.Current.Theme);
            _window.Closed += OnWindowClosed;
            _window.Activate();

            await _services.GetRequiredService<ILicenseService>().RefreshAsync();
#if DEBUG
            await StageDebugFilesAsync();
#endif
        }
        catch (Exception ex)
        {
            Log(ex, "Start failed");
            throw;
        }
    }

#if DEBUG
    /// <summary>
    /// Development aid: KVERTIS_STAGE_FILES holds paths separated by ";". They are staged like dropped files
    /// and step 2 opens right away, so the target page can be looked at without clicking through step 1.
    /// Debug builds only; the shipped app never reads an environment variable.
    /// </summary>
    private async Task StageDebugFilesAsync()
    {
        var value = Environment.GetEnvironmentVariable("KVERTIS_STAGE_FILES");
        if (string.IsNullOrWhiteSpace(value) || _services is null)
        {
            return;
        }
        var paths = value
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(File.Exists)
            .ToList();
        if (paths.Count == 0)
        {
            return;
        }
        await _services.GetRequiredService<ViewModels.MainViewModel>().AddPathsWithSettingsAsync(paths);
        _services.GetRequiredService<IStepNavigationService>().GoTo(WorkflowStep.Target);
    }
#endif

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

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        if (e.Exception is { } exception)
        {
            Log(exception, "Unhandled exception");
        }
    }

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
