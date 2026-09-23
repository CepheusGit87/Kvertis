using System.Runtime.Versioning;
using Kvertis.Engine.Abstractions;
using Kvertis.Engine.Windows.Codecs;
using Kvertis.Engine.Probing;
using Kvertis.Engine.Windows.Imaging;
using Kvertis.Engine.Windows.Media;
using Kvertis.Engine.Windows.Pdf;
using Kvertis.Engine.Windows.Processes;

namespace Kvertis.Engine.Windows;

/// <summary>
/// All Windows-specific engine services, registered by the app in one call. <see cref="MediaInfo"/> is the
/// ffprobe cache shared by the prober, the ffmpeg converters and <see cref="Transcoder"/>; register this
/// instance as the app's only <see cref="MediaInfoCache"/>.
/// </summary>
[SupportedOSPlatform("windows10.0.17763.0")]
public sealed record WindowsPlatformServices(
    ISystemCodecCapabilities Codecs,
    ISystemImageCodec Images,
    IProcessSuspender Suspender,
    WindowsPdfRasterizer Pdf,
    MediaInfoCache MediaInfo,
    IConverter Transcoder);

[SupportedOSPlatform("windows10.0.17763.0")]
public static class WindowsPlatform
{
    /// <summary>
    /// Creates the services. Cheap: the codec probe starts lazily on first access, or earlier via
    /// <see cref="MediaFoundationCapabilities.RefreshAsync"/>.
    /// </summary>
    public static WindowsPlatformServices Create()
    {
        var codecs = new MediaFoundationCapabilities();
        var mediaInfo = new MediaInfoCache();
        return new(
            codecs,
            new WicImageCodec(),
            new WindowsProcessSuspender(),
            new WindowsPdfRasterizer(),
            mediaInfo,
            new MediaFoundationTranscoder(mediaInfo, codecs));
    }
}
