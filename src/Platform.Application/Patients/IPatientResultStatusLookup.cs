using Platform.Domain.Organization;

namespace Platform.Application.Patients;

/// <summary>
/// How far the results of a patient have progressed, for the demographics lock (change request 2026-10-13 item 1,
/// PAT-032): the highest status among the <b>live</b> (not Cancelled) results, expressed on the
/// <see cref="DemographicsEditLimit"/> scale the special right <c>DemographicsEditUpTo</c> is compared with. The
/// Results module (an earlier wave for Patients' lock) is read through its entity type; the adapter is
/// <c>Platform.Infrastructure.Persistence.Patients.PatientResultStatusLookup</c>.
/// </summary>
public interface IPatientResultStatusLookup
{
    /// <summary>
    /// The highest live result status of <paramref name="visitId"/> (the visit the user edits from) when given, else of
    /// every visit of <paramref name="patientId"/>; <see cref="DemographicsEditLimit.Ordered"/> when the patient has no
    /// live result at all. A visit id that names no live result of this patient (unknown, foreign or not yet ordered)
    /// falls back to the patient-wide answer, so a client-supplied id never weakens the lock.
    /// </summary>
    Task<DemographicsEditLimit> HighestStatusAsync(Guid patientId, Guid? visitId, CancellationToken cancellationToken);
}
