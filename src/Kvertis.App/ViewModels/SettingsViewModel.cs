using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kvertis.App.Helpers;
using Kvertis.App.Services;
using Kvertis.Engine.Abstractions;
using Kvertis.Engine.Ffmpeg;
using Kvertis.Engine.Naming;
using Kvertis.Queue;

namespace Kvertis.App.ViewModels;

/// <summary>Settings screen (docs/06-design.md, "Einstellungen").</summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    public const int MinParallel = 1;
    public const int MaxParallelLimit = 16;

    private static readonly string[] LanguageTags = [string.Empty, "de-DE", "en-US"];

    private readonly ISettingsService _settings;
    private readonly IJobQueue _queue;
    private readonly IFilePickerService _pickers;
    private readonly IFfmpegLocator _ffmpeg;
    private readonly IProcessRunner _runner;
    private readonly IWindowContext _window;
    private readonly ILocalizer _loc;
    private readonly string _initialLanguage;
    private readonly string? _initialFfmpeg;
    private readonly bool _initializing;

    public SettingsViewModel(
        ISettingsService settings,
        IJobQueue queue,
        IFilePickerService pickers,
        IFfmpegLocator ffmpeg,
        IProcessRunner runner,
        IWindowContext window,
        ILocalizer loc)
    {
        _settings = settings;
        _queue = queue;
        _pickers = pickers;
        _ffmpeg = ffmpeg;
        _runner = runner;
        _window = window;
        _loc = loc;

        var s = settings.Current;
        _initialLanguage = s.Language;
        _initialFfmpeg = s.FfmpegDirectory;
        _initializing = true;
        LanguageIndex = Math.Max(0, Array.IndexOf(LanguageTags, s.Language));
        ThemeIndex = (int)s.Theme;
        IsAutoParallel = s.MaxParallel is null;
        MaxParallel = Math.Clamp(s.MaxParallel ?? queue.MaxParallel, MinParallel, MaxParallelLimit);
        OutputLocationIndex = (int)s.OutputLocation;
        NamePattern = s.NamePattern;
        StripMetadata = !s.KeepMetadata;
        FfmpegDirectory = s.FfmpegDirectory ?? string.Empty;
        LoggingEnabled = s.LoggingEnabled;
        _initializing = false;

        VersionText = loc.Format("About_Version_Text", PackageInfo.VersionText);
    }

    public string VersionText { get; }

    [ObservableProperty]
    private int languageIndex;

    [ObservableProperty]
    private int themeIndex;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsManualParallel))]
    private bool isAutoParallel;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MaxParallelText))]
    private double maxParallel;

    [ObservableProperty]
    private int outputLocationIndex;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NamePreview))]
    private string namePattern = OutputNamePattern.Default;

    [ObservableProperty]
    private bool stripMetadata;

    [ObservableProperty]
    private string ffmpegDirectory = string.Empty;

    [ObservableProperty]
    private string ffmpegStatusText = string.Empty;

    [ObservableProperty]
    private bool loggingEnabled;

    [ObservableProperty]
    private bool restartRequired;

    public bool IsManualParallel => !IsAutoParallel;

    public string MaxParallelText => ((int)MaxParallel).ToString(CultureInfo.CurrentCulture);

    public string NamePreview
    {
        get
        {
            try
            {
                return OutputNamePattern.Render(NamePattern, _loc.Get("Settings_NamePattern_SampleFile"), "jpg", DateTimeOffset.Now, 1);
            }
            catch (ArgumentException)
            {
                return string.Empty;
            }
        }
    }

    /// <summary>Checks the located ffmpeg build (found, LGPL without forbidden encoders). Local process only.</summary>
    public async Task RefreshFfmpegStatusAsync()
    {
        if (!_ffmpeg.IsAvailable)
        {
            FfmpegStatusText = _loc.Get("Settings_Ffmpeg_Status_Missing");
            return;
        }
        try
        {
            var report = await FfmpegCompliance.CheckAsync(_ffmpeg, _runner);
            FfmpegStatusText = _loc.Get(report.IsCompliant ? "Settings_Ffmpeg_Status_Ok" : "Settings_Ffmpeg_Status_NotCompliant");
        }
        catch (Exception)
        {
            FfmpegStatusText = _loc.Get("Settings_Ffmpeg_Status_Missing");
        }
    }

    [RelayCommand]
    private async Task PickFfmpegFolderAsync()
    {
        var folder = await _pickers.PickFolderAsync();
        if (folder is not null)
        {
            FfmpegDirectory = folder.Path;
        }
    }

    [RelayCommand]
    private void ResetFfmpegFolder() => FfmpegDirectory = string.Empty;

    partial void OnLanguageIndexChanged(int value)
    {
        if (_initializing)
        {
            return;
        }
        var tag = LanguageTags[Math.Clamp(value, 0, LanguageTags.Length - 1)];
        ThemeHelper.ApplyLanguage(tag);
        Save(s => s.Language = tag);
        UpdateRestartRequired();
    }

    partial void OnThemeIndexChanged(int value)
    {
        if (_initializing)
        {
            return;
        }
        var theme = (AppTheme)Math.Clamp(value, 0, 2);
        ThemeHelper.Apply(_window.Window, theme);
        Save(s => s.Theme = theme);
    }

    partial void OnIsAutoParallelChanged(bool value) => ApplyParallel();

    partial void OnMaxParallelChanged(double value) => ApplyParallel();

    partial void OnOutputLocationIndexChanged(int value)
    {
        if (!_initializing)
        {
            Save(s => s.OutputLocation = (OutputLocationKind)Math.Clamp(value, 0, 2));
        }
    }

    partial void OnNamePatternChanged(string value)
    {
        if (!_initializing)
        {
            Save(s => s.NamePattern = string.IsNullOrWhiteSpace(value) ? OutputNamePattern.Default : value);
        }
    }

    partial void OnStripMetadataChanged(bool value)
    {
        if (!_initializing)
        {
            Save(s => s.KeepMetadata = !value);
        }
    }

    partial void OnFfmpegDirectoryChanged(string value)
    {
        if (_initializing)
        {
            return;
        }
        Save(s => s.FfmpegDirectory = string.IsNullOrWhiteSpace(value) ? null : value);
        UpdateRestartRequired();
    }

    partial void OnLoggingEnabledChanged(bool value)
    {
        if (!_initializing)
        {
            Save(s => s.LoggingEnabled = value);
        }
    }

    private void ApplyParallel()
    {
        if (_initializing)
        {
            return;
        }
        var manual = (int)Math.Clamp(Math.Round(MaxParallel), MinParallel, MaxParallelLimit);
        _queue.MaxParallel = IsAutoParallel ? Math.Max(1, Environment.ProcessorCount) : manual;
        Save(s => s.MaxParallel = IsAutoParallel ? null : manual);
    }

    private void UpdateRestartRequired()
    {
        var s = _settings.Current;
        RestartRequired = !string.Equals(s.Language, _initialLanguage, StringComparison.Ordinal)
                          || !string.Equals(s.FfmpegDirectory, _initialFfmpeg, StringComparison.Ordinal);
    }

    private void Save(Action<AppSettings> change) => _ = _settings.UpdateAsync(change);
}
