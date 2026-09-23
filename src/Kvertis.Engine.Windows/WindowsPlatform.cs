using System.Runtime.Versioning;
using Kvertis.Engine.Abstractions;
using Kvertis.Engine.Windows.Codecs;
using Kvertis.Engine.Windows.Imaging;
using Kvertis.Engine.Windows.Pdf;
using Kvertis.Engine.Windows.Processes;

namespace Kvertis.Engine.Windows;

/// <summary>All Windows-specific engine services, registered by the app in one call.</summary>
[SupportedOSPlatform("windows10.0.17763.0")]
public sealed record WindowsPlatformServices(
    ISystemCodecCapabilities Codecs,
    IHeicDecoder Heic,
    IProcessSuspender Suspender,
    WindowsPdfRasterizer Pdf);

[SupportedOSPlatform("windows10.0.17763.0")]
public static class WindowsPlatform
{
    /// <summary>
    /// Creates the services. Cheap: the codec probe starts lazily on first access, or earlier via
    /// <see cref="MediaFoundationCapabilities.RefreshAsync"/>.
    /// </summary>
    public static WindowsPlatformServices Create() => new(
        new MediaFoundationCapabilities(),
        new WicHeicDecoder(),
        new WindowsProcessSuspender(),
        new WindowsPdfRasterizer());
}
