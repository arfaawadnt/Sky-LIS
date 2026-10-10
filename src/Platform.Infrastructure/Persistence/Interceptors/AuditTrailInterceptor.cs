using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Platform.Application.Abstractions.Observability;
using Platform.Application.Abstractions.Security;
using Platform.Infrastructure.Persistence.Audit;
using Platform.Infrastructure.Persistence.Outbox;
using Platform.SharedKernel.MultiTenancy;
using Platform.SharedKernel.Primitives;

namespace Platform.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Appends every domain event of a tenant aggregate to <c>audit.audit_trail</c> in the same transaction (01 §4.8,
/// C-16). Per tenant, under <c>pg_advisory_xact_lock(hashtext('audit_chain:' || tenant))</c>: read the last entry,
/// number the new ones after it and chain their SHA-256 hashes. When the save runs outside a transaction this
/// interceptor opens one so the lock spans the read and the insert, and commits it after the save. Events of
/// global aggregates (users, sessions) are not ledgered here; the security events are. Runs after the field-change
/// interceptor and before the outbox interceptor, which drains the events (InterceptorOrderTests).
/// </summary>
public sealed class AuditTrailInterceptor(ICurrentUser currentUser, ITenantContextAccessor tenant, IRequestContext request) : SaveChangesInterceptor
{
    public const string LockPrefix = "audit_chain:";
    private const string CanonicalSql = """SELECT x::text AS "Value" FROM unnest({0}::jsonb[]) WITH ORDINALITY AS t(x, n) ORDER BY n""";
    private IDbContextTransaction? _ownedTransaction;

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Append(eventData.Context, async: false, CancellationToken.None).GetAwaiter().GetResult();
        return base.SavingChanges(eventData, result);
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        await Append(eventData.Context, async: true, cancellationToken).ConfigureAwait(false);
        return await base.SavingChangesAsync(eventData, result, cancellationToken).ConfigureAwait(false);
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        _ownedTransaction?.Commit();
        _ownedTransaction?.Dispose();
        _ownedTransaction = null;
        return base.SavedChanges(eventData, result);
    }

    public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        if (_ownedTransaction is { } transaction)
        {
            _ownedTransaction = null;
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            await transaction.DisposeAsync().ConfigureAwait(false);
        }

        return await base.SavedChangesAsync(eventData, result, cancellationToken).ConfigureAwait(false);
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData)
    {
        _ownedTransaction?.Dispose();   // dispose without commit rolls back
        _ownedTransaction = null;
        base.SaveChangesFailed(eventData);
    }

    public override async Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        if (_ownedTransaction is { } transaction)
        {
            _ownedTransaction = null;
            await transaction.DisposeAsync().ConfigureAwait(false);
        }

        await base.SaveChangesFailedAsync(eventData, cancellationToken).ConfigureAwait(false);
    }

    private async Task Append(DbContext? context, bool async, CancellationToken cancellationToken)
    {
        if (context is null)
        {
            return;
        }

        var pending = new List<Pending>();
        foreach (var entry in context.ChangeTracker.Entries().Where(entry => entry.Entity is IDomainEventSource { DomainEvents.Count: > 0 }).ToList())
        {
            var tenantId = entry.Entity is ITenantScoped { TenantId: var id } && id != Guid.Empty ? id : tenant.CurrentTenantId;
            if (tenantId is null || entry.Entity is Domain.Identity.RefreshSession or Domain.Identity.PlatformUser)
            {
                continue;   // global aggregates: the security events are their ledger
            }

            var aggregateId = entry.Metadata.FindPrimaryKey()?.Properties
                .Where(property => property.Name != nameof(ITenantScoped.TenantId))
                .Select(property => FieldChangeInterceptor.Format(entry.Property(property.Name).CurrentValue) ?? string.Empty)
                .FirstOrDefault() ?? string.Empty;

            foreach (var domainEvent in ((IDomainEventSource)entry.Entity).DomainEvents)
            {
                var type = domainEvent.GetType();
                pending.Add(new Pending(tenantId.Value, domainEvent, type.FullName ?? type.Name, entry.Metadata.ClrType.Name, aggregateId, JsonSerializer.Serialize(domainEvent, type, OutboxEvent.SerializerOptions)));
            }
        }

        if (pending.Count == 0)
        {
            return;
        }

        if (context.Database.CurrentTransaction is null)
        {
            _ownedTransaction = async
                ? await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false)
                : context.Database.BeginTransaction();
        }

        // jsonb normalises key order and whitespace; hash the form the column will return, computed by the engine itself.
        object[] payloads = [pending.Select(p => p.Payload).ToArray()];   // one text[] parameter, not one parameter per payload
        var canonical = async
            ? await context.Database.SqlQueryRaw<string>(CanonicalSql, payloads).ToListAsync(cancellationToken).ConfigureAwait(false)
            : context.Database.SqlQueryRaw<string>(CanonicalSql, payloads).ToList();

        var entries = new List<AuditTrailEntry>(pending.Count);
        foreach (var group in pending.Select((item, index) => (item, payload: canonical[index])).GroupBy(pair => pair.item.TenantId))
        {
            var tenantId = group.Key;
            var lockKey = LockPrefix + tenantId.ToString("D");
            if (async)
            {
                await context.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtext({lockKey}))", cancellationToken).ConfigureAwait(false);
            }
            else
            {
                context.Database.ExecuteSqlInterpolated($"SELECT pg_advisory_xact_lock(hashtext({lockKey}))");
            }

            var lastQuery = context.Set<AuditTrailEntry>().AsNoTracking()
                .Where(entry => entry.TenantId == tenantId)
                .OrderByDescending(entry => entry.Sequence)
                .Select(entry => new { entry.Sequence, entry.EntryHash });
            var last = async ? await lastQuery.FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false) : lastQuery.FirstOrDefault();

            var sequence = last?.Sequence ?? 0;
            var previous = last?.EntryHash;
            foreach (var (item, payload) in group)
            {
                sequence++;
                var at = AuditChain.Truncate(item.Event.OccurredAtUtc);
                var hash = AuditChain.Hash(tenantId, sequence, item.Event.EventId, item.EventType, item.AggregateType, item.AggregateId, currentUser.UserId, at, request.CorrelationId, payload, previous);
                entries.Add(new AuditTrailEntry(tenantId, sequence, item.Event.EventId, item.EventType, item.AggregateType, item.AggregateId, currentUser.UserId, at, request.CorrelationId, payload, previous, hash));
                previous = hash;
            }
        }

        context.Set<AuditTrailEntry>().AddRange(entries);
    }

    private sealed record Pending(Guid TenantId, IDomainEvent Event, string EventType, string AggregateType, string AggregateId, string Payload);
}
