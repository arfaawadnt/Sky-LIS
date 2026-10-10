using Platform.Application.Abstractions.Persistence;
using Platform.Contracts.Catalogue;
using Platform.Contracts.Common;
using Platform.Domain.Catalogue;

namespace Platform.Application.Catalogue;

public interface IMeasurementUnitRepository : IRepository<MeasurementUnit>
{
    /// <summary>INV-CAT-002 on the upper-cased name, ignoring the unit being renamed.</summary>
    Task<bool> NameExistsAsync(string nameKey, Guid? exceptId, CancellationToken cancellationToken);

    Task<bool?> ActiveStateAsync(Guid id, CancellationToken cancellationToken);

    Task<Paged<MeasurementUnitListItem>> ListAsync(CatalogueAListFilter filter, PageRequest page, CancellationToken cancellationToken);

    Task<IReadOnlyList<MeasurementUnitListItem>> ExportAsync(CatalogueAListFilter filter, CancellationToken cancellationToken);
}

public interface IPatientConditionRepository : IRepository<PatientCondition>
{
    /// <summary>INV-CAT-002 on the upper-cased English name, ignoring the condition being renamed.</summary>
    Task<bool> NameExistsAsync(string nameKey, Guid? exceptId, CancellationToken cancellationToken);

    /// <summary>The ids among <paramref name="ids"/> that exist in the tenant, with their active flag.</summary>
    Task<IReadOnlyDictionary<Guid, bool>> ActiveStatesAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);

    Task<Paged<PatientConditionListItem>> ListAsync(CatalogueAListFilter filter, PageRequest page, CancellationToken cancellationToken);

    Task<IReadOnlyList<PatientConditionListItem>> ExportAsync(CatalogueAListFilter filter, CancellationToken cancellationToken);
}
