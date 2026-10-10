using Microsoft.EntityFrameworkCore;
using Platform.Application.Abstractions.Security;
using Platform.Application.Account;
using Platform.Contracts.Account;
using Platform.Contracts.Common;
using Platform.Domain.Account;
using Platform.SharedKernel.MultiTenancy;

namespace Platform.Infrastructure.Persistence.Account;

/// <summary>
/// The support tickets of the current tenant, or of every laboratory for the platform tenant (INV-SUP-004). The
/// table has no tenant query filter (it is a platform-level table), so this repository applies the scope itself in
/// <see cref="Scoped"/>, which every read goes through, including <see cref="GetForUpdateAsync"/>; the table's RLS
/// policy (<c>support_desk_scope</c>) enforces the same rule in the database. An unresolved tenant sees nothing.
/// </summary>
internal sealed class SupportTicketRepository(AppDbContext context, ICurrentTenant tenant) : Repository<SupportTicket>(context), ISupportTicketRepository
{
    public override Task<SupportTicket?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken) =>
        Scoped().FirstOrDefaultAsync(ticket => ticket.Id == id, cancellationToken);

    public async Task<Paged<SupportTicketListItem>> ListAsync(SupportTicketFilter filter, PageRequest page, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var query = Scoped().AsNoTracking();
        if (filter.TenantId is { } tenantId)
        {
            query = query.Where(ticket => ticket.TenantId == tenantId);
        }

        if (filter.Status is { } status)
        {
            query = query.Where(ticket => ticket.Status == status);
        }

        if (filter.Priority is { } priority)
        {
            query = query.Where(ticket => ticket.Priority == priority);
        }

        if (filter.OpenedByUserId is { } openedBy)
        {
            query = query.Where(ticket => ticket.OpenedByUserId == openedBy);
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var pattern = "%" + EscapeLike(filter.Search.Trim()) + "%";
            query = query.Where(ticket =>
                EF.Functions.ILike(ticket.Number, pattern, "\\")
                || EF.Functions.ILike(ticket.Subject, pattern, "\\")
                || (ticket.LaboratoryNameEn != null && EF.Functions.ILike(ticket.LaboratoryNameEn, pattern, "\\"))
                || (ticket.LaboratoryNameAr != null && EF.Functions.ILike(ticket.LaboratoryNameAr, pattern, "\\")));
        }

        var total = await query.LongCountAsync(cancellationToken).ConfigureAwait(false);
        var rows = await query
            .OrderByDescending(ticket => ticket.LastActivityAtUtc)
            .ThenByDescending(ticket => ticket.Id)
            .Skip(page.Skip)
            .Take(page.PageSize)
            .Select(ticket => new
            {
                ticket.Id,
                ticket.Number,
                ticket.TenantId,
                ticket.LaboratoryNameEn,
                ticket.LaboratoryNameAr,
                ticket.Subject,
                ticket.Priority,
                ticket.Status,
                ticket.OpenedByUserId,
                ticket.OpenedByName,
                ticket.OpenedAtUtc,
                ticket.LastActivityAtUtc,
                ReplyCount = ticket.Replies.Count,
                LastFromPlatform = ticket.Replies.OrderByDescending(reply => reply.AtUtc).Select(reply => (bool?)reply.FromPlatform).FirstOrDefault(),
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var items = rows.Select(row => new SupportTicketListItem(
            row.Id, row.Number, row.TenantId, row.LaboratoryNameEn, row.LaboratoryNameAr, row.Subject, row.Priority.ToString(), row.Status.ToString(),
            row.OpenedByUserId, row.OpenedByName, row.OpenedAtUtc, row.LastActivityAtUtc, row.ReplyCount, row.LastFromPlatform ?? false)).ToList();

        return new Paged<SupportTicketListItem>(items, page.Page, page.PageSize, total);
    }

    public async Task<SupportTicketDetail?> GetDetailAsync(Guid id, CancellationToken cancellationToken)
    {
        var ticket = await Scoped().AsNoTracking().FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken).ConfigureAwait(false);
        if (ticket is null)
        {
            return null;
        }

        var replies = ticket.Replies
            .OrderBy(reply => reply.AtUtc)
            .ThenBy(reply => reply.Id)
            .Select(reply => new SupportTicketReplyView(reply.Id, reply.AuthorUserId, reply.AuthorName, reply.Text, reply.FromPlatform, reply.AtUtc))
            .ToList();

        return new SupportTicketDetail(
            ticket.Id, ticket.Number, ticket.TenantId, ticket.LaboratoryNameEn, ticket.LaboratoryNameAr, ticket.Subject, ticket.Description,
            ticket.Priority.ToString(), ticket.Status.ToString(), ticket.OpenedByUserId, ticket.OpenedByName, ticket.OpenedAtUtc, ticket.LastActivityAtUtc,
            ticket.ResolvedAtUtc, ticket.ClosedAtUtc, replies);
    }

    /// <summary>INV-SUP-004 in one place: the laboratory's own tickets, every ticket for the platform tenant, none when the tenant is unresolved.</summary>
    private IQueryable<SupportTicket> Scoped()
    {
        if (tenant.TenantId is not { } current)
        {
            return Set.Where(_ => false);
        }

        return TenantIds.IsPlatform(current) ? Set : Set.Where(ticket => ticket.TenantId == current);
    }

    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal);
}
