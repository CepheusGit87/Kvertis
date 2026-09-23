using Kvertis.Engine.Abstractions;

namespace Kvertis.Engine.Platform;

/// <summary>Platform-neutral defaults: no system codecs, no system image codecs, no suspend. Kvertis.Engine.Windows replaces these.</summary>
public sealed class NullSystemCodecCapabilities : ISystemCodecCapabilities
{
    public static readonly NullSystemCodecCapabilities Instance = new();
    public bool CanEncodeH264 => false;
    public bool CanEncodeHevc => false;
    public bool CanEncodeAac => false;
    public bool CanDecodeHevc => false;
    public bool CanDecodeHeif => false;
}

/// <summary>Everything available. For tests and for machines where the probe is not wanted.</summary>
public sealed class AllSystemCodecCapabilities : ISystemCodecCapabilities
{
    public static readonly AllSystemCodecCapabilities Instance = new();
    public bool CanEncodeH264 => true;
    public bool CanEncodeHevc => true;
    public bool CanEncodeAac => true;
    public bool CanDecodeHevc => true;
    public bool CanDecodeHeif => true;
}

/// <summary>No system image codecs: HEIC, AVIF, non-DNG RAW, TIFF and the TIFF/BMP/GIF encoders are unavailable.</summary>
public sealed class NullSystemImageCodec : ISystemImageCodec
{
    public static readonly NullSystemImageCodec Instance = new();
    public bool IsAvailable => false;
    public bool CanDecode(FormatId format) => false;
    public bool CanEncode(FormatId format) => false;

    public Task<IReadOnlyList<string>> DecodeToPngFramesAsync(string path, string outputDirectory, int maxFrames, CancellationToken ct) =>
        throw new ConversionException(ConversionErrorCode.MissingSystemCodec, path, "system-decode", "no system image codecs on this platform");

    public Task EncodeFromPngAsync(string pngPath, string outputPath, FormatId format, int quality, CancellationToken ct) =>
        throw new ConversionException(ConversionErrorCode.MissingSystemCodec, outputPath, "system-encode", $"no system encoder for '{format}'");
}

public sealed class NullProcessSuspender : IProcessSuspender
{
    public static readonly NullProcessSuspender Instance = new();
    public bool TrySuspend(int processId) => false;
    public bool TryResume(int processId) => false;
}
