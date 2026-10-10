namespace Platform.SharedKernel.Primitives;

/// <summary>Who did something and when, as resolved per request by the host (never from a token claim or a request body).</summary>
public readonly record struct AuditStamp(DateTimeOffset AtUtc, Guid? UserId, string? DisplayName);

/// <summary>
/// Implemented by aggregate roots so the persistence layer's audit-stamp interceptor can attribute creation and
/// modification (01 §2.3). Domain code never calls these members.
/// </summary>
public interface IAuditStamped
{
    void StampCreated(AuditStamp stamp);

    void StampModified(AuditStamp stamp);
}
