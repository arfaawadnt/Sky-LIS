using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Platform.SharedKernel.MultiTenancy;

namespace Platform.Infrastructure.Persistence.Audit;

/// <summary>
/// Marks a persistence-owned row (not an aggregate) whose field changes the <c>FieldChangeInterceptor</c> records,
/// for example a tenant setting. Aggregates and entities are tracked without a marker.
/// </summary>
public interface IFieldChangeTracked;

public enum FieldChangeAction
{
    Created = 0,
    Modified = 1,
    Deleted = 2,
}

/// <summary>
/// <c>audit.field_changes</c> (01 §4.8, 03 §8): one row per tracked record per commit with the before/after of every
/// changed field, the actor as of the action and the correlation id. Append-only (trigger in the migration; lis_app
/// has no UPDATE/DELETE on the schema); under FORCE ROW LEVEL SECURITY like every tenant table.
/// </summary>
public sealed class FieldChange : ITenantScoped
{
    public const int MaxEntityTypeLength = 200;
    public const int MaxEntityIdLength = 200;
    public const int MaxActorNameLength = 200;
    public const int MaxCorrelationIdLength = 128;
    public const int MaxReasonLength = 1000;

    private FieldChange()
    {
        EntityType = string.Empty;
        EntityId = string.Empty;
        Changes = "[]";
    }

    public FieldChange(Guid tenantId, string entityType, string entityId, FieldChangeAction action, Guid? actorUserId, string? actorName, DateTimeOffset atUtc, string? correlationId, string changesJson, string? reason)
    {
        Id = Guid.CreateVersion7();
        TenantId = tenantId;
        EntityType = Clip(entityType, MaxEntityTypeLength);
        EntityId = Clip(entityId, MaxEntityIdLength);
        Action = action;
        ActorUserId = actorUserId;
        ActorName = actorName is null ? null : Clip(actorName, MaxActorNameLength);
        AtUtc = atUtc;
        CorrelationId = correlationId is null ? null : Clip(correlationId, MaxCorrelationIdLength);
        Changes = changesJson;
        Reason = reason is null ? null : Clip(reason, MaxReasonLength);
    }

    public Guid TenantId { get; private set; }

    public Guid Id { get; private set; }

    /// <summary>The CLR name of the aggregate or entity (<c>VisitAccount</c>, <c>TenantSetting</c> …).</summary>
    public string EntityType { get; private set; }

    /// <summary>The record id, or the non-tenant key parts joined by <c>:</c> for keyed rows without an id.</summary>
    public string EntityId { get; private set; }

    public FieldChangeAction Action { get; private set; }

    public Guid? ActorUserId { get; private set; }

    /// <summary>The actor's display name as of the action (03 §8).</summary>
    public string? ActorName { get; private set; }

    public DateTimeOffset AtUtc { get; private set; }

    public string? CorrelationId { get; private set; }

    /// <summary><c>[{"field","before","after"}]</c> as jsonb.</summary>
    public string Changes { get; private set; }

    /// <summary>The reason or comment the transition carried, copied from the event it raised (03 §8).</summary>
    public string? Reason { get; private set; }

    private static string Clip(string value, int max) => value.Length <= max ? value : value[..max];
}

/// <summary>
/// <c>audit.audit_trail</c> (01 §4.8, C-16): the tamper-evident ledger of every domain event of a tenant. Entries are
/// numbered per tenant under an advisory transaction lock and SHA-256 chained (<see cref="PrevHash"/> is the
/// predecessor's <see cref="EntryHash"/>), so a deleted, altered or inserted row breaks the chain at that sequence.
/// </summary>
public sealed class AuditTrailEntry : ITenantScoped
{
    public const int MaxEventTypeLength = 400;
    public const int MaxAggregateTypeLength = 200;
    public const int MaxAggregateIdLength = 200;
    public const int MaxCorrelationIdLength = 128;
    public const int HashLength = 32;

    private AuditTrailEntry()
    {
        EventType = string.Empty;
        AggregateType = string.Empty;
        AggregateId = string.Empty;
        Payload = "{}";
        EntryHash = [];
    }

    public AuditTrailEntry(Guid tenantId, long sequence, Guid eventId, string eventType, string aggregateType, string aggregateId, Guid? actorUserId, DateTimeOffset atUtc, string? correlationId, string payload, byte[]? prevHash, byte[] entryHash)
    {
        TenantId = tenantId;
        Sequence = sequence;
        EventId = eventId;
        EventType = eventType.Length <= MaxEventTypeLength ? eventType : eventType[..MaxEventTypeLength];
        AggregateType = aggregateType.Length <= MaxAggregateTypeLength ? aggregateType : aggregateType[..MaxAggregateTypeLength];
        AggregateId = aggregateId.Length <= MaxAggregateIdLength ? aggregateId : aggregateId[..MaxAggregateIdLength];
        ActorUserId = actorUserId;
        AtUtc = atUtc;
        CorrelationId = correlationId is null ? null : correlationId.Length <= MaxCorrelationIdLength ? correlationId : correlationId[..MaxCorrelationIdLength];
        Payload = payload;
        PrevHash = prevHash;
        EntryHash = entryHash;
    }

    public Guid TenantId { get; private set; }

    /// <summary>1, 2, 3 … per tenant, without gaps.</summary>
    public long Sequence { get; private set; }

    /// <summary>The event's own id, equal to the outbox row's id.</summary>
    public Guid EventId { get; private set; }

    public string EventType { get; private set; }

    public string AggregateType { get; private set; }

    public string AggregateId { get; private set; }

    public Guid? ActorUserId { get; private set; }

    /// <summary>The event's occurrence time, truncated to microseconds (what the column stores) so the hash recomputes from the row.</summary>
    public DateTimeOffset AtUtc { get; private set; }

    public string? CorrelationId { get; private set; }

    /// <summary>The event as jsonb, in the engine's canonical text form (what the column returns) so the hash recomputes from the row.</summary>
    public string Payload { get; private set; }

    public byte[]? PrevHash { get; private set; }

    public byte[] EntryHash { get; private set; }
}

/// <summary>
/// <c>audit.security_events</c> (02 §7): authentication, authorization and configuration events. The tenant is
/// nullable before authentication (a failed sign-in for an unknown account); a pre-authentication scope may insert
/// for any tenant, an authenticated scope only for its own; reads are bounded to the tenant like every other table.
/// </summary>
public sealed class SecurityEventRow
{
    public const int MaxNameLength = 64;
    public const int MaxSeverityLength = 16;
    public const int MaxDetailLength = 1000;
    public const int MaxCorrelationIdLength = 128;

    private SecurityEventRow()
    {
        Name = string.Empty;
        Severity = string.Empty;
    }

    public Guid Id { get; private set; }

    public Guid? TenantId { get; private set; }

    public string Name { get; private set; }

    public string Severity { get; private set; }

    public Guid? ActorUserId { get; private set; }

    public IPAddress? ClientIp { get; private set; }

    public byte[]? UserAgentHash { get; private set; }

    public string? CorrelationId { get; private set; }

    public string? Detail { get; private set; }

    public DateTimeOffset AtUtc { get; private set; }
}

internal sealed class FieldChangeConfiguration : IEntityTypeConfiguration<FieldChange>
{
    public void Configure(EntityTypeBuilder<FieldChange> builder)
    {
        builder.ToTable("field_changes", AppDbContext.AuditSchema);
        builder.Property(change => change.EntityType).HasMaxLength(FieldChange.MaxEntityTypeLength).IsRequired();
        builder.Property(change => change.EntityId).HasMaxLength(FieldChange.MaxEntityIdLength).IsRequired();
        builder.Property(change => change.Action).IsRequired();
        builder.Property(change => change.ActorName).HasMaxLength(FieldChange.MaxActorNameLength);
        builder.Property(change => change.AtUtc).IsRequired();
        builder.Property(change => change.CorrelationId).HasMaxLength(FieldChange.MaxCorrelationIdLength);
        builder.Property(change => change.Changes).HasColumnType("jsonb").IsRequired();
        builder.Property(change => change.Reason).HasMaxLength(FieldChange.MaxReasonLength);
        builder.HasIndex(change => new { change.TenantId, change.AtUtc }).HasDatabaseName("ix_field_changes_tenant_time");
        builder.HasIndex(change => new { change.TenantId, change.EntityType, change.EntityId }).HasDatabaseName("ix_field_changes_tenant_entity");
        builder.HasIndex(change => new { change.TenantId, change.ActorUserId }).HasDatabaseName("ix_field_changes_tenant_actor");
    }
}

internal sealed class AuditTrailEntryConfiguration : IEntityTypeConfiguration<AuditTrailEntry>
{
    public void Configure(EntityTypeBuilder<AuditTrailEntry> builder)
    {
        builder.ToTable("audit_trail", AppDbContext.AuditSchema);
        builder.HasKey(entry => new { entry.TenantId, entry.Sequence });
        builder.Property(entry => entry.Sequence).ValueGeneratedNever();
        builder.Property(entry => entry.EventType).HasMaxLength(AuditTrailEntry.MaxEventTypeLength).IsRequired();
        builder.Property(entry => entry.AggregateType).HasMaxLength(AuditTrailEntry.MaxAggregateTypeLength).IsRequired();
        builder.Property(entry => entry.AggregateId).HasMaxLength(AuditTrailEntry.MaxAggregateIdLength).IsRequired();
        builder.Property(entry => entry.AtUtc).IsRequired();
        builder.Property(entry => entry.CorrelationId).HasMaxLength(AuditTrailEntry.MaxCorrelationIdLength);
        builder.Property(entry => entry.Payload).HasColumnType("jsonb").IsRequired();
        builder.Property(entry => entry.PrevHash).HasMaxLength(AuditTrailEntry.HashLength);
        builder.Property(entry => entry.EntryHash).HasMaxLength(AuditTrailEntry.HashLength).IsRequired();
        builder.HasIndex(entry => new { entry.TenantId, entry.EventId }).IsUnique().HasDatabaseName("ux_audit_trail_tenant_event");
        builder.HasIndex(entry => new { entry.TenantId, entry.AtUtc }).HasDatabaseName("ix_audit_trail_tenant_time");
        builder.HasIndex(entry => new { entry.TenantId, entry.AggregateType, entry.AggregateId }).HasDatabaseName("ix_audit_trail_tenant_aggregate");
    }
}

internal sealed class SecurityEventRowConfiguration : IEntityTypeConfiguration<SecurityEventRow>
{
    public void Configure(EntityTypeBuilder<SecurityEventRow> builder)
    {
        // Nullable tenant: its policy is hand-written in the migration (reads by tenant; inserts by a pre-authentication scope for any tenant).
        builder.ToTable("security_events", AppDbContext.AuditSchema);
        builder.HasKey(row => row.Id);
        builder.Property(row => row.Id).ValueGeneratedNever();
        builder.Property(row => row.Name).HasMaxLength(SecurityEventRow.MaxNameLength).IsRequired();
        builder.Property(row => row.Severity).HasMaxLength(SecurityEventRow.MaxSeverityLength).IsRequired();
        builder.Property(row => row.UserAgentHash).HasMaxLength(32);
        builder.Property(row => row.CorrelationId).HasMaxLength(SecurityEventRow.MaxCorrelationIdLength);
        builder.Property(row => row.Detail).HasMaxLength(SecurityEventRow.MaxDetailLength);
        builder.Property(row => row.AtUtc).IsRequired();
        builder.HasIndex(row => new { row.TenantId, row.AtUtc }).HasDatabaseName("ix_security_events_tenant_time");
        builder.HasIndex(row => new { row.ActorUserId, row.AtUtc }).HasDatabaseName("ix_security_events_actor_time");
    }
}
