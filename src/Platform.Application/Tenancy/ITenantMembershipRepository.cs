using Platform.Application.Abstractions.Persistence;
using Platform.Domain.Tenancy;

namespace Platform.Application.Tenancy;

/// <summary>Write-side port of <see cref="TenantMembership"/>. Every query runs inside the current tenant (filter + RLS).</summary>
public interface ITenantMembershipRepository : IRepository<TenantMembership>
{
    Task<TenantMembership?> FindByPlatformUserAsync(Guid platformUserId, CancellationToken cancellationToken);

    Task<bool> EmployeeCodeExistsAsync(string employeeCode, CancellationToken cancellationToken);

    /// <summary>INV-ORG-005: the members (active or not) holding the role.</summary>
    Task<int> CountByRoleAsync(Guid roleId, CancellationToken cancellationToken);

    /// <summary>INV-ORG-010: the role is held by the tenant's primary administrator.</summary>
    Task<bool> PrimaryAdministratorHoldsRoleAsync(Guid roleId, CancellationToken cancellationToken);

    /// <summary>The current tenant already has its (single) primary administrator.</summary>
    Task<bool> PrimaryAdministratorExistsAsync(CancellationToken cancellationToken);
}
