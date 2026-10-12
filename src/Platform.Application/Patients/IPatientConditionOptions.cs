using Platform.Contracts.Patients;

namespace Platform.Application.Patients;

/// <summary>
/// Read port the Patients module needs from the catalogue (MODULES.md §1 "cross-module reads"; validation round 2 R2-04):
/// the patient conditions a file may carry, so the register, the file and the patient form show and offer them to a
/// patients reader without the catalogue right (<c>/api/v1/catalogue/patient-conditions</c> needs <c>catalogue.view</c>).
/// Active conditions of the current tenant, ordered by English name. The adapter is
/// <c>Platform.Infrastructure.Persistence.Patients.PatientConditionOptions</c>.
/// </summary>
public interface IPatientConditionOptions
{
    Task<IReadOnlyList<PatientConditionOption>> ListActiveAsync(CancellationToken cancellationToken);
}
