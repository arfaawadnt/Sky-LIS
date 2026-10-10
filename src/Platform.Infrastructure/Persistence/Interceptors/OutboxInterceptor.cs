using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Platform.Infrastructure.Persistence.Outbox;
using Platform.SharedKernel.Abstractions;
using Platform.SharedKernel.MultiTenancy;
using Platform.SharedKernel.Primitives;

namespace Platform.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Copies the domain events raised by tracked aggregates into <c>ops.outbox_events</c> before the commit, so the
/// state change and its events land in one transaction (pitfall P-50). Nothing is published from here; the
/// outbox processor delivers after commit. Runs after the tenant stamp so rows carry the tenant.
/// </summary>
public sealed class OutboxInterceptor(IClock clock) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Enqueue(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Enqueue(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Enqueue(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var now = clock.UtcNow;
        var traceParent = Activity.Current?.Id;
        var rows = new List<OutboxEvent>();

        foreach (var entry in context.ChangeTracker.Entries().Where(entry => entry.Entity is IDomainEventSource).ToList())
        {
            var source = (IDomainEventSource)entry.Entity;
            if (source.DomainEvents.Count == 0)
            {
                continue;
            }

            Guid? tenantId = entry.Entity is ITenantScoped { TenantId: var id } && id != Guid.Empty ? id : null;
            rows.AddRange(source.DomainEvents.Select(domainEvent => OutboxEvent.From(domainEvent, tenantId, now, traceParent)));
            source.ClearDomainEvents();
        }

        if (rows.Count > 0)
        {
            context.Set<OutboxEvent>().AddRange(rows);
        }
    }
}
