using Platform.Application.Abstractions.Persistence;
using Platform.Contracts.Common;
using Platform.Contracts.Organization;
using Platform.Domain.Organization;

namespace Platform.Application.Organization;

public sealed record RoleFilter(string? Search, bool? IsActive);

/// <summary>Write side of <see cref="Role"/> plus the projections of the Roles page (CP-37, SA-06). Every read is bounded to the current tenant.</summary>
public interface IRoleRepository : IRepository<Role>
{
    Task<Role?> FindByNameAsync(string nameEn, CancellationToken cancellationToken);

    /// <summary>INV-ORG-006: a role with this English name (case-insensitive) exists, other than <paramref name="excludingRoleId"/>.</summary>
    Task<bool> NameExistsAsync(string nameEn, Guid? excludingRoleId, CancellationToken cancellationToken);

    Task<Paged<RoleListItem>> ListAsync(RoleFilter filter, PageRequest page, CancellationToken cancellationToken);

    Task<RoleDetail?> GetDetailAsync(Guid id, CancellationToken cancellationToken);
}

public sealed record BranchFilter(string? Search, bool? IsActive);

/// <summary>Write side of <see cref="Branch"/> plus the projections of the Branches page (CP-36).</summary>
public interface IBranchRepository : IRepository<Branch>
{
    Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken);

    Task<Branch?> FindByCodeAsync(string code, CancellationToken cancellationToken);

    /// <summary>INV-ORG-002: the active branches of the tenant.</summary>
    Task<int> CountActiveAsync(CancellationToken cancellationToken);

    Task<Paged<BranchListItem>> ListAsync(BranchFilter filter, PageRequest page, CancellationToken cancellationToken);

    Task<BranchListItem?> GetListItemAsync(Guid id, CancellationToken cancellationToken);
}
