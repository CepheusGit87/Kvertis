using Kvertis.App.ViewModels;
using Kvertis.Engine.Abstractions;
using Kvertis.Engine.Conversion;
using Kvertis.Engine.Conversion.Audio;
using Kvertis.Engine.Conversion.Images;
using Kvertis.Engine.Conversion.Video;
using Kvertis.Engine.Estimation;
using Kvertis.Engine.Ffmpeg;
using Kvertis.Engine.Formats;
using Kvertis.Engine.Probing;
using Kvertis.Engine.Processes;
using Kvertis.Engine.Validation;
using Kvertis.Engine.Windows;
using Kvertis.Queue;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Kvertis.App.Services;

/// <summary>Things that must exist before the container is built (loaded asynchronously at start).</summary>
public sealed record StartupState(
    JsonSettingsService Settings,
    JsonFileSpeedProfileStore SpeedProfileStore,
    SpeedProfile SpeedProfile,
    WindowsPlatformServices Platform,
    WindowContext Window,
    IUiDispatcher Dispatcher);

/// <summary>The app's composition root (ADR-009). Engine and queue are wired here and nowhere else.</summary>
public static class ServiceRegistration
{
    public static ServiceProvider Build(StartupState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var services = new ServiceCollection();
        var settings = state.Settings;

        // Logging: local file, only while the user has it enabled (default off). Never any network sink.
        var fileLogger = new FileLoggerProvider(() => settings.Current.LoggingEnabled, AppPaths.LogsFolder);
        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.SetMinimumLevel(LogLevel.Information);
            builder.AddProvider(fileLogger);
        });

        // App infrastructure
        services.AddSingleton<ISettingsService>(settings);
        services.AddSingleton<IWindowContext>(state.Window);
        services.AddSingleton(state.Dispatcher);
        services.AddSingleton<FrameNavigationService>();
        services.AddSingleton<INavigationService>(sp => sp.GetRequiredService<FrameNavigationService>());
        services.AddSingleton<ILocalizer, ResourceLocalizer>();
        services.AddSingleton<ErrorMessageMapper>();
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<IFilePickerService, FilePickerService>();
        services.AddSingleton<ClipboardImageService>();
        services.AddSingleton<ThirdPartyLicensesProvider>();
#if DEBUG
        services.AddSingleton<ILicenseService, DebugLicenseService>();
#else
        services.AddSingleton<ILicenseService, StoreLicenseService>();
#endif
        services.AddSingleton<FreemiumPolicy>();

        // Engine: formats, validation, estimation
        services.AddSingleton<FormatRegistry>();
        services.AddSingleton(InputLimits.Default);
        services.AddSingleton(sp => new InputValidator(sp.GetRequiredService<InputLimits>()));
        services.AddSingleton<IInputValidator>(sp => sp.GetRequiredService<InputValidator>());
        services.AddSingleton(state.SpeedProfile);
        services.AddSingleton(state.SpeedProfileStore);
        services.AddSingleton<ISpeedProfileStore>(state.SpeedProfileStore);
        services.AddSingleton<Estimator>();
        services.AddSingleton<IEstimator>(sp => sp.GetRequiredService<Estimator>());

        // Engine: external processes and ffmpeg (separate process, LGPL build; ADR-002)
        services.AddSingleton<ProcessRunner>();
        services.AddSingleton<IProcessRunner>(sp => sp.GetRequiredService<ProcessRunner>());
        services.AddSingleton<IFfmpegLocator>(_ => new FfmpegLocator(settings.Current.FfmpegDirectory, AppPaths.BundledFfmpegDirectory));
        services.AddSingleton<FfmpegCompliance>();
        services.AddSingleton<IFfmpegFeatures, FfmpegFeatureProbe>();
        services.AddSingleton<FfprobeReader>();
        services.AddSingleton<MediaInfoCache>();
        services.AddSingleton<FfmpegToolset>();

        // Windows platform services (Media Foundation, WIC, process suspend)
        services.AddSingleton(state.Platform);
        services.AddSingleton(state.Platform.Codecs);
        services.AddSingleton(state.Platform.Heic);
        services.AddSingleton(state.Platform.Suspender);

        // Engine: detection and probing
        services.AddSingleton<IMediaProber, ImageProber>();
        services.AddSingleton<IMediaProber, MediaProber>();
        services.AddSingleton<IFormatDetector, FormatDetector>();

        // Engine: converters. Registration order is priority for ConverterResolver.
        services.AddSingleton<IConverter, ImageConverter>();
        services.AddSingleton<IConverter, AudioConverter>();
        services.AddSingleton<IConverter, VideoConverter>();
        // TODO(wiring): document converters (Conversion/Documents/*: PDF, Office, text) are being written in
        // parallel. Register them here as IConverter once they exist, plus the Windows PDF rasterizer adapter
        // (state.Platform.Pdf) for PDF -> image.
        services.AddSingleton<IConverterResolver, ConverterResolver>();

        // Queue
        services.AddSingleton(_ => new JsonFileHistoryStore(AppPaths.HistoryFile));
        services.AddSingleton<IHistoryStore>(sp => sp.GetRequiredService<JsonFileHistoryStore>());
        services.AddSingleton(sp => new JobHistory(
            sp.GetRequiredService<IHistoryStore>(),
            logger: sp.GetRequiredService<ILoggerFactory>().CreateLogger<JobHistory>()));
        services.AddSingleton(sp => new ProcessSuspendJobPauser(
            sp.GetRequiredService<IProcessSuspender>(),
            sp.GetRequiredService<IProcessRunner>()));
        services.AddSingleton(sp => new JobQueueOptions
        {
            MaxParallel = settings.Current.MaxParallel ?? Math.Max(1, Environment.ProcessorCount),
            Pauser = sp.GetRequiredService<ProcessSuspendJobPauser>(),
            AdmissionPolicy = sp.GetRequiredService<FreemiumPolicy>(),
            History = sp.GetRequiredService<JobHistory>(),
            SpeedProfileStore = sp.GetRequiredService<ISpeedProfileStore>(),
            Registry = sp.GetRequiredService<FormatRegistry>(),
            Logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger<JobQueue>(),
        });
        services.AddSingleton(sp => new JobQueue(
            sp.GetRequiredService<IConverterResolver>(),
            sp.GetRequiredService<IInputValidator>(),
            sp.GetRequiredService<IEstimator>(),
            sp.GetRequiredService<JobQueueOptions>()));
        services.AddSingleton<IJobQueue>(sp => sp.GetRequiredService<JobQueue>());
        // ConversionJobFactory is a static helper and needs no registration.

        // View models
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<HistoryViewModel>();
        services.AddSingleton<ProViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<LicensesViewModel>();

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });
    }
}
