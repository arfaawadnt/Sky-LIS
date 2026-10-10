using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NpgsqlTypes;
using Platform.Application.Abstractions.Notifications;
using Platform.Application.PlatformAdmin;
using Platform.Contracts.PlatformAdmin;
using Platform.Domain.Identity;
using Platform.Domain.PlatformAdmin;
using Platform.Infrastructure.Notifications;
using Platform.SharedKernel.Abstractions;
using Platform.SharedKernel.MultiTenancy;
using DomainTemplate = Platform.Domain.PlatformAdmin.NotificationTemplate;
using RenderedTemplate = Platform.Application.Abstractions.Notifications.NotificationTemplate;

namespace Platform.Infrastructure.Persistence.PlatformAdmin;

// Read ports and composites of platform administration, registered by convention (MODULE-PATTERN §2).

/// <summary>
/// SA-20 templates for every sender (replaces the foundation's in-code default by convention): the active template of
/// the code in the asked language, SRS aliases rewritten (<c>{lab}</c> → <c>{labName}</c>); the built-in text when the
/// code has no active template. Platform rows: readable from any tenant scope and from anonymous flows.
/// </summary>
internal sealed class PlatformNotificationTemplateSource(AppDbContext context) : INotificationTemplateSource
{
    private static readonly DefaultNotificationTemplateSource BuiltIn = new();

    public async Task<RenderedTemplate?> FindAsync(string code, string language, CancellationToken cancellationToken)
    {
        var key = (code ?? string.Empty).Trim().ToLowerInvariant();
        var template = await context.Set<DomainTemplate>().AsNoTracking()
            .Where(row => row.Code == key && row.IsActive)
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (template is null)
        {
            return await BuiltIn.FindAsync(code ?? string.Empty, language, cancellationToken).ConfigureAwait(false);
        }

        var arabic = language is not null && language.StartsWith("ar", StringComparison.OrdinalIgnoreCase);
        return new RenderedTemplate(
            template.Code,
            arabic ? "ar" : "en",
            arabic ? template.SubjectAr ?? template.SubjectEn : template.SubjectEn,
            DomainTemplate.Placeholders.ToCanonical(arabic ? template.TextAr : template.TextEn));
    }
}

/// <summary>INV-PLT-003 for sign-in and principal resolution: the platform tenant and unknown tenants are active; a client is active while usable.</summary>
internal sealed class TenantStatusLookup(ITenantRepository tenants, IClock clock) : ITenantStatusLookup
{
    public async Task<bool> IsActiveAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        if (TenantIds.IsPlatform(tenantId))
        {
            return true;
        }

        var status = await tenants.FindStatusAsync(tenantId, cancellationToken).ConfigureAwait(false);
        return status is not { } found
               || found.Status == TenantStatus.Active
               || (found.Status == TenantStatus.Trial && (found.TrialEndsAtUtc is null || found.TrialEndsAtUtc > clock.UtcNow));
    }
}

/// <summary><c>Lis:Platform:PortalDomain</c> (default <c>skylis.app</c>), read lazily so a test host's configuration applies.</summary>
internal sealed class PortalAddressOptions(IServiceProvider services) : IPortalAddressOptions
{
    public const string ConfigurationKey = "Lis:Platform:PortalDomain";

    public string PortalDomain
    {
        get
        {
            var configured = services.GetService<IConfiguration>()?[ConfigurationKey];
            return string.IsNullOrWhiteSpace(configured) ? Tenant.DefaultPortalDomain : configured.Trim().Trim('.');
        }
    }
}

/// <summary>
/// SA-04 counts across tenants without elevation (assumption Q8): <c>saas.client_usage_counts(uuid[])</c> is a
/// <c>SECURITY DEFINER</c> function owned by <c>lis_owner</c> that returns only counts, and only when the session's
/// tenant is the platform tenant (module SQL). The connection is opened through EF so the tenant stamp applies.
/// </summary>
internal sealed class ClientUsageCounts(AppDbContext context) : IClientUsageCounts
{
    public const string Sql = "SELECT tenant_id, branches, users FROM saas.client_usage_counts(@ids)";

    public async Task<IReadOnlyDictionary<Guid, ClientUsage>> GetAsync(IReadOnlyCollection<Guid> tenantIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tenantIds);
        var result = new Dictionary<Guid, ClientUsage>();
        if (tenantIds.Count == 0)
        {
            return result;
        }

        var database = context.Database;
        await database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var command = database.GetDbConnection().CreateCommand();
            command.CommandText = Sql;
            command.Transaction = database.CurrentTransaction?.GetDbTransaction();
            command.Parameters.Add(new NpgsqlParameter("ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = tenantIds.Distinct().ToArray() });
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                result[reader.GetGuid(0)] = new ClientUsage(checked((int)reader.GetInt64(1)), checked((int)reader.GetInt64(2)));
            }
        }
        finally
        {
            await database.CloseConnectionAsync().ConfigureAwait(false);
        }

        return result;
    }
}

/// <summary>Placeholder until the registration/reporting modules exist: no activity yet (SA-03 shows zeros).</summary>
[FallbackAdapter]
internal sealed class ZeroClientActivityReader : IClientActivityReader
{
    public Task<ClientActivitySnapshot> ReadAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken) => Task.FromResult(ClientActivitySnapshot.Empty);
}

internal sealed class PlatformDashboardReader(AppDbContext context) : IPlatformDashboardReader
{
    public async Task<ClientCounts> CountClientsAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        var groups = await context.Set<Tenant>().AsNoTracking()
            .GroupBy(tenant => new { tenant.Status, tenant.Source })
            .Select(group => new { group.Key.Status, group.Key.Source, Count = group.Count() })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        int Sum(Func<TenantStatus, TenantSource, bool> predicate) => groups.Where(group => predicate(group.Status, group.Source)).Sum(group => group.Count);
        return new ClientCounts(
            Sum((_, _) => true),
            Sum((status, _) => status == TenantStatus.Active),
            Sum((status, _) => status == TenantStatus.Inactive),
            Sum((status, _) => status == TenantStatus.Trial),
            Sum((_, source) => source == TenantSource.Subscription),
            Sum((_, source) => source == TenantSource.Manual));
    }

    /// <summary>The query filter and RLS bound this to the platform tenant, the scope of every dashboard request.</summary>
    public Task<int> CountAdminUsersAsync(CancellationToken cancellationToken) =>
        context.TenantMemberships.AsNoTracking().CountAsync(membership => membership.TenantId == TenantIds.Platform && membership.IsActive, cancellationToken);

    public Task<int> CountWalletsAtOrBelowZeroAsync(CancellationToken cancellationToken) =>
        context.Set<Wallet>().AsNoTracking().CountAsync(wallet => wallet.Balance <= 0, cancellationToken);

    public async Task<IReadOnlyList<PlanShare>> PlanDistributionAsync(CancellationToken cancellationToken) =>
        await context.Set<Plan>().AsNoTracking().OrderBy(plan => plan.MonthlyFee).ThenBy(plan => plan.Code)
            .Select(plan => new PlanShare(plan.Id, plan.Code, plan.Name.En, plan.Name.Ar, context.Set<Tenant>().Count(tenant => tenant.PlanId == plan.Id)))
            .ToListAsync(cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<RecentClient>> RecentClientsAsync(int take, string portalDomain, CancellationToken cancellationToken)
    {
        var rows = await (
            from tenant in context.Set<Tenant>().AsNoTracking().OrderByDescending(tenant => tenant.CreatedAtUtc).Take(take)
            join country in context.Set<Country>().AsNoTracking() on tenant.CountryId equals country.Id
            join plan in context.Set<Plan>().AsNoTracking() on tenant.PlanId equals plan.Id
            from wallet in context.Set<Wallet>().AsNoTracking().Where(wallet => wallet.TenantId == tenant.Id).DefaultIfEmpty()
            select new
            {
                tenant.Id,
                NameEn = tenant.Name.En,
                NameAr = tenant.Name.Ar,
                tenant.PortalSlug,
                CountryEn = country.Name.En,
                CountryAr = country.Name.Ar,
                plan.Code,
                Balance = wallet == null ? 0m : wallet.Balance,
                Currency = wallet == null ? country.CurrencyCode : wallet.CurrencyCode,
                tenant.CreatedAtUtc,
                tenant.Source,
                tenant.Status,
            }).ToListAsync(cancellationToken).ConfigureAwait(false);

        return rows.OrderByDescending(row => row.CreatedAtUtc)
            .Select(row => new RecentClient(row.Id, row.NameEn, row.NameAr, Tenant.BuildPortalUrl(row.PortalSlug, portalDomain), row.CountryEn, row.CountryAr, row.Code, row.Balance, row.Currency.Trim(), row.CreatedAtUtc, row.Source.ToString(), row.Status.ToString()))
            .ToList();
    }

    public async Task<IReadOnlyDictionary<Guid, (string NameEn, string NameAr)>> ClientNamesAsync(IReadOnlyCollection<Guid> tenantIds, CancellationToken cancellationToken)
    {
        var ids = tenantIds.ToList();
        var rows = await context.Set<Tenant>().AsNoTracking().Where(tenant => ids.Contains(tenant.Id))
            .Select(tenant => new { tenant.Id, tenant.Name.En, tenant.Name.Ar })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.ToDictionary(row => row.Id, row => (row.En, row.Ar));
    }
}

/// <summary>Live refresh sessions by tenant (identity rows have no RLS); "since" is the start of the rotation family.</summary>
internal sealed class PlatformSessionDirectory(AppDbContext context) : IPlatformSessionDirectory
{
    public async Task<IReadOnlyList<AdminSessionRow>> ListLiveAsync(Guid tenantId, DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        var rows = await (
            from session in Live(tenantId, nowUtc).AsNoTracking()
            join user in context.PlatformUsers.AsNoTracking() on session.PlatformUserId equals user.Id
            select new
            {
                session.Id,
                session.FamilyId,
                session.PlatformUserId,
                user.DisplayName,
                user.Username,
                session.IpAddress,
                Since = context.RefreshSessions.Where(member => member.FamilyId == session.FamilyId).Min(member => member.IssuedAtUtc),
                session.LastSeenAtUtc,
                session.ExpiresAtUtc,
            }).ToListAsync(cancellationToken).ConfigureAwait(false);

        return rows.OrderByDescending(row => row.LastSeenAtUtc)
            .Select(row => new AdminSessionRow(row.Id, row.FamilyId, row.PlatformUserId, row.DisplayName, row.Username, row.IpAddress?.ToString(), row.Since, row.LastSeenAtUtc, row.ExpiresAtUtc))
            .ToList();
    }

    public async Task<IReadOnlyList<RefreshSession>> ListLiveForUpdateAsync(Guid tenantId, DateTimeOffset nowUtc, CancellationToken cancellationToken) =>
        await Live(tenantId, nowUtc).ToListAsync(cancellationToken).ConfigureAwait(false);

    private IQueryable<RefreshSession> Live(Guid tenantId, DateTimeOffset nowUtc) =>
        context.RefreshSessions.Where(session =>
            session.TenantId == tenantId
            && !session.IsRevoked
            && session.RotatedAtUtc == null
            && session.ExpiresAtUtc > nowUtc
            && session.AbsoluteExpiresAtUtc > nowUtc);
}
