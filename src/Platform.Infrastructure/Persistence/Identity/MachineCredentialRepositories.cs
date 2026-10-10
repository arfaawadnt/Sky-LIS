using Microsoft.EntityFrameworkCore;
using Platform.Application.Abstractions.Security;
using Platform.Application.Identity.MachineCredentials;
using Platform.Contracts.Common;
using Platform.Contracts.Identity;
using Platform.Domain.Identity;
using Platform.Domain.Organization;
using Platform.Domain.Tenancy;
using Platform.SharedKernel.MultiTenancy;
using Platform.SharedKernel.Primitives;

namespace Platform.Infrastructure.Persistence.Identity;

/// <summary>
/// <see cref="IMachineCredentialRepository"/>: the table has no row-level security, so every read except the two
/// pre-authentication lookups (client id, service user) is bounded to a tenant here, including the inherited
/// <see cref="GetForUpdateAsync"/> (the scope's tenant).
/// </summary>
internal sealed class MachineCredentialRepository(AppDbContext context) : Repository<MachineCredential>(context), IMachineCredentialRepository
{
    public override Task<MachineCredential?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken)
    {
        var tenantId = Context.CurrentTenantIdOrNil;
        return Set.FirstOrDefaultAsync(credential => credential.Id == id && credential.TenantId == tenantId, cancellationToken);
    }

    public Task<MachineCredential?> FindByClientIdAsync(string clientId, CancellationToken cancellationToken) =>
        Set.FirstOrDefaultAsync(credential => credential.ClientId == clientId, cancellationToken);

    public Task<MachineCredential?> FindByServiceUserAsync(Guid serviceUserId, CancellationToken cancellationToken) =>
        Set.FirstOrDefaultAsync(credential => credential.ServiceUserId == serviceUserId, cancellationToken);

    public Task<MachineCredential?> GetInTenantAsync(Guid id, Guid tenantId, CancellationToken cancellationToken) =>
        Set.FirstOrDefaultAsync(credential => credential.Id == id && credential.TenantId == tenantId, cancellationToken);

    public Task<bool> LiveNameExistsAsync(Guid tenantId, string name, CancellationToken cancellationToken)
    {
        var lowered = (name ?? string.Empty).Trim().ToLowerInvariant();
        return Set.AnyAsync(credential => credential.TenantId == tenantId && credential.RevokedAtUtc == null && credential.Name.ToLower() == lowered, cancellationToken);
    }

    public async Task<Paged<MachineCredentialListItem>> ListAsync(Guid tenantId, bool includeRevoked, DateTimeOffset nowUtc, PageRequest page, CancellationToken cancellationToken)
    {
        var query = Set.AsNoTracking().Where(credential => credential.TenantId == tenantId);
        if (!includeRevoked)
        {
            query = query.Where(credential => credential.RevokedAtUtc == null);
        }

        var total = await query.LongCountAsync(cancellationToken).ConfigureAwait(false);
        var rows = await query.OrderBy(credential => credential.Name).ThenBy(credential => credential.Id)
            .Skip(page.Skip)
            .Take(page.PageSize)
            .Select(credential => new
            {
                credential.Id,
                credential.Name,
                credential.ClientId,
                credential.Scopes,
                credential.BranchId,
                credential.CreatedAtUtc,
                credential.ExpiresAtUtc,
                credential.LastUsedAtUtc,
                credential.SecretRotatedAtUtc,
                credential.RevokedAtUtc,
                credential.RevocationReason,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var items = rows.Select(row => new MachineCredentialListItem(
            row.Id,
            row.Name,
            row.ClientId,
            row.Scopes,
            row.BranchId,
            StatusOf(row.RevokedAtUtc, row.ExpiresAtUtc, nowUtc),
            row.CreatedAtUtc,
            row.ExpiresAtUtc,
            row.LastUsedAtUtc,
            row.SecretRotatedAtUtc,
            row.RevokedAtUtc,
            row.RevocationReason)).ToList();

        return new Paged<MachineCredentialListItem>(items, page.Page, page.PageSize, total);
    }

    public async Task<IReadOnlyDictionary<Guid, MachineCredentialSummary>> SummariesAsync(Guid tenantId, IReadOnlyCollection<Guid> credentialIds, DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(credentialIds);
        var ids = credentialIds.Distinct().ToArray();
        if (ids.Length == 0)
        {
            return new Dictionary<Guid, MachineCredentialSummary>();
        }

        var rows = await Set.AsNoTracking()
            .Where(credential => credential.TenantId == tenantId && ids.Contains(credential.Id))
            .Select(credential => new { credential.Id, credential.ClientId, credential.ExpiresAtUtc, credential.RevokedAtUtc })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return rows.ToDictionary(row => row.Id, row => new MachineCredentialSummary(row.Id, row.ClientId, row.ExpiresAtUtc, StatusOf(row.RevokedAtUtc, row.ExpiresAtUtc, nowUtc)));
    }

    /// <summary>Live, Expired or Revoked (revocation wins).</summary>
    public static string StatusOf(DateTimeOffset? revokedAtUtc, DateTimeOffset expiresAtUtc, DateTimeOffset nowUtc) =>
        revokedAtUtc is not null ? "Revoked" : expiresAtUtc <= nowUtc ? "Expired" : "Live";
}

/// <summary>
/// <see cref="IMachineServiceIdentityStore"/>: stages the service user, its role and its membership. The role is stamped
/// with the scope's tenant as it is added — exactly what <c>TenantStampInterceptor</c> does at commit — so the
/// membership can be assigned the role in the same commit (<see cref="TenantMembership.AssignRole"/> compares tenants).
/// </summary>
internal sealed class MachineServiceIdentityStore(AppDbContext context, ITenantContextAccessor tenant) : IMachineServiceIdentityStore
{
    public void Add(PlatformUser serviceUser, Role role, TenantMembership membership)
    {
        ArgumentNullException.ThrowIfNull(serviceUser);
        ArgumentNullException.ThrowIfNull(role);
        ArgumentNullException.ThrowIfNull(membership);

        var tenantId = tenant.CurrentTenantId
            ?? throw new DomainException(ProblemCodes.TenantUnresolved, "No tenant is resolved for this scope; the service identity cannot be attributed.", DomainFault.Refused);
        if (membership.TenantId != tenantId)
        {
            throw new DomainException(ProblemCodes.TenantForbidden, "The service identity belongs to another tenant.", DomainFault.Refused);
        }

        context.Set<PlatformUser>().Add(serviceUser);
        var entry = context.Set<Role>().Add(role);
        if (role.TenantId == Guid.Empty)
        {
            entry.Property(nameof(ITenantScoped.TenantId)).CurrentValue = tenantId;
        }

        context.Set<TenantMembership>().Add(membership);
    }
}
