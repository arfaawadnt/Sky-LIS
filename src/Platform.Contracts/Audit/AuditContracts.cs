namespace Platform.Contracts.Audit;

/// <summary>One field of a tracked record, before and after the action (text form; <see langword="null"/> = no value).</summary>
public sealed record FieldChangeItem(string Field, string? Before, string? After);

/// <summary>
/// One line of the unified audit log (SRS CP-68 / SA-09): either a field-level change written by the persistence
/// layer (<c>Kind = "FieldChange"</c>: <see cref="Changes"/> and <see cref="Reason"/> are set) or a domain event
/// appended to the tamper-evident trail (<c>Kind = "Event"</c>: <see cref="Sequence"/> and <see cref="Payload"/> are set).
/// In the platform tenant's log (SA-09) <see cref="ClientTenantId"/> and the client's names say which laboratory the
/// action concerned (a client record, its wallet, or an event naming the client); <see langword="null"/> for the
/// platform's own records and in a laboratory's log (UAT C-18).
/// </summary>
public sealed record AuditEntryItem(
    Guid Id,
    string Kind,
    DateTimeOffset AtUtc,
    Guid? ActorUserId,
    string? ActorName,
    string EntityType,
    string EntityId,
    string Action,
    string? CorrelationId,
    IReadOnlyList<FieldChangeItem>? Changes,
    string? Reason,
    long? Sequence,
    string? Payload,
    Guid? ClientTenantId = null,
    string? ClientNameEn = null,
    string? ClientNameAr = null)
{
    public const string KindFieldChange = "FieldChange";
    public const string KindEvent = "Event";
}

/// <summary>A client (laboratory) the platform audit log can be filtered by (SA-09 "client"; UAT C-18).</summary>
public sealed record AuditClientItem(Guid TenantId, string NameEn, string NameAr, string PortalSlug);

/// <summary>The outcome of walking the tenant's audit chain (01 §4.8): every entry recomputed and linked to its predecessor.</summary>
public sealed record ChainVerificationResult(bool Ok, long Checked, long? FirstBrokenSequence);
