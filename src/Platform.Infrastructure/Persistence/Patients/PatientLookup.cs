using Microsoft.EntityFrameworkCore;
using Platform.Application.Patients;
using Platform.Contracts.Patients;
using Platform.Domain.Patients;
using Platform.SharedKernel.Abstractions;

namespace Platform.Infrastructure.Persistence.Patients;

/// <summary>
/// Adapter of <see cref="IPatientLookup"/> (the module's read port for Registration and Results): reads
/// <c>app.patients</c> of the current tenant (query filter + RLS), computes the age for the clock's UTC date and
/// resolves the survivor of a Merged file by following the merge chain.
/// </summary>
internal sealed class PatientLookup(AppDbContext context, IClock clock) : IPatientLookup
{
    public const int MaxIds = 500;

    public async Task<PatientSummary?> GetAsync(Guid patientId, CancellationToken cancellationToken)
    {
        var found = await GetManyAsync([patientId], cancellationToken).ConfigureAwait(false);
        return found.TryGetValue(patientId, out var summary) ? summary : null;
    }

    public async Task<IReadOnlyDictionary<Guid, PatientSummary>> GetManyAsync(IReadOnlyCollection<Guid> patientIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(patientIds);
        var ids = patientIds.Distinct().ToList();
        ArgumentOutOfRangeException.ThrowIfGreaterThan(ids.Count, MaxIds, nameof(patientIds));
        if (ids.Count == 0)
        {
            return new Dictionary<Guid, PatientSummary>();
        }

        var patients = context.Set<Patient>().AsNoTracking();
        var rows = await PatientQueries.Rows(patients.Where(patient => ids.Contains(patient.Id)), patients).ToListAsync(cancellationToken).ConfigureAwait(false);
        var survivors = await ResolveSurvivorsAsync(rows.Where(row => row.MergedIntoPatientId is not null).ToDictionary(row => row.Id, row => row.MergedIntoPatientId!.Value), cancellationToken).ConfigureAwait(false);

        var today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        return rows.ToDictionary(row => row.Id, row => PatientProjections.ToSummary(row, today, survivors.TryGetValue(row.Id, out var survivor) ? survivor : null));
    }

    /// <summary>For each merged file, the first Active file of its chain (at most <see cref="PatientRepository.MaxMergeDepth"/> hops).</summary>
    private async Task<Dictionary<Guid, Guid>> ResolveSurvivorsAsync(Dictionary<Guid, Guid> pointers, CancellationToken cancellationToken)
    {
        var resolved = new Dictionary<Guid, Guid>();
        var current = new Dictionary<Guid, Guid>(pointers);
        for (var depth = 0; depth < PatientRepository.MaxMergeDepth && current.Count > 0; depth++)
        {
            var targets = current.Values.Distinct().ToList();
            var states = await context.Set<Patient>().AsNoTracking()
                .Where(patient => targets.Contains(patient.Id))
                .Select(patient => new { patient.Id, patient.Status, patient.MergedIntoPatientId })
                .ToDictionaryAsync(patient => patient.Id, cancellationToken)
                .ConfigureAwait(false);

            var next = new Dictionary<Guid, Guid>();
            foreach (var (origin, target) in current)
            {
                if (!states.TryGetValue(target, out var state))
                {
                    continue;
                }

                if (state.Status == PatientStatus.Active || state.MergedIntoPatientId is null)
                {
                    resolved[origin] = target;
                }
                else
                {
                    next[origin] = state.MergedIntoPatientId.Value;
                }
            }

            current = next;
        }

        return resolved;
    }
}
