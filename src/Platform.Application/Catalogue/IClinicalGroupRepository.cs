using Platform.Application.Abstractions.Persistence;
using Platform.Contracts.Catalogue;
using Platform.Contracts.Common;
using Platform.Domain.Catalogue;

namespace Platform.Application.Catalogue;

/// <summary>Write side and registers of <see cref="ClinicalGroup"/>; every read is bounded to the current tenant.</summary>
public interface IClinicalGroupRepository : IRepository<ClinicalGroup>
{
    Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken);

    /// <summary><see langword="null"/> when the group does not exist in the tenant, else whether it is active.</summary>
    Task<bool?> ActiveStateAsync(Guid id, CancellationToken cancellationToken);

    Task<Paged<ClinicalGroupListItem>> ListAsync(CatalogueAListFilter filter, PageRequest page, CancellationToken cancellationToken);

    Task<IReadOnlyList<ClinicalGroupListItem>> ExportAsync(CatalogueAListFilter filter, CancellationToken cancellationToken);
}
