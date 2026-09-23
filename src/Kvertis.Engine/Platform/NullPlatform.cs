using Kvertis.Engine.Abstractions;

namespace Kvertis.Engine.Platform;

/// <summary>Platform-neutral defaults: no system codecs, no HEIC, no suspend. Kvertis.Engine.Windows replaces these.</summary>
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

public sealed class NullHeicDecoder : IHeicDecoder
{
    public static readonly NullHeicDecoder Instance = new();
    public bool IsAvailable => false;

    public Task DecodeToPngAsync(string heicPath, string outputPngPath, CancellationToken ct) =>
        throw new ConversionException(ConversionErrorCode.MissingSystemCodec, heicPath, "heic-decode", "HEIF image extension not available");
}

public sealed class NullProcessSuspender : IProcessSuspender
{
    public static readonly NullProcessSuspender Instance = new();
    public bool TrySuspend(int processId) => false;
    public bool TryResume(int processId) => false;
}
