using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Platform.SharedKernel.Primitives;

namespace Platform.Infrastructure.Persistence.Outbox;

/// <summary>
/// A domain event waiting for delivery, written in the same transaction as the state change that raised it
/// (ADR-0006 carried forward, 01 §4.8). The processor claims rows with <c>FOR UPDATE SKIP LOCKED</c> under a lease,
/// retries with back-off and dead-letters after the last attempt; that worker arrives with its work package.
/// </summary>
public sealed class OutboxEvent
{
    public const int MaxTypeLength = 400;
    public const int MaxTraceParentLength = 64;

    public static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private OutboxEvent()
    {
        Type = string.Empty;
        Payload = string.Empty;
    }

    private OutboxEvent(Guid id, string type, string payload, Guid? tenantId, DateTimeOffset occurredAtUtc, DateTimeOffset enqueuedAtUtc, string? traceParent)
    {
        Id = id;
        Type = type;
        Payload = payload;
        TenantId = tenantId;
        OccurredAtUtc = occurredAtUtc;
        EnqueuedAtUtc = enqueuedAtUtc;
        NextAttemptAtUtc = enqueuedAtUtc;
        TraceParent = traceParent;
    }

    /// <summary>Equals the event's own id, so a consumer can deduplicate on it.</summary>
    public Guid Id { get; private set; }

    /// <summary>The CLR type name of the event; the processor resolves the handler from it.</summary>
    public string Type { get; private set; }

    /// <summary>The event as JSON (<c>jsonb</c>).</summary>
    public string Payload { get; private set; }

    public Guid? TenantId { get; private set; }

    public DateTimeOffset OccurredAtUtc { get; private set; }

    public DateTimeOffset EnqueuedAtUtc { get; private set; }

    public int Attempts { get; private set; }

    public DateTimeOffset NextAttemptAtUtc { get; private set; }

    public DateTimeOffset? ClaimedUntilUtc { get; private set; }

    public DateTimeOffset? ProcessedAtUtc { get; private set; }

    public DateTimeOffset? DeadLetteredAtUtc { get; private set; }

    public string? TraceParent { get; private set; }

    public string? LastError { get; private set; }

    public static OutboxEvent From(IDomainEvent domainEvent, Guid? tenantId, DateTimeOffset enqueuedAtUtc, string? traceParent)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        var type = domainEvent.GetType();
        return new OutboxEvent(
            domainEvent.EventId,
            type.FullName ?? type.Name,
            JsonSerializer.Serialize(domainEvent, type, SerializerOptions),
            tenantId,
            domainEvent.OccurredAtUtc,
            enqueuedAtUtc,
            traceParent is { Length: <= MaxTraceParentLength } ? traceParent : null);
    }

    public void Claim(DateTimeOffset untilUtc) => ClaimedUntilUtc = untilUtc;

    public void MarkProcessed(DateTimeOffset nowUtc)
    {
        ProcessedAtUtc = nowUtc;
        ClaimedUntilUtc = null;
        LastError = null;
    }

    public void RecordFailure(string error, DateTimeOffset nextAttemptAtUtc)
    {
        Attempts++;
        LastError = error;
        NextAttemptAtUtc = nextAttemptAtUtc;
        ClaimedUntilUtc = null;
    }

    public void DeadLetter(DateTimeOffset nowUtc, string error)
    {
        Attempts++;
        LastError = error;
        DeadLetteredAtUtc = nowUtc;
        ClaimedUntilUtc = null;
    }
}

/// <summary><c>ops.outbox_events</c>: nullable tenant, no RLS (the processor runs elevated), partial index on pending rows.</summary>
internal sealed class OutboxEventConfiguration : IEntityTypeConfiguration<OutboxEvent>
{
    public void Configure(EntityTypeBuilder<OutboxEvent> builder)
    {
        builder.ToTable("outbox_events", AppDbContext.OpsSchema);

        builder.HasKey(outboxEvent => outboxEvent.Id);
        builder.Property(outboxEvent => outboxEvent.Id).ValueGeneratedNever();

        builder.Property(outboxEvent => outboxEvent.Type).HasMaxLength(OutboxEvent.MaxTypeLength).IsRequired();
        builder.Property(outboxEvent => outboxEvent.Payload).HasColumnType("jsonb").IsRequired();
        builder.Property(outboxEvent => outboxEvent.TraceParent).HasMaxLength(OutboxEvent.MaxTraceParentLength);
        builder.Property(outboxEvent => outboxEvent.LastError).HasColumnType("text");
        builder.Property(outboxEvent => outboxEvent.Attempts).IsRequired();

        builder.HasIndex(outboxEvent => outboxEvent.NextAttemptAtUtc)
            .HasFilter("processed_at_utc IS NULL AND dead_lettered_at_utc IS NULL")
            .HasDatabaseName("ix_outbox_events_pending");
    }
}
