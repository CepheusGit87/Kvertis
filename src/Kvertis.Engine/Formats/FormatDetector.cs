using Kvertis.Engine.Abstractions;

namespace Kvertis.Engine.Formats;

/// <summary>
/// Default IFormatDetector: sniffs magic bytes, then hands the file to the probers that support
/// its media kind (ffprobe, image header, PDF) to fill in duration, dimensions and page count.
/// </summary>
public sealed class FormatDetector : IFormatDetector
{
    private const int TextSampleLength = 4096;

    private readonly FormatRegistry _registry;
    private readonly IReadOnlyList<IMediaProber> _probers;

    public FormatDetector(FormatRegistry registry, IEnumerable<IMediaProber> probers)
    {
        _registry = registry;
        _probers = probers.ToList();
    }

    public async Task<InputInfo> DetectAsync(string path, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        FileInfo file;
        try
        {
            file = new FileInfo(path);
            if (!file.Exists)
            {
                throw new ConversionException(ConversionErrorCode.InputNotReadable, path, "detect");
            }
        }
        catch (Exception ex) when (ex is not ConversionException)
        {
            throw ConversionException.From(ex, path, "detect");
        }

        if (file.Length == 0)
        {
            throw new ConversionException(ConversionErrorCode.CorruptFile, path, "detect", "empty file");
        }

        var format = await SniffAsync(path, file.Length, ct).ConfigureAwait(false);
        var descriptor = format is null ? null : _registry.Get(format.Value);
        if (format is null || descriptor is null)
        {
            throw new ConversionException(ConversionErrorCode.UnsupportedFormat, path, "detect");
        }
        if (format.Value == FormatRegistry.LegacyOffice && Conversion.Documents.OfficeProtection.IsEncryptedOfficeFile(path))
        {
            // An OLE container holding EncryptedPackage/EncryptionInfo is a password-protected OOXML file.
            throw new ConversionException(ConversionErrorCode.ProtectedFile, path, "detect", "encrypted office document");
        }
        if (!descriptor.CanRead)
        {
            throw new ConversionException(ConversionErrorCode.UnsupportedFormat, path, "detect", $"format '{format}' is recognized but not readable");
        }

        var warnings = new List<InputWarning>();
        var byExtension = _registry.GetByExtension(path);
        if (byExtension is not null && byExtension.Id != format.Value && !SameFamily(byExtension.Id, format.Value))
        {
            warnings.Add(InputWarning.ExtensionMismatch);
        }

        var info = new InputInfo(path, format.Value, descriptor.Kind, file.Length, null, null, null, null, warnings);

        foreach (var prober in _probers)
        {
            if (!prober.Supports(info.Kind))
            {
                continue;
            }
            ct.ThrowIfCancellationRequested();
            info = await prober.ProbeAsync(info, ct).ConfigureAwait(false);
        }

        return info;
    }

    private static bool SameFamily(FormatId a, FormatId b)
    {
        // MP4/M4A/MOV share a container family; TS/MPEG likewise. Do not nag the user about those.
        var family = new[]
        {
            new HashSet<FormatId> { FormatRegistry.Mp4, FormatRegistry.M4a, FormatRegistry.Mov, FormatRegistry.ThreeGp },
            new HashSet<FormatId> { FormatRegistry.Ogg, FormatRegistry.Opus },
            new HashSet<FormatId> { FormatRegistry.Mkv, FormatRegistry.WebM },
            new HashSet<FormatId> { FormatRegistry.Tiff, FormatRegistry.Raw },
            new HashSet<FormatId> { FormatRegistry.Wmv, FormatRegistry.Wma },
            new HashSet<FormatId> { FormatRegistry.Txt, FormatRegistry.Markdown, FormatRegistry.Csv, FormatRegistry.Html },
        };
        return family.Any(f => f.Contains(a) && f.Contains(b));
    }

    private static async Task<FormatId?> SniffAsync(string path, long length, CancellationToken ct)
    {
        var sampleLength = (int)Math.Min(length, TextSampleLength);
        var buffer = new byte[sampleLength];
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
            var read = 0;
            while (read < sampleLength)
            {
                var n = await stream.ReadAsync(buffer.AsMemory(read, sampleLength - read), ct).ConfigureAwait(false);
                if (n == 0)
                {
                    break;
                }
                read += n;
            }
            var format = MagicBytes.Detect(buffer.AsSpan(0, Math.Min(read, MagicBytes.HeaderLength)), path);
            if (format is not null)
            {
                return format;
            }
            if (read >= 2 && buffer[0] == (byte)'P' && buffer[1] == (byte)'K')
            {
                stream.Position = 0;
                return MagicBytes.DetectZipBased(stream);
            }
            return MagicBytes.DetectText(buffer.AsSpan(0, read), path);
        }
        catch (Exception ex) when (ex is not ConversionException and not OperationCanceledException)
        {
            throw ConversionException.From(ex, path, "detect");
        }
    }
}
