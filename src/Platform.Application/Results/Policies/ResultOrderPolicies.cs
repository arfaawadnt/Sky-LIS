using Platform.Application.Abstractions.Messaging;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Catalogue;
using Platform.Application.Registration;
using Platform.Application.Samples;
using Platform.Domain.Catalogue;
using Platform.Domain.Registration;
using Platform.Domain.Results;

namespace Platform.Application.Results.Policies;

// Cross-module reactions of the Results module to Registration (intake LIS-INT-RES-001 §11, MODULES.md §3). Each policy
// runs after the commit that raised the event, inside the event's tenant scope, is idempotent on its natural key and
// commits once.

/// <summary>P1: when a visit is registered, then one Ordered result per test of every Ordered line (idempotent on LineId + TestId).</summary>
public sealed class CreateResultsOnVisitRegisteredPolicy(IResultRepository results, ICatalogueLookup catalogue, ISampleStatusLookup samples, IVisitLookup visits, IUnitOfWork unitOfWork) : IDomainEventHandler<VisitRegistered>
{
    public async Task HandleAsync(VisitRegistered domainEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        var stored = await visits.GetAsync(domainEvent.VisitId, cancellationToken);
        var visit = new ResultOrdering.VisitContext(domainEvent.VisitId, domainEvent.Accession, domainEvent.BranchId, domainEvent.PatientId, domainEvent.PayerId, domainEvent.Urgent, domainEvent.RegisteredByUserId, domainEvent.OccurredAtUtc, stored);
        await new ResultOrdering(results, catalogue, samples, unitOfWork).CreateAsync(visit, domainEvent.Tubes, domainEvent.Lines, cancellationToken);
    }
}

/// <summary>P1 for lines added to a saved visit or Reserved lines released to Ordered: the same idempotent rule.</summary>
public sealed class CreateResultsOnVisitLinesAddedPolicy(IResultRepository results, ICatalogueLookup catalogue, ISampleStatusLookup samples, IVisitLookup visits, IUnitOfWork unitOfWork) : IDomainEventHandler<VisitLinesAdded>
{
    public async Task HandleAsync(VisitLinesAdded domainEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        var stored = await visits.GetAsync(domainEvent.VisitId, cancellationToken);
        var visit = new ResultOrdering.VisitContext(
            domainEvent.VisitId,
            domainEvent.Accession,
            domainEvent.BranchId,
            domainEvent.PatientId,
            stored?.PayerId,
            stored?.Urgent ?? false,
            domainEvent.ActorUserId,
            domainEvent.OccurredAtUtc,
            stored);
        await new ResultOrdering(results, catalogue, samples, unitOfWork).CreateAsync(visit, domainEvent.Tubes, domainEvent.Lines, cancellationToken);
    }
}

/// <summary>P2: when a visit line is cancelled, then its results are cancelled with the line's reason (idempotent).</summary>
public sealed class CancelResultsOnVisitLineCancelledPolicy(IResultRepository results, IUnitOfWork unitOfWork) : IDomainEventHandler<VisitLineCancelled>
{
    public async Task HandleAsync(VisitLineCancelled domainEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        var changed = false;
        foreach (var result in await results.ListByLineForUpdateAsync(domainEvent.LineId, cancellationToken))
        {
            changed |= result.Cancel(domainEvent.ActorUserId, domainEvent.Reason, domainEvent.OccurredAtUtc);
        }

        if (changed)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
    }
}

/// <summary>P6: when a visit is transferred to another payer, then its results follow (worklist payer filter).</summary>
public sealed class FollowPayerOnVisitTransferredPolicy(IResultRepository results, IUnitOfWork unitOfWork) : IDomainEventHandler<VisitTransferred>
{
    public async Task HandleAsync(VisitTransferred domainEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        var changed = false;
        foreach (var result in await results.ListByVisitForUpdateAsync(domainEvent.VisitId, cancellationToken))
        {
            changed |= result.FollowPayer(domainEvent.ToPayerId);
        }

        if (changed)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
    }
}

/// <summary>
/// The body of P1 (intake §7 T1, §11): expands each Ordered line into its tests (the line's test ids, else the service
/// through the catalogue: profile, culture and text-report members; a Package line carries none — its member lines do;
/// Additional services have none), places each result in its report unit (the clinical group; or its own unit when the
/// line's service, or the package it belongs to, prints alone) and on its tube, and starts the turnaround clock
/// (BR-13) at the registration — or at the collection when the tube is already collected.
/// </summary>
internal sealed class ResultOrdering(IResultRepository results, ICatalogueLookup catalogue, ISampleStatusLookup samples, IUnitOfWork unitOfWork)
{
    private const string OrderedLine = "Ordered";
    private const string PackageMemberLine = "PackageMember";
    private const int MaxDepth = 4;

    internal sealed record VisitContext(Guid VisitId, string Accession, Guid BranchId, Guid PatientId, Guid? PayerId, bool Urgent, Guid ActorUserId, DateTimeOffset AtUtc, VisitView? Stored);

    private sealed record Member(Guid OwningServiceId, Guid TestId, int Order);

    public async Task CreateAsync(VisitContext visit, IReadOnlyList<RegisteredTube> tubes, IReadOnlyList<RegisteredLine> lines, CancellationToken cancellationToken)
    {
        var ordered = (lines ?? []).Where(line => line.LineId != Guid.Empty && line.ServiceId != Guid.Empty && string.Equals(line.Status, OrderedLine, StringComparison.OrdinalIgnoreCase)).DistinctBy(line => line.LineId).ToList();
        if (ordered.Count == 0)
        {
            return;
        }

        var stored = visit.Stored?.Lines.ToDictionary(line => line.Id) ?? [];
        var parentServices = ordered.Select(line => stored.GetValueOrDefault(line.LineId)?.ParentLineId)
            .Where(parent => parent is not null && stored.ContainsKey(parent.Value))
            .Select(parent => stored[parent!.Value].ServiceId);
        var seen = new HashSet<(Guid LineId, Guid TestId)>(await results.ExistingLineTestsAsync([.. ordered.Select(line => line.LineId)], cancellationToken));
        var services = await LoadServicesAsync(ordered.Select(line => line.ServiceId).Concat(parentServices), cancellationToken);
        var expansion = ordered.ToDictionary(line => line.LineId, line => Expand(line, services));
        var tests = (await catalogue.GetTestsAsync([.. expansion.Values.SelectMany(members => members).Select(member => member.TestId).Distinct()], cancellationToken)).ToDictionary(test => test.Id);
        var tubeList = tubes ?? [];
        var tubeIds = tubeList.Select(tube => tube.TubeId).Concat(ordered.Where(line => line.TubeId is not null).Select(line => line.TubeId!.Value)).Where(id => id != Guid.Empty).Distinct().ToList();
        var collected = await samples.ByTubesAsync(tubeIds, cancellationToken);

        var created = 0;
        foreach (var line in ordered)
        {
            var lineService = services.GetValueOrDefault(line.ServiceId);
            var storedLine = stored.GetValueOrDefault(line.LineId);
            // a package member reports in its package's unit when that package prints alone; a formula operand line
            // (LIS-CR-015) is a service of its own and reports like one
            var parentLine = storedLine?.ParentLineId is { } parentId && string.Equals(storedLine.Kind, PackageMemberLine, StringComparison.Ordinal) ? stored.GetValueOrDefault(parentId) : null;
            var parentService = parentLine is null ? null : services.GetValueOrDefault(parentLine.ServiceId);
            foreach (var member in expansion[line.LineId])
            {
                if (seen.Contains((line.LineId, member.TestId)) || !tests.TryGetValue(member.TestId, out var test))
                {
                    continue;
                }

                var owning = services.GetValueOrDefault(member.OwningServiceId);
                var unitKey = parentService?.PrintAlone == true ? ReportUnits.ForService(visit.VisitId, parentLine!.Id, parentService.Id)
                    : lineService?.PrintAlone == true ? ReportUnits.ForService(visit.VisitId, line.LineId, line.ServiceId)
                    : owning?.PrintAlone == true ? ReportUnits.ForService(visit.VisitId, line.LineId, member.OwningServiceId)
                    : ReportUnits.ForGroup(visit.VisitId, test.ClinicalGroupId);
                var tubeId = tubeList.FirstOrDefault(tube => tube.ClinicalGroupId == test.ClinicalGroupId && (tube.ServiceIds ?? []).Contains(member.OwningServiceId))?.TubeId
                    ?? tubeList.FirstOrDefault(tube => tube.ClinicalGroupId == test.ClinicalGroupId && (tube.ServiceIds ?? []).Contains(line.ServiceId))?.TubeId
                    ?? line.TubeId;
                var expectedAt = storedLine?.ExpectedResultAtUtc ?? TurnaroundClock.ExpectedAt(visit.AtUtc, test.Timing.DurationHours, test.Timing.TestDays);

                var result = Result.Order(new ResultOrder(
                    visit.VisitId,
                    visit.Accession,
                    line.LineId,
                    line.ServiceId,
                    member.OwningServiceId,
                    test.Id,
                    test.Type,
                    test.IsHiddenHelper,
                    tubeId,
                    test.ClinicalGroupId,
                    unitKey,
                    visit.PatientId,
                    visit.BranchId,
                    visit.PayerId,
                    visit.Urgent,
                    owning?.Sequence ?? lineService?.Sequence ?? 0,
                    member.Order,
                    visit.AtUtc,
                    expectedAt,
                    visit.ActorUserId));

                if (tubeId is { } tube && collected.TryGetValue(tube, out var sample) && sample.CollectedAtUtc is { } collectedAt)
                {
                    result.Retime(collectedAt, TurnaroundClock.ExpectedAt(collectedAt, test.Timing.DurationHours, test.Timing.TestDays));
                }

                results.Add(result);
                seen.Add((line.LineId, member.TestId));
                created++;
            }
        }

        if (created > 0)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>The services and every nested member service, by id.</summary>
    private async Task<Dictionary<Guid, CatalogueServiceInfo>> LoadServicesAsync(IEnumerable<Guid> serviceIds, CancellationToken cancellationToken)
    {
        var loaded = new Dictionary<Guid, CatalogueServiceInfo>();
        var pending = serviceIds.Distinct().ToList();
        for (var depth = 0; depth < MaxDepth && pending.Count > 0; depth++)
        {
            foreach (var service in await catalogue.GetServicesAsync(pending, cancellationToken))
            {
                loaded[service.Id] = service;
            }

            pending = [.. loaded.Values.Where(service => service.Type != ServiceType.Package).SelectMany(service => service.MemberServiceIds).Where(id => !loaded.ContainsKey(id)).Distinct()];
        }

        return loaded;
    }

    /// <summary>The tests of a line with the member service that owns each, in report order; none for a Package or Additional line.</summary>
    private static List<Member> Expand(RegisteredLine line, IReadOnlyDictionary<Guid, CatalogueServiceInfo> services)
    {
        var service = services.GetValueOrDefault(line.ServiceId);
        if (service?.Type is ServiceType.Package or ServiceType.Additional)
        {
            return [];
        }

        var members = new List<Member>();
        if (service is not null)
        {
            Walk(service, services, members, 0);
        }

        var wanted = (line.TestIds ?? []).Where(id => id != Guid.Empty).Distinct().ToList();
        if (wanted.Count == 0)
        {
            return [.. members.DistinctBy(member => member.TestId)];
        }

        var byTest = members.DistinctBy(member => member.TestId).ToDictionary(member => member.TestId);
        return [.. wanted.Select((testId, index) => byTest.TryGetValue(testId, out var member) ? member : new Member(line.ServiceId, testId, 1000 + index))];
    }

    private static void Walk(CatalogueServiceInfo service, IReadOnlyDictionary<Guid, CatalogueServiceInfo> services, List<Member> members, int depth)
    {
        if (service.Type is ServiceType.Package or ServiceType.Additional || depth >= MaxDepth)
        {
            return;
        }

        foreach (var member in service.Members.OrderBy(member => member.Order))
        {
            if (member.Kind == ServiceMemberKind.Test)
            {
                members.Add(new Member(service.Id, member.MemberId, member.Order));
            }
            else if (services.TryGetValue(member.MemberId, out var child))
            {
                Walk(child, services, members, depth + 1);
            }
        }
    }
}
