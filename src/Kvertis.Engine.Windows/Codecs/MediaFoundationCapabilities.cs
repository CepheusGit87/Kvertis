using System.Runtime.Versioning;
using Kvertis.Engine.Abstractions;
using Windows.Graphics.Imaging;
using Windows.Media.Core;

namespace Kvertis.Engine.Windows.Codecs;

/// <summary>
/// Probes which Media Foundation encoders/decoders and WIC decoders exist on this machine (ADR-003, ADR-006).
/// The probe runs lazily on first property access (bounded by <see cref="FirstProbeTimeout"/>) and is cached.
/// If the probe has not answered in time the properties report false and switch to the real values once
/// the probe completes. Call <see cref="RefreshAsync"/> at app start (and after the user installed an
/// extension) to avoid the blocking first access. No member ever throws.
/// </summary>
[SupportedOSPlatform("windows10.0.17763.0")]
public sealed class MediaFoundationCapabilities : ISystemCodecCapabilities
{
    /// <summary>Upper bound for the synchronous wait on the very first property access.</summary>
    public static readonly TimeSpan FirstProbeTimeout = TimeSpan.FromSeconds(3);

    private readonly object _gate = new();
    private Snapshot _snapshot = Snapshot.None;
    private Task? _probe;

    public bool CanEncodeH264 => Current.EncodeH264;
    public bool CanEncodeHevc => Current.EncodeHevc;
    public bool CanEncodeAac => Current.EncodeAac;
    public bool CanDecodeHevc => Current.DecodeHevc;
    public bool CanDecodeHeif => Current.DecodeHeif;

    /// <summary>Runs the probe again and replaces the cached snapshot. Never throws.</summary>
    public Task RefreshAsync()
    {
        lock (_gate)
        {
            _probe = Task.Run(ProbeAndStoreAsync);
            return _probe;
        }
    }

    private Snapshot Current
    {
        get
        {
            Task? firstProbe = null;
            lock (_gate)
            {
                if (_probe is null)
                {
                    // Task.Run: the WinRT continuations must not need the caller's (UI) context,
                    // otherwise the bounded wait below would always run into the timeout.
                    _probe = Task.Run(ProbeAndStoreAsync);
                    firstProbe = _probe;
                }
            }

            if (firstProbe is not null)
            {
                try
                {
                    // Deliberate, bounded blocking wait (the only one): the interface is synchronous.
                    firstProbe.Wait(FirstProbeTimeout);
                }
                catch (Exception)
                {
                    // ProbeAndStoreAsync never faults; guard anyway so a property never throws.
                }
            }

            return Volatile.Read(ref _snapshot);
        }
    }

    private async Task ProbeAndStoreAsync()
    {
        var h264 = await HasCodecAsync(CodecKind.Video, CodecCategory.Encoder, CodecSubtypes.VideoFormatH264).ConfigureAwait(false);
        var hevcEnc = await HasCodecAsync(CodecKind.Video, CodecCategory.Encoder, CodecSubtypes.VideoFormatHevc).ConfigureAwait(false);
        var aac = await HasCodecAsync(CodecKind.Audio, CodecCategory.Encoder, CodecSubtypes.AudioFormatAac).ConfigureAwait(false);
        var hevcDec = await HasCodecAsync(CodecKind.Video, CodecCategory.Decoder, CodecSubtypes.VideoFormatHevc).ConfigureAwait(false);
        var heif = HasHeifDecoder();
        Volatile.Write(ref _snapshot, new Snapshot(h264, hevcEnc, aac, hevcDec, heif));
    }

    private static async Task<bool> HasCodecAsync(CodecKind kind, CodecCategory category, string subtype)
    {
        try
        {
            var query = new CodecQuery();
            var found = await query.FindAllAsync(kind, category, subtype).AsTask().ConfigureAwait(false);
            return found.Count > 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// True when the HEIF image decoder (system extension) is registered with WIC. Synchronous and cheap.
    /// Decoding .heic additionally needs the HEVC video decoder, see <see cref="CanDecodeHevc"/>.
    /// </summary>
    internal static bool HasHeifDecoder()
    {
        try
        {
            var heifId = BitmapDecoder.HeifDecoderId;
            foreach (var info in BitmapDecoder.GetDecoderInformationEnumerator())
            {
                if (info.CodecId == heifId)
                {
                    return true;
                }
            }

            return false;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private sealed record Snapshot(bool EncodeH264, bool EncodeHevc, bool EncodeAac, bool DecodeHevc, bool DecodeHeif)
    {
        public static readonly Snapshot None = new(false, false, false, false, false);
    }
}
