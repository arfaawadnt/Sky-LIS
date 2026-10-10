using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Platform.Application.Audit;
using Platform.Contracts.Audit;
using Platform.Contracts.Common;

namespace Platform.Infrastructure.Persistence.Audit;

/// <summary>
/// The audit log (SRS CP-68 / SA-09) over both audit tables of the current tenant. The two sides are paged
/// separately (each returns at most <c>skip + take</c> rows, newest first) and merged, so the page is exact and no
/// SQL union of differently shaped rows is needed. Chain verification walks the trail in sequence order in batches.
/// </summary>
internal sealed class AuditReader(AppDbContext context) : IAuditReader
{
    private const int VerifyBatch = 1000;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<Paged<AuditEntryItem>> ListAsync(AuditEntryFilter filter, PageRequest page, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var window = page.Skip + page.PageSize;

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

        var total = await changes.LongCountAsync(cancellationToken).ConfigureAwait(false) + await trail.LongCountAsync(cancellationToken).ConfigureAwait(false);

        var changeRows = await changes.OrderByDescending(change => change.AtUtc).ThenByDescending(change => change.Id).Take(window).ToListAsync(cancellationToken).ConfigureAwait(false);
        var trailRows = await (
            from entry in trail.OrderByDescending(entry => entry.AtUtc).ThenByDescending(entry => entry.Sequence).Take(window)
            join candidate in context.PlatformUsers.AsNoTracking() on entry.ActorUserId equals candidate.Id into actors
            from actor in actors.DefaultIfEmpty()
            select new { entry, ActorName = actor == null ? null : actor.DisplayName }).ToListAsync(cancellationToken).ConfigureAwait(false);

        var merged = changeRows.Select(ToItem)
            .Concat(trailRows.Select(row => ToItem(row.entry, row.ActorName)))
            .OrderByDescending(item => item.AtUtc)
            .ThenByDescending(item => item.Sequence ?? 0)
            .ThenByDescending(item => item.Id)
            .Skip(page.Skip)
            .Take(page.PageSize)
            .ToList();

        return new Paged<AuditEntryItem>(merged, page.Page, page.PageSize, total);
    }

    public async Task<ChainVerificationResult> VerifyChainAsync(CancellationToken cancellationToken)
    {
        long expected = 1;
        long checkedCount = 0;
        byte[]? previous = null;

        while (true)
        {
            var batch = await context.Set<AuditTrailEntry>().AsNoTracking()
                .Where(entry => entry.Sequence >= expected)
                .OrderBy(entry => entry.Sequence)
                .Take(VerifyBatch)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            if (batch.Count == 0)
            {
                return new ChainVerificationResult(true, checkedCount, null);
            }

            foreach (var entry in batch)
            {
                if (entry.Sequence != expected)
                {
                    return new ChainVerificationResult(false, checkedCount, expected);   // a gap: a deleted or renumbered row
                }

                var hash = AuditChain.Hash(entry.TenantId, entry.Sequence, entry.EventId, entry.EventType, entry.AggregateType, entry.AggregateId, entry.ActorUserId, entry.AtUtc, entry.CorrelationId, entry.Payload, previous);
                if (!hash.AsSpan().SequenceEqual(entry.EntryHash) || !PrevMatches(entry.PrevHash, previous))
                {
                    return new ChainVerificationResult(false, checkedCount, entry.Sequence);
                }

                previous = entry.EntryHash;
                expected++;
                checkedCount++;
            }
        }
    }

    private static bool PrevMatches(byte[]? stored, byte[]? computed) =>
        (stored is null && computed is null) || (stored is not null && computed is not null && stored.AsSpan().SequenceEqual(computed));

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

    private static AuditEntryItem ToItem(AuditTrailEntry entry, string? actorName) =>
        new(entry.EventId, AuditEntryItem.KindEvent, entry.AtUtc, entry.ActorUserId, actorName, entry.AggregateType, entry.AggregateId, ShortName(entry.EventType), entry.CorrelationId, null, null, entry.Sequence, entry.Payload);

    private static string ShortName(string fullName)
    {
        var dot = fullName.LastIndexOf('.');
        return dot < 0 ? fullName : fullName[(dot + 1)..];
    }
}
