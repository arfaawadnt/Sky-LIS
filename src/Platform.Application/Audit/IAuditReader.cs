using Platform.Contracts.Audit;
using Platform.Contracts.Common;

namespace Platform.Application.Audit;

/// <summary>Filters of the audit log (SRS CP-68 / SA-09): a time window, an actor, a record type and id. Every value is optional.</summary>
public sealed record AuditEntryFilter(DateTimeOffset? FromUtc, DateTimeOffset? ToUtc, Guid? UserId, string? EntityType, string? EntityId);

/// <summary>
/// Read port over <c>audit.field_changes</c> and <c>audit.audit_trail</c> (01 §4.8): one ordered, paged list of both,
/// bounded to the current tenant by the query filter and by row-level security, and the chain verification operators run.
/// </summary>
public interface IAuditReader
{
    Task<Paged<AuditEntryItem>> ListAsync(AuditEntryFilter filter, PageRequest page, CancellationToken cancellationToken);

    /// <summary>Walks the tenant's trail in sequence order, recomputing every hash against its predecessor.</summary>
    Task<ChainVerificationResult> VerifyChainAsync(CancellationToken cancellationToken);
}
