namespace Platform.Contracts.Audit;

/// <summary>One field of a tracked record, before and after the action (text form; <see langword="null"/> = no value).</summary>
public sealed record FieldChangeItem(string Field, string? Before, string? After);

/// <summary>
/// One line of the unified audit log (SRS CP-68 / SA-09): either a field-level change written by the persistence
/// layer (<c>Kind = "FieldChange"</c>: <see cref="Changes"/> and <see cref="Reason"/> are set) or a domain event
/// appended to the tamper-evident trail (<c>Kind = "Event"</c>: <see cref="Sequence"/> and <see cref="Payload"/> are set).
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
    string? Payload)
{
    public const string KindFieldChange = "FieldChange";
    public const string KindEvent = "Event";
}

/// <summary>The outcome of walking the tenant's audit chain (01 §4.8): every entry recomputed and linked to its predecessor.</summary>
public sealed record ChainVerificationResult(bool Ok, long Checked, long? FirstBrokenSequence);
