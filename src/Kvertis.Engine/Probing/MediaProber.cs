using Kvertis.Engine.Abstractions;
using Kvertis.Engine.Ffmpeg;
using Kvertis.Engine.Formats;

namespace Kvertis.Engine.Probing;

/// <summary>
/// Deep probe for audio and video via ffprobe (separate process). Fills duration and dimensions,
/// adds warnings (VFR, interlaced, several audio tracks, unknown duration) and re-classifies an MP4
/// without a video stream as M4A. Without ffmpeg the info is returned unchanged. Only a protected file
/// is reported as an error here; everything else is left to the converter.
/// </summary>
public sealed class MediaProber : IMediaProber
{
    private readonly FfprobeReader _reader;
    private readonly IFfmpegLocator _locator;
    private readonly MediaInfoCache _cache;

    public MediaProber(FfprobeReader reader, IFfmpegLocator locator, MediaInfoCache cache)
    {
        _reader = reader;
        _locator = locator;
        _cache = cache;
    }

    public bool Supports(MediaKind kind) => kind is MediaKind.Audio or MediaKind.Video;

    public async Task<InputInfo> ProbeAsync(InputInfo info, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(info);
        if (!Supports(info.Kind) || !_locator.IsAvailable)
        {
            return info;
        }

        MediaInfo media;
        if (!_cache.TryGet(info.Path, out media))
        {
            try
            {
                media = await _reader.ReadAsync(info.Path, info.Kind, ct).ConfigureAwait(false);
            }
            catch (ConversionException ex) when (ex.Code == ConversionErrorCode.ProtectedFile)
            {
                throw;
            }
            catch (ConversionException ex) when (ex.Code == ConversionErrorCode.Cancelled)
            {
                throw;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw new ConversionException(ConversionErrorCode.Cancelled, info.Path, "probe");
            }
            catch (Exception)
            {
                // Not certain enough to reject: the converter reports the real error later.
                return info;
            }
            _cache.Set(info.Path, media);
        }

        return Apply(info, media);
    }

    /// <summary>Merges ffprobe findings into <paramref name="info"/>. Pure; exposed for tests.</summary>
    public static InputInfo Apply(InputInfo info, MediaInfo media)
    {
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(media);

        if (media.IsEncrypted)
        {
            throw new ConversionException(ConversionErrorCode.ProtectedFile, info.Path, "probe", "encrypted stream");
        }

        var result = info with
        {
            Duration = media.Duration ?? info.Duration,
            Width = media.Width ?? info.Width,
            Height = media.Height ?? info.Height,
        };

        if (result.Format == FormatRegistry.Mp4 && !media.HasVideo && media.HasAudio)
        {
            result = result with { Format = FormatRegistry.M4a, Kind = MediaKind.Audio };
        }

        if (result.Kind == MediaKind.Video && media.IsVariableFrameRate)
        {
            result = result.WithWarning(InputWarning.VariableFrameRate);
        }
        if (result.Kind == MediaKind.Video && media.IsInterlaced)
        {
            result = result.WithWarning(InputWarning.Interlaced);
        }
        if (media.AudioTrackCount > 1)
        {
            result = result.WithWarning(InputWarning.MultipleAudioTracks);
        }
        if (result.Duration is null)
        {
            result = result.WithWarning(InputWarning.DurationUnknown);
        }
        return result;
    }
}
