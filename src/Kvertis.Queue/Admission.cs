namespace Kvertis.Queue;

/// <summary>Answer of an <see cref="IJobAdmissionPolicy"/>. <see cref="Reason"/> is a stable key for the app, not user text.</summary>
public sealed record AdmissionResult(bool IsAllowed, string? Reason = null)
{
    public static readonly AdmissionResult Allowed = new(true);

    public static AdmissionResult Denied(string reason) => new(false, reason);
}

/// <summary>
/// Hook for the app's freemium rules (video, batch size). The queue asks it before accepting jobs;
/// the rules themselves live in the app so the engine and queue stay free of licensing logic.
/// </summary>
public interface IJobAdmissionPolicy
{
    AdmissionResult Check(IReadOnlyList<ConversionJob> proposed);
}

public sealed class AllowAllPolicy : IJobAdmissionPolicy
{
    public static readonly AllowAllPolicy Instance = new();

    public AdmissionResult Check(IReadOnlyList<ConversionJob> proposed) => AdmissionResult.Allowed;
}

/// <summary>Thrown by <see cref="IJobQueue.Enqueue"/> when the admission policy rejects the jobs. Nothing was enqueued.</summary>
public sealed class JobAdmissionException : InvalidOperationException
{
    public JobAdmissionException()
        : this(AdmissionResult.Denied("unspecified"))
    {
    }

    public JobAdmissionException(string message)
        : base(message)
    {
        Result = AdmissionResult.Denied(message);
    }

    public JobAdmissionException(string message, Exception innerException)
        : base(message, innerException)
    {
        Result = AdmissionResult.Denied(message);
    }

    public JobAdmissionException(AdmissionResult result)
        : base($"Jobs rejected by admission policy: {result?.Reason ?? "unspecified"}")
    {
        Result = result ?? AdmissionResult.Denied("unspecified");
    }

    public AdmissionResult Result { get; }
}
