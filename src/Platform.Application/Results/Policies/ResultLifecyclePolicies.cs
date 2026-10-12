using Platform.Application.Abstractions.Messaging;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Catalogue;
using Platform.Application.Catalogue.Services;
using Platform.Application.Patients;
using Platform.Application.Samples;
using Platform.Domain.Integration;
using Platform.Domain.Patients;
using Platform.Domain.Results;
using Platform.Domain.Samples;
using Platform.SharedKernel.Abstractions;

namespace Platform.Application.Results.Policies;

// Reactions to Patients (merge) and Samples (collection) — intake §11 P4, P5.

/// <summary>P4: when patient files are merged, then the merged file's results belong to the survivor (idempotent, logged).</summary>
public sealed class MoveResultsOnPatientsMergedPolicy(IResultRepository results, IUnitOfWork unitOfWork) : IDomainEventHandler<PatientsMerged>
{
    public async Task HandleAsync(PatientsMerged domainEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        var changed = false;
        foreach (var result in await results.ListByPatientForUpdateAsync(domainEvent.MergedId, cancellationToken))
        {
            changed |= result.MoveToPatient(domainEvent.SurvivorId, domainEvent.ActorUserId, $"Merged into {domainEvent.SurvivorNumber}: {domainEvent.Reason}", domainEvent.OccurredAtUtc);
        }

        if (changed)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
    }
}

/// <summary>P5: when a tube is collected, then the turnaround clock of its open results restarts at the collection (BR-13).</summary>
public sealed class RetimeResultsOnSampleCollectedPolicy(IResultRepository results, ICatalogueLookup catalogue, IUnitOfWork unitOfWork) : IDomainEventHandler<SampleCollected>
{
    public async Task HandleAsync(SampleCollected domainEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        var tubeResults = await results.ListByTubeForUpdateAsync(domainEvent.TubeId, cancellationToken);
        if (tubeResults.Count == 0)
        {
            return;
        }

        var tests = (await catalogue.GetTestsAsync([.. tubeResults.Select(result => result.TestId).Distinct()], cancellationToken)).ToDictionary(test => test.Id);
        var changed = false;
        foreach (var result in tubeResults)
        {
            if (tests.TryGetValue(result.TestId, out var test))
            {
                changed |= result.Retime(domainEvent.OccurredAtUtc, TurnaroundClock.ExpectedAt(domainEvent.OccurredAtUtc, test.Timing.DurationHours, test.Timing.TestDays));
            }
        }

        if (changed)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
    }
}

/// <summary>
/// BR-11 (validation A-02, 2026-10-11): when a result is verified, then the patient's open results of the same test in
/// other visits re-check their delta against it, so the flag is on the result before anyone reviews or verifies it. The
/// sign-off commands re-check again (the safety net); this only brings the flag forward. Idempotent.
/// </summary>
public sealed class RecheckDeltasOnResultVerifiedPolicy(IResultRepository results, ICatalogueLookup catalogue, IUnitOfWork unitOfWork, IClock clock) : IDomainEventHandler<ResultVerified>
{
    public async Task HandleAsync(ResultVerified domainEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        var verified = await results.ReadAsync(domainEvent.ResultId, cancellationToken);
        if (verified is null || verified.NumericValue is null)
        {
            return;
        }

        var open = (await results.ListByPatientForUpdateAsync(verified.PatientId, cancellationToken))
            .Where(result => result.TestId == verified.TestId && result.VisitId != verified.VisitId)
            .ToList();
        if (open.Count == 0)
        {
            return;
        }

        var changed = await ResultDeltaRecheck.RunAsync(results, catalogue, open, [], clock.UtcNow, cancellationToken);
        if (changed.Count > 0)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
    }
}
