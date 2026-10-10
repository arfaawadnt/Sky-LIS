using Microsoft.EntityFrameworkCore;
using Platform.Application.Tenancy;
using Platform.Domain.Tenancy;

namespace Platform.Infrastructure.Persistence.Tenancy;

/// <summary>Every query here is bounded to the current tenant by the query filter and by row-level security.</summary>
internal sealed class TenantMembershipRepository(AppDbContext context) : Repository<TenantMembership>(context), ITenantMembershipRepository
{
    public Task<TenantMembership?> FindByPlatformUserAsync(Guid platformUserId, CancellationToken cancellationToken) =>
        Set.FirstOrDefaultAsync(membership => membership.PlatformUserId == platformUserId, cancellationToken);

    public Task<bool> EmployeeCodeExistsAsync(string employeeCode, CancellationToken cancellationToken)
    {
        var trimmed = (employeeCode ?? string.Empty).Trim();
        return Set.AnyAsync(membership => membership.EmployeeCode == trimmed, cancellationToken);
    }

    public Task<int> CountByRoleAsync(Guid roleId, CancellationToken cancellationToken) =>
        Set.CountAsync(membership => membership.RoleId == roleId, cancellationToken);

    public Task<bool> PrimaryAdministratorHoldsRoleAsync(Guid roleId, CancellationToken cancellationToken) =>
        Set.AnyAsync(membership => membership.RoleId == roleId && membership.IsPrimaryAdministrator, cancellationToken);

    public Task<bool> PrimaryAdministratorExistsAsync(CancellationToken cancellationToken) =>
        Set.AnyAsync(membership => membership.IsPrimaryAdministrator, cancellationToken);
}
