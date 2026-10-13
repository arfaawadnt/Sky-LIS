using Microsoft.EntityFrameworkCore;
using Platform.Application.Patients;
using Platform.Domain.Organization;
using Platform.Domain.Results;

namespace Platform.Infrastructure.Persistence.Patients;

/// <summary>
/// <see cref="IPatientResultStatusLookup"/> over <c>app.results</c> (Results, an earlier wave: its entity type is read directly;
/// the tenant filter and RLS bound the read). Cancelled results are ignored; the distinct statuses are folded in memory (at
/// most five) because the status column is a string. A visit id naming no live result of the patient falls back to the
/// patient-wide answer (see the port).
/// </summary>
internal sealed class PatientResultStatusLookup(AppDbContext context) : IPatientResultStatusLookup
{
    public async Task<DemographicsEditLimit> HighestStatusAsync(Guid patientId, Guid? visitId, CancellationToken cancellationToken)
    {
        if (visitId is { } visit)
        {
            var ofVisit = await StatusesAsync(patientId, visit, cancellationToken).ConfigureAwait(false);
            if (ofVisit.Count > 0)
            {
                return Highest(ofVisit);
            }
        }

        return Highest(await StatusesAsync(patientId, null, cancellationToken).ConfigureAwait(false));
    }

    private Task<List<ResultStatus>> StatusesAsync(Guid patientId, Guid? visitId, CancellationToken cancellationToken)
    {
        var query = context.Set<Result>().AsNoTracking().Where(result => result.PatientId == patientId && result.Status != ResultStatus.Cancelled);
        if (visitId is { } visit)
        {
            query = query.Where(result => result.VisitId == visit);
        }

        return query.Select(result => result.Status).Distinct().ToListAsync(cancellationToken);
    }

    private static DemographicsEditLimit Highest(IReadOnlyCollection<ResultStatus> statuses) =>
        statuses.Count == 0 ? DemographicsEditLimit.Ordered : statuses.Max(Map);

    /// <summary>The result status model and the edit-limit scale share their five names, lowest first.</summary>
    private static DemographicsEditLimit Map(ResultStatus status) => status switch
    {
        ResultStatus.Ordered => DemographicsEditLimit.Ordered,
        ResultStatus.Complete => DemographicsEditLimit.Complete,
        ResultStatus.Reviewed => DemographicsEditLimit.Reviewed,
        ResultStatus.Verified => DemographicsEditLimit.Verified,
        ResultStatus.Printed => DemographicsEditLimit.Printed,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "A cancelled result has no edit-limit rank."),
    };
}
