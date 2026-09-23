using Kvertis.Engine.Abstractions;
using Kvertis.Engine.Ffmpeg;

namespace Kvertis.Engine.Conversion.Audio;

/// <summary>
/// Audio → audio and video → audio (sound track extraction, <c>-vn</c>) through ffmpeg as a separate
/// process. AAC only via aac_mf; see <see cref="FfmpegArguments"/> for the encoder table.
/// Inputs with a patent-encumbered stream (AAC, WMA, E-AC-3, …; <see cref="EncumberedCodecs"/>) are never
/// handled here: <see cref="Supports"/> is false when cached probe data says so, and the conversion re-checks
/// after probing (ADR-015). Media Foundation converts those.
/// </summary>
public sealed class AudioConverter : IConverter
{
    public static readonly TimeSpan PreviewExcerpt = TimeSpan.FromSeconds(10);

    private readonly FfmpegToolset _tools;

    public AudioConverter(FfmpegToolset tools)
    {
        _tools = tools;
    }

    public string Name => "audio";

    public bool Supports(InputInfo input, FormatId output)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.Kind is not (MediaKind.Audio or MediaKind.Video) || _tools.IsKnownToRequireSystemDecoding(input))
        {
            return false;
        }
        var descriptor = _tools.Registry.Get(output);
        return descriptor is { Kind: MediaKind.Audio, CanWrite: true };
    }

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
        if (!Supports(input, settings.Output))
        {
            throw new ConversionException(ConversionErrorCode.UnsupportedFormat, input.Path, "convert", $"{input.Format} -> {settings.Output}");
        }

        return _tools.ConvertAsync(input, outputPath, settings, progress,
            (context, tempPath) => FfmpegArguments.Build(input, context.Media, tempPath, settings, _tools.Registry, _tools.Codecs, context.Features),
            ct);
    }

    /// <summary>Encodes a 10-second excerpt from the middle with the same settings and extrapolates the size.</summary>
    public async Task<PreviewResult?> PreviewAsync(InputInfo input, ConversionSettings settings, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(settings);
        if (!Supports(input, settings.Output) || !_tools.Locator.IsAvailable)
        {
            return null;
        }

        var previewPath = FfmpegToolset.NewPreviewPath(_tools.Registry.ExtensionFor(settings.Output));
        try
        {
            var context = await _tools.PrepareAsync(input, ct).ConfigureAwait(false);
            var duration = context.Media?.Duration ?? input.Duration;
            var (start, length) = ExcerptWindow(duration);
            var options = new FfmpegJobOptions { ExcerptStart = start, ExcerptDuration = length, ReportProgress = false };
            var arguments = FfmpegArguments.Build(input, context.Media, previewPath, settings, _tools.Registry, _tools.Codecs, context.Features, options);

            await _tools.RunFfmpegAsync(context.FfmpegPath, arguments, input, null, "preview", ct).ConfigureAwait(false);

            var excerptBytes = new FileInfo(previewPath) is { Exists: true } file ? file.Length : 0;
            if (excerptBytes == 0)
            {
                throw new ConversionException(ConversionErrorCode.ToolFailed, input.Path, "preview", "no output produced");
            }
            var estimated = duration is { } d && length > TimeSpan.Zero
                ? (long)(excerptBytes * (d.TotalSeconds / length.TotalSeconds))
                : excerptBytes;
            return new PreviewResult(previewPath, estimated, length);
        }
        catch (Exception ex)
        {
            FfmpegToolset.TryDelete(previewPath);
            throw ConversionException.From(ex, input.Path, "preview");
        }
    }

    /// <summary>Ten seconds centred on the middle; the whole file when it is shorter; the start when the duration is unknown.</summary>
    public static (TimeSpan? Start, TimeSpan Length) ExcerptWindow(TimeSpan? duration)
    {
        if (duration is not { } d || d <= TimeSpan.Zero)
        {
            return (null, PreviewExcerpt);
        }
        if (d <= PreviewExcerpt)
        {
            return (null, d);
        }
        return ((d / 2) - (PreviewExcerpt / 2), PreviewExcerpt);
    }
}
