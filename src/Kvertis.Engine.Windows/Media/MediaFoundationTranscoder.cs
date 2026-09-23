using System.Diagnostics;
using System.Runtime.Versioning;
using Kvertis.Engine.Abstractions;
using Kvertis.Engine.Conversion.Video;
using Kvertis.Engine.Ffmpeg;
using Kvertis.Engine.IO;
using Kvertis.Engine.Probing;
using Kvertis.Engine.Validation;
using Windows.Foundation.Metadata;
using Windows.Graphics.Imaging;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;
using Windows.Storage;
using Windows.Storage.FileProperties;

namespace Kvertis.Engine.Windows.Media;

/// <summary>
/// Converts audio/video inputs with patent-encumbered streams (H.264, HEVC, AAC, MPEG-4 Part 2, WMV/WMA/VC-1,
/// E-AC-3, DTS, AMR, …; see <see cref="EncumberedCodecs"/>) entirely with Windows Media Foundation through the
/// WinRT <see cref="MediaTranscoder"/> (ADR-015). The bundled ffmpeg has no decoders for these formats.
/// Outputs: MP4 (H.264/AAC), M4A (AAC), MP3, WAV, FLAC. Routing and parameters come from the platform-neutral
/// <see cref="TranscodePlan"/>; this class only maps the plan onto a <see cref="MediaEncodingProfile"/>.
/// </summary>
/// <remarks>
/// <para>Stream codecs come from the shared <see cref="MediaInfoCache"/>, filled by ffprobe during detection.
/// ffprobe works with the allowlist build because <c>-show_streams</c> reads container and codec parameters
/// without opening a decoder.</para>
/// <para>Known limitation (Phase 2): <see cref="MediaTranscoder"/> has no switch to drop container metadata;
/// with <see cref="MetadataPolicy.Strip"/> some tags (title, creation time) may be carried over by the media sink.
/// GPS/EXIF blocks of camera MP4 files are not guaranteed to be removed on this path.</para>
/// <para>MediaTranscoder picks the container from the profile. The work file nevertheless carries the real
/// extension (<c>&lt;target&gt;.kvertis-tmp.&lt;ext&gt;</c>) and is renamed onto the usual temp path before the
/// atomic commit (ADR-007), so no assumption about extension handling is needed.</para>
/// </remarks>
[SupportedOSPlatform("windows10.0.17763.0")]
public sealed class MediaFoundationTranscoder : IConverter
{
    private const string Step = "mf-transcode";
    private const int BufferSize = 81920;
    private const uint ThumbnailSize = 1024;

    // MF_E_UNSUPPORTED_BYTESTREAM_TYPE: the source resolver cannot open the file.
    private const int UnsupportedByteStream = unchecked((int)0xC00D36C4);

    // MF_E_TOPO_CODEC_NOT_FOUND: no decoder or encoder for a stream.
    private const int TopologyCodecNotFound = unchecked((int)0xC00D5212);

    private readonly MediaInfoCache _mediaInfo;
    private readonly ISystemCodecCapabilities _codecs;

    public MediaFoundationTranscoder(MediaInfoCache mediaInfo, ISystemCodecCapabilities codecs)
    {
        ArgumentNullException.ThrowIfNull(mediaInfo);
        ArgumentNullException.ThrowIfNull(codecs);
        _mediaInfo = mediaInfo;
        _codecs = codecs;
    }

    public string Name => "mf";

    public bool Supports(InputInfo input, FormatId output)
    {
        ArgumentNullException.ThrowIfNull(input);
        return TranscodePlan.Supports(input, output, CachedMedia(input), _codecs);
    }

    public async Task<ConversionResult> ConvertAsync(
        InputInfo input,
        string outputPath,
        ConversionSettings settings,
        IProgress<ConversionProgress> progress,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(progress);

        var stopwatch = Stopwatch.StartNew();
        var media = CachedMedia(input);
        if (!TranscodePlan.Supports(input, settings.Output, media, _codecs))
        {
            throw new ConversionException(ConversionErrorCode.UnsupportedFormat, input.Path, Step, $"{input.Format} -> {settings.Output}");
        }
        if (media is { IsEncrypted: true })
        {
            throw new ConversionException(ConversionErrorCode.ProtectedFile, input.Path, Step, "encrypted stream");
        }

        var plan = TranscodePlan.Create(input, media, settings);
        string? workPath = null;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(InputLimits.ConversionTimeoutFor(input.Kind));
        try
        {
            progress.Report(ConversionProgress.Start);
            var profile = CreateProfile(plan, input.Path);

            using var output = ConversionOutput.Begin(outputPath, plan.EstimateBytes(media?.Duration ?? input.Duration));
            workPath = output.TempPath + "." + ExtensionFor(plan.Container);
            await using (new FileStream(workPath, FileMode.Create, FileAccess.Write, FileShare.None, 1))
            {
                // An empty file: StorageFile needs an existing path; MediaTranscoder overwrites it.
            }

            var source = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(input.Path)).AsTask(timeout.Token).ConfigureAwait(false);
            var destination = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(workPath)).AsTask(timeout.Token).ConfigureAwait(false);

            var transcoder = new MediaTranscoder
            {
                HardwareAccelerationEnabled = true,
                // Without this, MF may pass matching streams through and ignore bitrate/size settings.
                AlwaysReencode = true,
            };
            var prepared = await transcoder.PrepareFileTranscodeAsync(source, destination, profile).AsTask(timeout.Token).ConfigureAwait(false);
            if (!prepared.CanTranscode)
            {
                throw MapFailure(prepared.FailureReason, input.Path, media);
            }

            progress.Report(new ConversionProgress(FfmpegProgressParser.ConvertStart, ConversionPhase.Converting));
            await prepared.TranscodeAsync().AsTask(timeout.Token, new PercentProgress(progress)).ConfigureAwait(false);

            progress.Report(new ConversionProgress(FfmpegProgressParser.ConvertEnd, ConversionPhase.Finalizing));
            File.Move(workPath, output.TempPath, overwrite: true);
            var bytes = output.Commit();
            progress.Report(ConversionProgress.Complete);
            return new ConversionResult(outputPath, input.SizeBytes, bytes, stopwatch.Elapsed);
        }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested && timeout.IsCancellationRequested)
        {
            throw new ConversionException(ConversionErrorCode.Timeout, input.Path, Step, inner: ex);
        }
        catch (Exception ex)
        {
            throw Map(ex, input.Path, media);
        }
        finally
        {
            if (workPath is not null)
            {
                TryDelete(workPath);
            }
        }
    }

    /// <summary>
    /// Video → MP4: the system thumbnail of the video (<see cref="ThumbnailMode.VideosView"/>) as PNG plus the
    /// size estimate of the plan. Audio outputs and audio inputs have no preview on this path (null).
    /// </summary>
    public async Task<PreviewResult?> PreviewAsync(InputInfo input, ConversionSettings settings, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(settings);
        var media = CachedMedia(input);
        if (input.Kind != MediaKind.Video || !TranscodePlan.Supports(input, settings.Output, media, _codecs))
        {
            return null;
        }

        var plan = TranscodePlan.Create(input, media, settings);
        if (plan.AudioOnly)
        {
            return null;
        }

        var previewPath = Path.Combine(Path.GetTempPath(), "kvertis-preview-" + Guid.NewGuid().ToString("N") + ".png");
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(input.Path)).AsTask(ct).ConfigureAwait(false);
            using var thumbnail = await file.GetThumbnailAsync(ThumbnailMode.VideosView, ThumbnailSize).AsTask(ct).ConfigureAwait(false);
            if (thumbnail is null || thumbnail.Type == ThumbnailType.Icon)
            {
                return null; // No frame available (e.g. HEVC without the system extension).
            }

            var decoder = await BitmapDecoder.CreateAsync(thumbnail).AsTask(ct).ConfigureAwait(false);
            using var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied).AsTask(ct).ConfigureAwait(false);
            await using (var stream = new FileStream(previewPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None, BufferSize, FileOptions.Asynchronous))
            {
                using var ras = stream.AsRandomAccessStream();
                var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, ras).AsTask(ct).ConfigureAwait(false);
                encoder.SetSoftwareBitmap(bitmap);
                await encoder.FlushAsync().AsTask(ct).ConfigureAwait(false);
            }

            return new PreviewResult(previewPath, plan.EstimateBytes(media?.Duration ?? input.Duration));
        }
        catch (Exception ex)
        {
            TryDelete(previewPath);
            throw Map(ex, input.Path, media, "preview");
        }
    }

    private MediaInfo? CachedMedia(InputInfo input) => _mediaInfo.TryGet(input.Path, out var media) ? media : null;

    /// <summary>Maps a <see cref="TranscodePlan"/> onto a WinRT encoding profile.</summary>
    internal static MediaEncodingProfile CreateProfile(TranscodePlan plan, string path)
    {
        switch (plan.Container)
        {
            case TranscodeContainer.Mp4:
                return CreateVideoProfile(plan);
            case TranscodeContainer.M4a:
                return WithAudio(MediaEncodingProfile.CreateM4a(AudioEncodingQuality.High), plan);
            case TranscodeContainer.Mp3:
                return WithAudio(MediaEncodingProfile.CreateMp3(AudioEncodingQuality.High), plan);
            case TranscodeContainer.Wav:
                return WithAudio(MediaEncodingProfile.CreateWav(AudioEncodingQuality.High), plan);
            case TranscodeContainer.Flac:
                // Present since Windows 10 1709; guarded anyway so an unexpected platform reports a clear error.
                if (!ApiInformation.IsMethodPresent("Windows.Media.MediaProperties.MediaEncodingProfile", nameof(MediaEncodingProfile.CreateFlac)))
                {
                    throw new ConversionException(ConversionErrorCode.MissingSystemCodec, path, Step, "flac encoder");
                }
                return WithAudio(MediaEncodingProfile.CreateFlac(AudioEncodingQuality.High), plan);
            default:
                throw new ConversionException(ConversionErrorCode.UnsupportedFormat, path, Step, plan.Container.ToString());
        }
    }

    private static MediaEncodingProfile CreateVideoProfile(TranscodePlan plan)
    {
        // With a known source size, start from a fixed profile and override everything that matters.
        // Unknown size: "Auto" lets MF take the source's size and frame rate.
        var known = plan.Width is > 0 && plan.Height is > 0;
        var profile = MediaEncodingProfile.CreateMp4(known ? VideoEncodingQuality.HD1080p : VideoEncodingQuality.Auto);
        if (known)
        {
            profile.Video.Width = (uint)plan.Width!.Value;
            profile.Video.Height = (uint)plan.Height!.Value;
        }
        if (plan.VideoBitrateKbps is { } videoKbps)
        {
            profile.Video.Bitrate = (uint)videoKbps * 1000;
        }
        if (plan.FrameRate is { } fps)
        {
            profile.Video.FrameRate.Numerator = (uint)Math.Round(fps * 1000);
            profile.Video.FrameRate.Denominator = 1000;
        }

        if (!plan.IncludeAudio)
        {
            profile.Audio = null;
            return profile;
        }
        return WithAudio(profile, plan);
    }

    private static MediaEncodingProfile WithAudio(MediaEncodingProfile profile, TranscodePlan plan)
    {
        if (profile.Audio is null)
        {
            return profile;
        }
        if (plan.AudioBitrateKbps is { } kbps)
        {
            profile.Audio.Bitrate = (uint)kbps * 1000;
        }
        if (plan.SampleRateHz is { } rate)
        {
            profile.Audio.SampleRate = (uint)rate;
        }
        return profile;
    }

    private static string ExtensionFor(TranscodeContainer container) => container switch
    {
        TranscodeContainer.Mp4 => "mp4",
        TranscodeContainer.M4a => "m4a",
        TranscodeContainer.Mp3 => "mp3",
        TranscodeContainer.Wav => "wav",
        TranscodeContainer.Flac => "flac",
        _ => "bin",
    };

    private static ConversionException MapFailure(TranscodeFailureReason reason, string path, MediaInfo? media) => reason switch
    {
        // HEVC needs the system HEVC video extension; tell the UI which codec is missing.
        TranscodeFailureReason.CodecNotFound => new ConversionException(ConversionErrorCode.MissingSystemCodec, path, Step,
            media is { IsHevc: true } ? EncumberedCodecs.Hevc : "system codec"),
        TranscodeFailureReason.InvalidProfile => new ConversionException(ConversionErrorCode.UnsupportedFormat, path, Step, "invalid profile"),
        _ => new ConversionException(ConversionErrorCode.CorruptFile, path, Step, reason.ToString()),
    };

    private static ConversionException Map(Exception ex, string path, MediaInfo? media, string step = Step) => ex switch
    {
        ConversionException ce => ce,
        OperationCanceledException => ConversionException.From(ex, path, step),
        _ when ex.HResult == TopologyCodecNotFound => new ConversionException(ConversionErrorCode.MissingSystemCodec, path, step,
            media is { IsHevc: true } ? EncumberedCodecs.Hevc : $"0x{ex.HResult:X8}", ex),
        _ when ex.HResult == UnsupportedByteStream => new ConversionException(ConversionErrorCode.CorruptFile, path, step, $"0x{ex.HResult:X8}", ex),
        _ => ConversionException.From(ex, path, step),
    };

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>Maps MediaTranscoder's 0..100 progress into the converting band of the job (synchronously).</summary>
    private sealed class PercentProgress(IProgress<ConversionProgress> target) : IProgress<double>
    {
        public void Report(double value)
        {
            var fraction = Math.Clamp(value / 100.0, 0, 1);
            var overall = FfmpegProgressParser.ConvertStart + ((FfmpegProgressParser.ConvertEnd - FfmpegProgressParser.ConvertStart) * fraction);
            target.Report(new ConversionProgress(overall, ConversionPhase.Converting));
        }
    }
}
