using Microsoft.EntityFrameworkCore;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.PlatformAdmin;
using Platform.Contracts.Common;
using Platform.SharedKernel.MultiTenancy;

namespace Platform.Infrastructure.Persistence.PlatformAdmin;

/// <summary>
/// SA-08b (change request LIS-CR-003 §2): the client users' browser sessions grouped by rotation family. The grouping is
/// one SQL statement over <c>identity.refresh_sessions</c> (no RLS; read by the platform tenant only — the handler's
/// PLT-403 check) joined to the user and, when the tenant has a client record, to <c>saas.tenants</c>; the filters, the
/// order and the page are composed over it by EF, so a sort covers the whole history before paging (B-03). Rules of the
/// grouping: login = the first issue; last seen = the latest activity of any member; sign-out = the revocation with
/// reason Logout (status SignedOut); else the first other revocation (Revoked, with the reason); else the latest sliding
/// expiry when it is past (Expired); else Active. Duration = (ended ?? last seen) − login in whole minutes, never
/// negative. The address and the agent are the first member's (the sign-in request).
/// </summary>
internal sealed class ClientLoginLogReader(AppDbContext context) : IClientLoginLogReader
{
    /// <summary>{0} = now (UTC). Composable: EF wraps it as a sub-query for the filters, the order and the page.</summary>
    public const string Sql = """
        SELECT f.family_id,
               f.tenant_id,
               t.name_en AS client_name_en,
               t.name_ar AS client_name_ar,
               f.platform_user_id AS user_id,
               u.username,
               u.display_name,
               f.login_at_utc,
               f.ended_at_utc,
               f.last_seen_at_utc,
               f.ip_address,
               f.user_agent,
               f.duration_minutes,
               f.status,
               f.revocation_reason
        FROM (
            SELECT g.family_id, g.tenant_id, g.platform_user_id, g.login_at_utc, g.last_seen_at_utc, g.ip_address, g.user_agent, g.revocation_reason,
                   COALESCE(g.signed_out_at_utc, g.revoked_at_utc, CASE WHEN g.expires_at_utc < {0} THEN g.expires_at_utc END) AS ended_at_utc,
                   CASE WHEN g.signed_out_at_utc IS NOT NULL THEN 'SignedOut'
                        WHEN g.revoked_at_utc IS NOT NULL THEN 'Revoked'
                        WHEN g.expires_at_utc < {0} THEN 'Expired'
                        ELSE 'Active' END AS status,
                   GREATEST(0, FLOOR(EXTRACT(EPOCH FROM (COALESCE(g.signed_out_at_utc, g.revoked_at_utc, CASE WHEN g.expires_at_utc < {0} THEN g.expires_at_utc END, g.last_seen_at_utc) - g.login_at_utc)) / 60))::int AS duration_minutes
            FROM (
                SELECT s.family_id,
                       s.tenant_id,
                       s.platform_user_id,
                       MIN(s.issued_at_utc) AS login_at_utc,
                       MAX(s.last_seen_at_utc) AS last_seen_at_utc,
                       MAX(s.expires_at_utc) AS expires_at_utc,
                       MIN(CASE WHEN s.is_revoked AND s.revocation_reason = 'Logout' THEN s.revoked_at_utc END) AS signed_out_at_utc,
                       MIN(CASE WHEN s.is_revoked AND s.revocation_reason <> 'Logout' THEN s.revoked_at_utc END) AS revoked_at_utc,
                       MIN(CASE WHEN s.is_revoked AND s.revocation_reason <> 'Logout' THEN s.revocation_reason END) AS revocation_reason,
                       host((array_agg(s.ip_address ORDER BY s.issued_at_utc, s.id))[1]) AS ip_address,
                       (array_agg(s.user_agent ORDER BY s.issued_at_utc, s.id))[1] AS user_agent
                FROM identity.refresh_sessions s
                WHERE s.kind = 'Browser' AND s.tenant_id IS NOT NULL
                GROUP BY s.family_id, s.tenant_id, s.platform_user_id
            ) g
        ) f
        JOIN identity.platform_users u ON u.id = f.platform_user_id
        LEFT JOIN saas.tenants t ON t.id = f.tenant_id
        """;

    private static readonly SortColumns<LogRow> Sorts = new SortColumns<LogRow>(PlatformAdminSortKeys.ClientLoginLogs)
        .Column("login", row => row.LoginAtUtc)
        .Column("logout", row => row.EndedAtUtc)
        .Column("client", row => row.ClientNameEn)
        .Column("clientAr", row => row.ClientNameAr)
        .Column("user", row => row.DisplayName)
        .Column("duration", row => row.DurationMinutes)
        .Column("status", row => row.Status)
        .ThenBy(row => row.LoginAtUtc)
        .ThenBy(row => row.FamilyId);

    public async Task<Paged<ClientLoginLogRow>> ListAsync(ClientLoginLogFilter filter, ListSort sort, PageRequest page, DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var query = Filtered(filter, nowUtc);
        var total = await query.LongCountAsync(cancellationToken).ConfigureAwait(false);
        var rows = await Ordered(query, sort).Skip(page.Skip).Take(page.PageSize).ToListAsync(cancellationToken).ConfigureAwait(false);
        return new Paged<ClientLoginLogRow>(rows.Select(ToRow).ToList(), page.Page, page.PageSize, total);
    }

    public async Task<IReadOnlyList<ClientLoginLogRow>> ListAllAsync(ClientLoginLogFilter filter, ListSort sort, int maxRows, DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentOutOfRangeException.ThrowIfNegative(maxRows);
        var rows = await Ordered(Filtered(filter, nowUtc), sort).Take(maxRows + 1).ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.Select(ToRow).ToList();
    }

    private IQueryable<LogRow> Filtered(ClientLoginLogFilter filter, DateTimeOffset nowUtc)
    {
        var query = context.Database.SqlQueryRaw<LogRow>(Sql, nowUtc);
        query = filter.TenantId is { } tenantId
            ? query.Where(row => row.TenantId == tenantId)
            : query.Where(row => row.TenantId != TenantIds.Platform);   // the administrators' own sessions are SA-08's list

        if (filter.Search is { Length: > 0 } search)
        {
            var pattern = Like.Contains(search);
            query = query.Where(row => EF.Functions.ILike(row.Username, pattern, Like.Escape) || EF.Functions.ILike(row.DisplayName, pattern, Like.Escape));
        }

        if (filter.LoginFromUtc is { } loginFrom)
        {
            query = query.Where(row => row.LoginAtUtc >= loginFrom);
        }

        if (filter.LoginToUtc is { } loginTo)
        {
            query = query.Where(row => row.LoginAtUtc <= loginTo);
        }

        if (filter.LogoutFromUtc is { } logoutFrom)
        {
            query = query.Where(row => row.EndedAtUtc != null && row.EndedAtUtc >= logoutFrom);
        }

        if (filter.LogoutToUtc is { } logoutTo)
        {
            query = query.Where(row => row.EndedAtUtc != null && row.EndedAtUtc <= logoutTo);
        }

        if (filter.MinMinutes is { } min)
        {
            query = query.Where(row => row.DurationMinutes >= min);
        }

        if (filter.MaxMinutes is { } max)
        {
            query = query.Where(row => row.DurationMinutes <= max);
        }

        if (filter.Status is { Length: > 0 } status)
        {
            query = query.Where(row => row.Status == status);
        }

        return query;
    }

    private static IOrderedQueryable<LogRow> Ordered(IQueryable<LogRow> query, ListSort sort) =>
        Sorts.Apply(query, sort, rows => rows.OrderByDescending(row => row.LoginAtUtc).ThenBy(row => row.FamilyId));

    private static ClientLoginLogRow ToRow(LogRow row) =>
        new(row.FamilyId, row.TenantId, row.ClientNameEn, row.ClientNameAr, row.UserId, row.Username, row.DisplayName, row.LoginAtUtc, row.EndedAtUtc, row.LastSeenAtUtc, row.IpAddress, row.UserAgent, row.DurationMinutes, row.Status, row.RevocationReason);

    /// <summary>The columns of <see cref="Sql"/> (snake_case of the property names, as the naming convention maps an ad hoc type).</summary>
    private sealed class LogRow
    {
        public Guid FamilyId { get; set; }

        public Guid TenantId { get; set; }

        public string? ClientNameEn { get; set; }

        public string? ClientNameAr { get; set; }

        public Guid UserId { get; set; }

        public string Username { get; set; } = string.Empty;

        public string DisplayName { get; set; } = string.Empty;

        public DateTimeOffset LoginAtUtc { get; set; }

        public DateTimeOffset? EndedAtUtc { get; set; }

        public DateTimeOffset LastSeenAtUtc { get; set; }

        public string? IpAddress { get; set; }

        public string? UserAgent { get; set; }

        public int DurationMinutes { get; set; }

        public string Status { get; set; } = string.Empty;

        public string? RevocationReason { get; set; }
    }
}
