using Kvertis.App.ViewModels;
using Kvertis.Engine.Abstractions;
using Kvertis.Engine.Conversion;
using Kvertis.Engine.Conversion.Audio;
using Kvertis.Engine.Conversion.Documents;
using Kvertis.Engine.Conversion.Images;
using Kvertis.Engine.Conversion.Models;
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
        services.AddSingleton<IStepNavigationService, StepNavigationService>();
        // State of the three steps (ADR-020): staged files, target plan, output location, history entry.
        services.AddSingleton<IWorkflowSession, WorkflowSession>();
        services.AddSingleton<IMotionSettings, SystemMotionSettings>();
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
        // One ffprobe cache for the prober, the ffmpeg converters and the Media Foundation transcoder (routing by stream codec, ADR-015).
        services.AddSingleton<MediaInfoCache>(state.Platform.MediaInfo);
        services.AddSingleton<FfmpegToolset>();

        // Windows platform services (Media Foundation, WIC, process suspend). ISystemImageCodec (WIC) decodes
        // HEIC/AVIF/RAW/TIFF and encodes TIFF/BMP/GIF for ImageConverter and ImageToPdfConverter (ADR-006).
        services.AddSingleton(state.Platform);
        services.AddSingleton(state.Platform.Codecs);
        services.AddSingleton(state.Platform.Images);
        services.AddSingleton(state.Platform.Suspender);

        // Engine: detection and probing
        services.AddSingleton<IMediaProber, ImageProber>();
        services.AddSingleton<IMediaProber, MediaProber>();
        services.AddSingleton<IMediaProber, DocumentProber>();
        services.AddSingleton<IFormatDetector, FormatDetector>();

        // Engine: converters. Registration order is priority for ConverterResolver.
        services.AddSingleton<IConverter, ImageConverter>();
        // Media Foundation before ffmpeg: inputs with H.264/HEVC/AAC/WMV/… streams are decoded only by the system (ADR-015).
        services.AddSingleton<IConverter>(state.Platform.Transcoder);
        services.AddSingleton<IConverter, AudioConverter>();
        services.AddSingleton<IConverter, VideoConverter>();
        // Documents: PDF pages are rasterized by the Windows PDF engine (system component, no library).
        services.AddSingleton<IPdfRasterizer>(state.Platform.Pdf);
        services.AddSingleton<IConverter, PdfConverter>();
        services.AddSingleton<IConverter, OfficeConverter>();
        services.AddSingleton<IConverter, TextConverter>();
        services.AddSingleton<IConverter, ImageToPdfConverter>();
        // 3D models: own readers and writers, no library (ADR-016).
        services.AddSingleton<IConverter, ModelConverter>();
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
            FormatDetector = sp.GetRequiredService<IFormatDetector>(),
            MediaInfo = sp.GetRequiredService<MediaInfoCache>(),
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
        services.AddSingleton<Kvertis.App.ViewModels.Target.TargetPageViewModel>();
        services.AddSingleton<HistoryViewModel>();
        services.AddSingleton<ProViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<LicensesViewModel>();

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });
    }
}
