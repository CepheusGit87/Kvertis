using System.Runtime.InteropServices;
using Windows.Storage;

namespace Kvertis.App.Services;

/// <summary>Every file location the app uses. All user data stays in the package's local app data (ADR-008).</summary>
public static class AppPaths
{
    /// <summary>Packaged: the MSIX local folder. Unpackaged (developer run): %LOCALAPPDATA%\Kvertis.</summary>
    public static string LocalFolder => PackageInfo.HasIdentity
        ? ApplicationData.Current.LocalFolder.Path
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Kvertis");

    public static string LocalCacheFolder => PackageInfo.HasIdentity
        ? ApplicationData.Current.LocalCacheFolder.Path
        : Path.Combine(LocalFolder, "cache");

    public static string SettingsFile => Path.Combine(LocalFolder, "settings.json");

    public static string HistoryFile => Path.Combine(LocalFolder, "history.json");

    public static string SpeedProfileFile => Path.Combine(LocalFolder, "speed-profile.json");

    public static string LogsFolder => Path.Combine(LocalFolder, "logs");

    /// <summary>Temporary PNG files created from clipboard bitmaps.</summary>
    public static string ClipboardFolder => Path.Combine(LocalCacheFolder, "clipboard");

    /// <summary>Bundled LGPL ffmpeg build: Tools/ffmpeg/&lt;arch&gt; next to the executable (docs/07-store.md).</summary>
    public static string BundledFfmpegDirectory =>
        Path.Combine(AppContext.BaseDirectory, "Tools", "ffmpeg", RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant());

    /// <summary>License texts copied from third_party/ into the package.</summary>
    public static string ThirdPartyFolder => Path.Combine(AppContext.BaseDirectory, "ThirdParty");
}
