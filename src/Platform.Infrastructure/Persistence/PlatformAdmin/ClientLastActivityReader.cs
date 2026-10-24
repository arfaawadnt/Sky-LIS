using Microsoft.EntityFrameworkCore;
using Platform.Application.Abstractions.Security;
using Platform.Application.PlatformAdmin;
using Platform.Domain.Identity;
using Platform.Domain.PlatformAdmin;
using Platform.Domain.Registration;

namespace Platform.Infrastructure.Persistence.PlatformAdmin;

/// <summary>
/// LIS-CR-007 A-01, silent clients. The clients and their last browser sign-in come from the platform tables
/// (<c>saas.tenants</c>, <c>identity.refresh_sessions</c>: no RLS) in one grouped query each. The last registration of a
/// client is read in that client's own tenant scope (<see cref="ICurrentTenantSetter.ForTenant"/>, no elevation: the
/// pattern of the public laboratory card): RLS and the tenant filter bound the read to the client, and
/// <c>ix_visits_tenant_id_registered_at_utc</c> answers the <c>max</c>. Each <c>ForTenant</c> read opens its own
/// connection, so the session tenant is stamped for that client and set back for the next read.
/// </summary>
internal sealed class ClientLastActivityReader(AppDbContext context, ICurrentTenantSetter tenantScope) : IClientLastActivityReader
{
    public async Task<IReadOnlyList<ClientActivityRow>> ListClientsAsync(CancellationToken cancellationToken)
    {
        var clients = await context.Set<Tenant>().AsNoTracking()
            .Where(tenant => tenant.Status != TenantStatus.Inactive)
            .Select(tenant => new
            {
                tenant.Id,
                NameEn = tenant.Name.En,
                NameAr = tenant.Name.Ar,
                tenant.PortalSlug,
                tenant.Status,
                tenant.CreatedAtUtc,
                tenant.ManagerName,
                tenant.ManagerMobile,
            })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        if (clients.Count == 0)
        {
            return [];
        }

        var ids = clients.Select(client => (Guid?)client.Id).ToList();
        var signIns = await context.RefreshSessions.AsNoTracking()
            .Where(session => session.Kind == SessionKind.Browser && session.TenantId != null && ids.Contains(session.TenantId))
            .GroupBy(session => session.TenantId)
            .Select(group => new { TenantId = group.Key, Last = group.Max(session => session.IssuedAtUtc) })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var lastSignIn = signIns.Where(row => row.TenantId is not null).ToDictionary(row => row.TenantId!.Value, row => row.Last);

        return clients
            .Select(client => new ClientActivityRow(
                client.Id,
                client.NameEn,
                client.NameAr,
                client.PortalSlug,
                client.Status,
                client.CreatedAtUtc,
                client.ManagerName,
                client.ManagerMobile,
                lastSignIn.TryGetValue(client.Id, out var at) ? at : null))
            .ToList();
    }

    public async Task<IReadOnlyDictionary<Guid, DateTimeOffset>> LastRegistrationsAsync(IReadOnlyCollection<Guid> tenantIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tenantIds);
        var result = new Dictionary<Guid, DateTimeOffset>();
        foreach (var tenantId in tenantIds.Distinct())
        {
            DateTimeOffset? last;
            using (tenantScope.ForTenant(tenantId))
            {
                last = await context.Set<Visit>().AsNoTracking()
                    .Where(visit => visit.TenantId == tenantId)
                    .MaxAsync(visit => (DateTimeOffset?)visit.RegisteredAtUtc, cancellationToken)
                    .ConfigureAwait(false);
            }

            if (last is { } value)
            {
                result[tenantId] = value;
            }
        }

        return result;
    }
}
