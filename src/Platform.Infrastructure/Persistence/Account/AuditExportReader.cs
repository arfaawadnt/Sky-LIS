using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Platform.Application.Abstractions.Security;
using Platform.Application.Account;
using Platform.Application.Audit;
using Platform.Contracts.Audit;
using Platform.Domain.Identity;
using Platform.Infrastructure.Persistence.Audit;

namespace Platform.Infrastructure.Persistence.Account;

/// <summary>
/// The CSV export of the audit log (CP-68 / SA-09): the newest <c>maxRows</c> entries of both audit tables of the current
/// tenant (query filter + RLS), merged in one pass. The paged reader of the foundation merges a window per page, which
/// would read the same rows again for every page of a large export; this reader reads each side once.
/// </summary>
internal sealed class AuditExportReader(AppDbContext context, ICurrentTenant tenant) : IAuditExportReader
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<AuditEntryItem>> ReadAsync(AuditEntryFilter filter, int maxRows, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxRows, 1);

        var changes = context.Set<FieldChange>().AsNoTracking().AsQueryable();
        var trail = context.Set<AuditTrailEntry>().AsNoTracking().AsQueryable();
        if (filter.FromUtc is { } from)
        {
            changes = changes.Where(change => change.AtUtc >= from);
            trail = trail.Where(entry => entry.AtUtc >= from);
        }

        if (filter.ToUtc is { } to)
        {
            changes = changes.Where(change => change.AtUtc <= to);
            trail = trail.Where(entry => entry.AtUtc <= to);
        }

        if (filter.UserId is { } user)
        {
            changes = changes.Where(change => change.ActorUserId == user);
            trail = trail.Where(entry => entry.ActorUserId == user);
        }

        if (!string.IsNullOrWhiteSpace(filter.EntityType))
        {
            var entityType = filter.EntityType.Trim();
            changes = changes.Where(change => change.EntityType == entityType);
            trail = trail.Where(entry => entry.AggregateType == entityType);
        }

        if (!string.IsNullOrWhiteSpace(filter.EntityId))
        {
            var entityId = filter.EntityId.Trim();
            changes = changes.Where(change => change.EntityId == entityId);
            trail = trail.Where(entry => entry.AggregateId == entityId);
        }

        var platformLog = AuditClients.IsPlatformLog(tenant.TenantId);
        if (platformLog && filter.ClientTenantId is { } client)
        {
            (changes, trail) = await AuditClients.NarrowAsync(context, changes, trail, client, cancellationToken).ConfigureAwait(false);
        }

        var changeRows = await changes.OrderByDescending(change => change.AtUtc).ThenByDescending(change => change.Id).Take(maxRows)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var trailRows = await (
            from entry in trail.OrderByDescending(entry => entry.AtUtc).ThenByDescending(entry => entry.Sequence).Take(maxRows)
            join candidate in context.Set<PlatformUser>().AsNoTracking() on entry.ActorUserId equals candidate.Id into actors
            from actor in actors.DefaultIfEmpty()
            select new { entry, ActorName = actor == null ? null : actor.DisplayName }).ToListAsync(cancellationToken).ConfigureAwait(false);

        var merged = changeRows.Select(ToItem)
            .Concat(trailRows.Select(row => ToItem(row.entry, row.ActorName)))
            .OrderByDescending(item => item.AtUtc)
            .ThenByDescending(item => item.Sequence ?? 0)
            .ThenByDescending(item => item.Id)
            .Take(maxRows)
            .ToList();
        return platformLog ? await AuditClients.FillAsync(context, merged, cancellationToken).ConfigureAwait(false) : merged;
    }

    private static AuditEntryItem ToItem(FieldChange change)
    {
        IReadOnlyList<FieldChangeItem> items;
        try
        {
            items = JsonSerializer.Deserialize<List<FieldChangeItem>>(change.Changes, Json) ?? [];
        }
        catch (JsonException)
        {
            items = [];
        }

        return new AuditEntryItem(change.Id, AuditEntryItem.KindFieldChange, change.AtUtc, change.ActorUserId, change.ActorName, change.EntityType, change.EntityId, change.Action.ToString(), change.CorrelationId, items, change.Reason, null, null);
    }

    private static AuditEntryItem ToItem(AuditTrailEntry entry, string? actorName)
    {
        var dot = entry.EventType.LastIndexOf('.');
        var action = dot < 0 ? entry.EventType : entry.EventType[(dot + 1)..];
        return new AuditEntryItem(entry.EventId, AuditEntryItem.KindEvent, entry.AtUtc, entry.ActorUserId, actorName, entry.AggregateType, entry.AggregateId, action, entry.CorrelationId, null, null, entry.Sequence, entry.Payload);
    }
}
