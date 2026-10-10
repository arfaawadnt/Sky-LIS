using Platform.Contracts.Patients;

namespace Platform.Application.Patients;

/// <summary>
/// Read port of the Patients module for other modules (MODULES.md §1 "cross-module reads"): Registration resolves the
/// file of a visit, Results reads sex, age and condition for reference ranges (BR-08) and the VIP indicator. The
/// adapter (<c>Platform.Infrastructure.Persistence.Patients.PatientLookup</c>) reads <c>app.patients</c> of the current
/// tenant only; the age is computed for the server's current UTC date. A Merged file is returned as it is, with
/// <see cref="PatientSummary.SurvivorPatientId"/> naming the active file its merge chain ends in, so a caller can
/// follow it; unknown ids are absent from the results. The summary is unmasked: the caller decides what to show.
/// </summary>
public interface IPatientLookup
{
    Task<PatientSummary?> GetAsync(Guid patientId, CancellationToken cancellationToken);

    /// <summary>Summaries of the known ids (at most 500 per call), keyed by id.</summary>
    Task<IReadOnlyDictionary<Guid, PatientSummary>> GetManyAsync(IReadOnlyCollection<Guid> patientIds, CancellationToken cancellationToken);
}
