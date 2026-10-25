using Platform.Application.Abstractions.Messaging;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Abstractions.Settings;
using Platform.Domain.Registration;
using Platform.Domain.Samples;
using Platform.Domain.Settings;

namespace Platform.Application.Samples.Policies;

// Cross-module reactions of the Samples module (intake §11, MODULES.md §3). Each policy runs after the commit that
// raised the Registration event, inside the event's tenant scope, is idempotent on its natural key and commits once.

/// <summary>P1: when a visit is registered, then create one sample per tube (idempotent on TubeId) and apply the arrival parameters.</summary>
public sealed class CreateSamplesOnVisitRegisteredPolicy(ISampleRepository samples, ITenantSettings settings, IUnitOfWork unitOfWork) : IDomainEventHandler<VisitRegistered>
{
    public Task HandleAsync(VisitRegistered domainEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        var visit = new TubeSampleFactory.VisitContext(domainEvent.VisitId, domainEvent.Accession, domainEvent.BranchId, domainEvent.PatientId, domainEvent.RegisteredByUserId, domainEvent.OccurredAtUtc);
        return TubeSampleFactory.CreateAsync(samples, settings, unitOfWork, visit, domainEvent.Tubes, domainEvent.Lines, cancellationToken);
    }
}

/// <summary>P1 for lines added to a saved visit, or Reserved lines released to Ordered: the same idempotent rule; a tube that already has its sample gains the new services.</summary>
public sealed class CreateSamplesOnVisitLinesAddedPolicy(ISampleRepository samples, ITenantSettings settings, IUnitOfWork unitOfWork) : IDomainEventHandler<VisitLinesAdded>
{
    public Task HandleAsync(VisitLinesAdded domainEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        var visit = new TubeSampleFactory.VisitContext(domainEvent.VisitId, domainEvent.Accession, domainEvent.BranchId, domainEvent.PatientId, domainEvent.ActorUserId, domainEvent.OccurredAtUtc);
        return TubeSampleFactory.CreateAsync(samples, settings, unitOfWork, visit, domainEvent.Tubes, domainEvent.Lines, cancellationToken);
    }
}

/// <summary>
/// P2: when a visit line is cancelled, then cancel the tube's live sample once no other line of the tube is Ordered
/// or Reserved (CP-26 "related samples cancelled"). Idempotent: a Cancelled sample stays as it is.
/// </summary>
public sealed class CancelSampleOnVisitLineCancelledPolicy(ISampleRepository samples, IVisitLineLookup lines, IUnitOfWork unitOfWork) : IDomainEventHandler<VisitLineCancelled>
{
    public async Task HandleAsync(VisitLineCancelled domainEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        if (domainEvent.TubeId is not { } tubeId || tubeId == Guid.Empty)
        {
            return;   // a Reserved line has no tube (BR-05)
        }

        var sample = await samples.GetLiveByTubeForUpdateAsync(tubeId, cancellationToken);
        if (sample is null || sample.Status == SampleStatus.Cancelled)
        {
            return;
        }

        if (await lines.TubeHasLiveLinesAsync(tubeId, domainEvent.LineId, cancellationToken))
        {
            return;
        }

        if (sample.Cancel(domainEvent.ActorUserId, domainEvent.Reason, domainEvent.OccurredAtUtc))
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
    }
}

/// <summary>The shared body of P1 (intake §7 T1, §16 Q3–Q4); a tube marked "collected outside" arrives Collected (LIS-CR-008 C-02).</summary>
internal static class TubeSampleFactory
{
    private const string OrderedLine = "Ordered";

    internal sealed record VisitContext(Guid VisitId, string Accession, Guid BranchId, Guid PatientId, Guid ActorUserId, DateTimeOffset AtUtc);

    public static async Task CreateAsync(
        ISampleRepository samples,
        ITenantSettings settings,
        IUnitOfWork unitOfWork,
        VisitContext visit,
        IReadOnlyList<RegisteredTube> tubes,
        IReadOnlyList<RegisteredLine> lines,
        CancellationToken cancellationToken)
    {
        var candidates = (tubes ?? [])
            .Where(tube => tube.TubeId != Guid.Empty && NeedsSample(tube, lines ?? []))
            .DistinctBy(tube => tube.TubeId)
            .ToList();
        if (candidates.Count == 0)
        {
            return;
        }

        var existing = await samples.ExistingTubeIdsAsync([.. candidates.Select(tube => tube.TubeId)], cancellationToken);
        var toCreate = candidates.Where(tube => !existing.Contains(tube.TubeId)).ToList();

        // Lines added to a tube that already has its sample (BR-02 merge on a saved visit): the services join the tube.
        var changed = false;
        foreach (var tube in candidates.Where(tube => existing.Contains(tube.TubeId)))
        {
            var sample = await samples.GetLiveByTubeForUpdateAsync(tube.TubeId, cancellationToken);
            changed |= sample is not null && sample.AddServices(tube.ServiceIds ?? []);
        }

        if (toCreate.Count == 0 && !changed)
        {
            return;   // already handled (the processor retries; INV-SMP-001)
        }

        var autoCollect = await settings.GetAsync(SettingKeys.AutoCollect, cancellationToken);
        var autoReceive = await settings.GetAsync(SettingKeys.AutoReceive, cancellationToken);
        foreach (var tube in toCreate)
        {
            var sample = Sample.Create(
                tube.TubeId,
                tube.SampleNumber,
                visit.VisitId,
                visit.Accession,
                visit.PatientId,
                visit.BranchId,
                tube.ClinicalGroupId,
                tube.SampleTypeId,
                tube.SampleConditionId,
                tube.ServiceIds ?? [],
                visit.ActorUserId,
                visit.AtUtc);
            if (tube.CollectedOutside)
            {
                sample.ArriveCollectedOutside(tube.CollectedAtUtc, autoReceive, visit.ActorUserId, visit.BranchId, visit.AtUtc);   // LIS-CR-008 C-02
            }
            else
            {
                sample.AutoAdvance(autoCollect, autoReceive, visit.ActorUserId, visit.BranchId, visit.AtUtc);
            }
            samples.Add(sample);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    /// <summary>BR-05: a tube whose referencing lines are all Reserved gets no sample yet; a tube no line references is created.</summary>
    private static bool NeedsSample(RegisteredTube tube, IReadOnlyList<RegisteredLine> lines)
    {
        var referencing = lines.Where(line => line.TubeId == tube.TubeId).ToList();
        return referencing.Count == 0 || referencing.Any(line => string.Equals(line.Status, OrderedLine, StringComparison.OrdinalIgnoreCase));
    }
}
