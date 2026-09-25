using Kvertis.Engine.Abstractions;
using Kvertis.Queue;

namespace Kvertis.App.Services;

/// <summary>
/// Free tier limits (docs/01-anforderungen.md, "Monetarisierung"): no video, at most
/// <see cref="FreeBatchLimit"/> files per start. Pro: everything. Enforced here in the app, never in engine or queue.
/// </summary>
public sealed class FreemiumPolicy : IJobAdmissionPolicy
{
    /// <summary>Batch limit of the free tier (final value is an open point in docs/09-roadmap.md).</summary>
    public const int FreeBatchLimit = 5;

    /// <summary>Stable reason keys carried in <see cref="AdmissionResult.Reason"/>.</summary>
    public const string ReasonVideo = TargetPlanner.ReasonVideo;

    public const string ReasonBatchSize = TargetPlanner.ReasonBatchSize;

    private readonly ILicenseService _license;

    public FreemiumPolicy(ILicenseService license)
    {
        _license = license ?? throw new ArgumentNullException(nameof(license));
    }

    /// <summary>True when this kind needs Pro (video). Pure query for step 2 (ADR-020).</summary>
    public bool IsKindLocked(MediaKind kind) => !_license.IsPro && kind == MediaKind.Video;

    /// <summary>How many files one start may contain, null when there is no limit.</summary>
    public int? BatchLimit => _license.IsPro ? null : FreeBatchLimit;

    /// <summary>The limits as step 2 needs them.</summary>
    public TargetLimits Limits => _license.IsPro
        ? TargetLimits.None
        : new TargetLimits(VideoLocked: true, BatchLimit: FreeBatchLimit);

    public AdmissionResult Check(IReadOnlyList<ConversionJob> proposed)
    {
        ArgumentNullException.ThrowIfNull(proposed);
        if (_license.IsPro)
        {
            return AdmissionResult.Allowed;
        }
        if (proposed.Any(j => j.IsVideo))
        {
            return AdmissionResult.Denied(ReasonVideo);
        }
        if (proposed.Count > FreeBatchLimit)
        {
            return AdmissionResult.Denied(ReasonBatchSize);
        }
        return AdmissionResult.Allowed;
    }
}
