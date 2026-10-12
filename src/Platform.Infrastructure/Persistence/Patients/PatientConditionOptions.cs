using Microsoft.EntityFrameworkCore;
using Platform.Application.Patients;
using Platform.Contracts.Patients;
using Platform.Domain.Catalogue;

namespace Platform.Infrastructure.Persistence.Patients;

/// <summary>Adapter of <see cref="IPatientConditionOptions"/>: the active rows of <c>app.patient_conditions</c> of the current tenant (query filter and RLS).</summary>
internal sealed class PatientConditionOptions(AppDbContext context) : IPatientConditionOptions
{
    /// <summary>A catalogue list, not a register: bounded all the same.</summary>
    internal const int MaxRows = 500;

    public async Task<IReadOnlyList<PatientConditionOption>> ListActiveAsync(CancellationToken cancellationToken) =>
        await context.Set<PatientCondition>().AsNoTracking()
            .Where(condition => condition.IsActive)
            .OrderBy(condition => condition.NameKey)
            .Take(MaxRows)
            .Select(condition => new PatientConditionOption(condition.Id, condition.Name.En, condition.Name.Ar))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
}
