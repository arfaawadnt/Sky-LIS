using Platform.Domain.Catalogue;
using Platform.SharedKernel.Primitives;

namespace Platform.Application.Catalogue;

/// <summary>
/// INV-CAT-010 / BR-24 for the handlers of catalogue half A: a referenced row must exist in the tenant, and it must be
/// active when it is newly selected (a reference kept from before may point at a row deactivated since; existing
/// records keep working, BR-15).
/// </summary>
internal static class CatalogueAReferences
{
    public static async Task RequireClinicalGroupAsync(IClinicalGroupRepository groups, Guid? selected, Guid? current, CancellationToken cancellationToken)
    {
        if (selected is not { } id || id == Guid.Empty)
        {
            return;
        }

        var state = await groups.ActiveStateAsync(id, cancellationToken);
        if (state is null || (state == false && id != current))
        {
            throw Unavailable("clinical group");
        }
    }

    public static async Task RequireUnitAsync(IMeasurementUnitRepository units, Guid? selected, Guid? current, CancellationToken cancellationToken)
    {
        if (selected is not { } id || id == Guid.Empty)
        {
            return;
        }

        var state = await units.ActiveStateAsync(id, cancellationToken);
        if (state is null || (state == false && id != current))
        {
            throw Unavailable("unit of measurement");
        }
    }

    /// <summary>The sample type exists (active when newly selected) and the condition belongs to it (CAT-030), active when newly selected.</summary>
    public static async Task RequireSampleAsync(
        ISampleTypeRepository sampleTypes,
        Guid? selectedType,
        Guid? selectedCondition,
        Guid? currentType,
        Guid? currentCondition,
        CancellationToken cancellationToken)
    {
        if (selectedType is not { } typeId || typeId == Guid.Empty)
        {
            return;
        }

        var sampleType = await sampleTypes.GetForUpdateAsync(typeId, cancellationToken);
        if (sampleType is null || (!sampleType.IsActive && typeId != currentType))
        {
            throw Unavailable("sample type");
        }

        if (selectedCondition is not { } conditionId || conditionId == Guid.Empty)
        {
            return;
        }

        if (!sampleType.HasCondition(conditionId, mustBeActive: false))
        {
            throw new DomainException(ServiceCodes.ConditionMismatch, "The sample condition does not belong to the sample type.", DomainFault.Validation);
        }

        if (!sampleType.HasCondition(conditionId, mustBeActive: true) && conditionId != currentCondition)
        {
            throw Unavailable("sample condition");
        }
    }

    public static async Task RequirePatientConditionsAsync(IPatientConditionRepository conditions, IReadOnlyCollection<Guid> selected, IReadOnlyCollection<Guid> current, CancellationToken cancellationToken)
    {
        var ids = selected.Where(id => id != Guid.Empty).Distinct().ToList();
        if (ids.Count == 0)
        {
            return;
        }

        var states = await conditions.ActiveStatesAsync(ids, cancellationToken);
        foreach (var id in ids)
        {
            if (!states.TryGetValue(id, out var active) || (!active && !current.Contains(id)))
            {
                throw Unavailable("patient condition");
            }
        }
    }

    public static DomainException Unavailable(string what) =>
        new(CatalogueACodes.ReferenceUnavailable, $"The selected {what} does not exist or is inactive (BR-24).", DomainFault.Validation);
}
