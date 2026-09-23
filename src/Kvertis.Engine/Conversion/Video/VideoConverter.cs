using Kvertis.Engine.Abstractions;
using Kvertis.Engine.Ffmpeg;
using Kvertis.Engine.Formats;

namespace Kvertis.Engine.Conversion.Video;

/// <summary>
/// Video → MP4 (H.264/AAC via Media Foundation only), MKV and WebM (VP9/Opus), for patent-free inputs
/// (VP8/VP9/AV1/Theora/MPEG-1/2 with Opus/Vorbis/FLAC/MP3/AC-3/PCM). The Archive preset with MKV copies the
/// streams unchanged. Inputs with a patent-encumbered stream (<see cref="EncumberedCodecs"/>) never reach
/// ffmpeg, not even for a stream copy: <see cref="Supports"/> is false when cached probe data says so or when
/// there is no probe data for a container other than WebM, and the conversion re-checks after probing (ADR-015). A target size uses a single-pass average bitrate in phase 1
/// (see <see cref="FfmpegArguments.VideoBitrateForTargetSize"/>); exact two-pass encoding is phase 2.
/// </summary>
public sealed class VideoConverter : IConverter
{
    private readonly FfmpegToolset _tools;

    public VideoConverter(FfmpegToolset tools)
    {
        _tools = tools;
    }

    public string Name => "video";

    public bool Supports(InputInfo input, FormatId output)
    {
        ArgumentNullException.ThrowIfNull(input);
        return HandlesKinds(input, output) && _tools.MayUseFfmpeg(input);
    }

    /// <summary>Kind and output check only; the decoder rule is applied by <see cref="Supports"/> and after probing.</summary>
    private static bool HandlesKinds(InputInfo input, FormatId output) =>
        input.Kind == MediaKind.Video && FfmpegArguments.IsVideoOutput(output);

    public Task<ConversionResult> ConvertAsync(
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
        if (_tools.IsKnownToRequireSystemDecoding(input))
        {
            throw new ConversionException(ConversionErrorCode.UnsupportedFormat, input.Path, "convert", FfmpegToolset.RequiresSystemDecodingDetail);
        }
        if (!HandlesKinds(input, settings.Output))
        {
            throw new ConversionException(ConversionErrorCode.UnsupportedFormat, input.Path, "convert", $"{input.Format} -> {settings.Output}");
        }

        // Without cached probe data PrepareAsync probes first and refuses unknown or encumbered streams (ADR-015).
        return _tools.ConvertAsync(input, outputPath, settings, progress,
            (context, tempPath) => FfmpegArguments.Build(input, context.Media, tempPath, settings, _tools.Registry, _tools.Codecs, context.Features),
            ct);
    }

    /// <summary>Extracts one PNG frame at 25 % of the duration; the size estimate comes from the chosen bitrates.</summary>
    public async Task<PreviewResult?> PreviewAsync(InputInfo input, ConversionSettings settings, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(settings);
        // Uncached inputs are probed by PrepareAsync, which applies the decoder rule (ADR-015).
        if (!HandlesKinds(input, settings.Output) || _tools.IsKnownToRequireSystemDecoding(input) || !_tools.Locator.IsAvailable)
        {
            return null;
        }

        var previewPath = FfmpegToolset.NewPreviewPath(_tools.Registry.ExtensionFor(FormatRegistry.Png));
        try
        {
            var context = await _tools.PrepareAsync(input, ct).ConfigureAwait(false);
            var duration = context.Media?.Duration ?? input.Duration;
            var at = duration is { } d && d > TimeSpan.Zero ? d / 4 : TimeSpan.Zero;

            // Validate the real job first so the preview fails the same way the conversion would.
            _ = FfmpegArguments.Build(input, context.Media, previewPath, settings, _tools.Registry, _tools.Codecs, context.Features);
            var arguments = FfmpegArguments.BuildFrameExtraction(input, context.Media, previewPath, at, settings);

            await _tools.RunFfmpegAsync(context.FfmpegPath, arguments, input, null, "preview", ct).ConfigureAwait(false);
            if (new FileInfo(previewPath) is not { Exists: true, Length: > 0 })
            {
                throw new ConversionException(ConversionErrorCode.ToolFailed, input.Path, "preview", "no frame produced");
            }

            var estimated = FfmpegArguments.EstimateOutputBytes(input, context.Media, settings, _tools.Registry);
            return new PreviewResult(previewPath, estimated);
        }
        catch (Exception ex)
        {
            FfmpegToolset.TryDelete(previewPath);
            throw ConversionException.From(ex, input.Path, "preview");
        }
    }
}
