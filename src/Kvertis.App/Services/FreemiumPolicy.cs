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
    public const string ReasonVideo = "video";

    public const string ReasonBatchSize = "batch-size";

    private readonly ILicenseService _license;

    public FreemiumPolicy(ILicenseService license)
    {
        _license = license ?? throw new ArgumentNullException(nameof(license));
    }

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
