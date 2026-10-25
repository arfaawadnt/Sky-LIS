using Microsoft.EntityFrameworkCore;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Abstractions.Security;
using Platform.Application.Samples;
using Platform.Contracts.Common;
using Platform.Contracts.Samples;
using Platform.Domain.Samples;

namespace Platform.Infrastructure.Persistence.Samples;

/// <summary>
/// Write side and projections of the Samples module. Transitions load the sample with its timeline (the new steps
/// are discovered as Added); registers read untracked samples and resolve other modules' names in batches through
/// <see cref="SampleReferenceReader"/> after paging.
/// </summary>
internal sealed class SampleRepository(AppDbContext context, ITenantContextAccessor tenant) : Repository<Sample>(context), ISampleRepository
{
    private readonly SampleReferenceReader references = new(context, tenant);

    public override Task<Sample?> GetForUpdateAsync(Guid id, CancellationToken cancellationToken) =>
        Set.Include(sample => sample.Events).FirstOrDefaultAsync(sample => sample.Id == id, cancellationToken);

    public async Task<IReadOnlySet<Guid>> ExistingTubeIdsAsync(IReadOnlyCollection<Guid> tubeIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tubeIds);
        if (tubeIds.Count == 0)
        {
            return new HashSet<Guid>();
        }

        var ids = tubeIds.Distinct().ToArray();
        var found = await Set.AsNoTracking().Where(sample => ids.Contains(sample.TubeId)).Select(sample => sample.TubeId).Distinct().ToListAsync(cancellationToken).ConfigureAwait(false);
        return found.ToHashSet();
    }

    public Task<Sample?> GetLiveByTubeForUpdateAsync(Guid tubeId, CancellationToken cancellationToken) =>
        Set.Include(sample => sample.Events)
            .Where(sample => sample.TubeId == tubeId && sample.Status != SampleStatus.Replaced)
            .OrderByDescending(sample => sample.Generation)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<Sample?> GetByNumberForUpdateAsync(string sampleNumber, CancellationToken cancellationToken) =>
        Set.Include(sample => sample.Events).FirstOrDefaultAsync(sample => sample.SampleNumber == sampleNumber, cancellationToken);

    public async Task<IReadOnlyList<Sample>> ListLiveByVisitForUpdateAsync(Guid visitId, CancellationToken cancellationToken) =>
        await Set.Include(sample => sample.Events)
            .Where(sample => sample.VisitId == visitId && sample.Status != SampleStatus.Replaced)
            .OrderBy(sample => sample.SampleNumber)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    private static readonly SortColumns<Sample> Sorts = new SortColumns<Sample>(SamplesSortKeys.Samples)
        .Column("sampleNumber", sample => sample.SampleNumber)
        .Column("accession", sample => sample.Accession)
        .Column("status", sample => sample.Status == SampleStatus.Ordered ? 0 : sample.Status == SampleStatus.Collected ? 1 : sample.Status == SampleStatus.Received ? 2 : sample.Status == SampleStatus.Rejected ? 3 : sample.Status == SampleStatus.Cancelled ? 4 : 5)
        .Column("rejectedAtUtc", sample => sample.Rejection != null ? sample.Rejection.AtUtc : (DateTimeOffset?)null)
        .ThenBy(sample => sample.SampleNumber)
        .ThenBy(sample => sample.Id);

    public async Task<Paged<SampleListItem>> ListAsync(SampleFilter filter, PageRequest page, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var query = Set.AsNoTracking();
        if (filter.VisitId is { } visitId)
        {
            query = query.Where(sample => sample.VisitId == visitId);
        }

        if (filter.Accession is { } accession)
        {
            var upper = accession.ToUpperInvariant();
            query = query.Where(sample => sample.Accession.ToUpper() == upper);
        }

        if (filter.Status is { } status)
        {
            query = query.Where(sample => sample.Status == status);
        }

        if (filter.FromUtc is { } from)
        {
            query = query.Where(sample => sample.OrderedAtUtc >= from);
        }

        if (filter.ToUtc is { } to)
        {
            query = query.Where(sample => sample.OrderedAtUtc < to);
        }

        if (filter.BranchId is { } branchId)
        {
            query = query.Where(sample => sample.BranchId == branchId);
        }

        if (filter.ClinicalGroupId is { } groupId)
        {
            query = query.Where(sample => sample.ClinicalGroupId == groupId);
        }

        if (filter.Search is { } search)
        {
            query = query.Where(sample => sample.SampleNumber.StartsWith(search) || sample.Accession.ToUpper().StartsWith(search));
        }

        var total = await query.LongCountAsync(cancellationToken).ConfigureAwait(false);
        var rows = await Sorts.Apply(query, filter.Sort, samples => samples
                .OrderByDescending(sample => sample.OrderedAtUtc)
                .ThenBy(sample => sample.Accession)
                .ThenBy(sample => sample.SampleNumber))
            .Skip(page.Skip)
            .Take(page.PageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new Paged<SampleListItem>(await ToListItemsAsync(rows, cancellationToken).ConfigureAwait(false), page.Page, page.PageSize, total);
    }

    public async Task<IReadOnlyList<SampleListItem>> ListRejectedAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, Guid? branchId, CancellationToken cancellationToken)
    {
        var query = Set.AsNoTracking()
            .Where(sample => sample.Status == SampleStatus.Rejected && sample.StatusChangedAtUtc >= fromUtc && sample.StatusChangedAtUtc < toUtc);
        if (branchId is { } branch)
        {
            query = query.Where(sample => sample.BranchId == branch);
        }

        var rows = await query.OrderByDescending(sample => sample.StatusChangedAtUtc).Take(PageRequest.MaxPageSize).ToListAsync(cancellationToken).ConfigureAwait(false);
        return await ToListItemsAsync(rows, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Guid?> FindIdByNumberAsync(string sampleNumber, CancellationToken cancellationToken)
    {
        var ids = await Set.AsNoTracking().Where(sample => sample.SampleNumber == sampleNumber).Select(sample => sample.Id).Take(1).ToListAsync(cancellationToken).ConfigureAwait(false);
        return ids.Count == 0 ? null : ids[0];
    }

    public async Task<SampleDetail?> GetDetailAsync(Guid id, CancellationToken cancellationToken)
    {
        var sample = await Set.AsNoTracking().FirstOrDefaultAsync(row => row.Id == id, cancellationToken).ConfigureAwait(false);
        if (sample is null)
        {
            return null;
        }

        var summary = (await ToListItemsAsync([sample], cancellationToken).ConfigureAwait(false))[0];
        var services = await references.ServicesAsync(sample.ServiceIds, cancellationToken).ConfigureAwait(false);
        var users = await references.UsersAsync([sample.CollectedByUserId, sample.ReceivedByUserId, sample.Rejection?.ByUserId], cancellationToken).ConfigureAwait(false);
        var branches = await references.BranchesAsync(
            new[] { sample.CollectedBranchId, sample.ReceivedBranchId, sample.Rejection?.BranchId }.Where(branch => branch is not null).Select(branch => branch!.Value),
            cancellationToken).ConfigureAwait(false);

        var linkIds = new[] { sample.ReplacedBySampleId, sample.ReplacesSampleId }.Where(link => link is not null).Select(link => link!.Value).ToArray();
        var links = linkIds.Length == 0
            ? []
            : await Set.AsNoTracking().Where(row => linkIds.Contains(row.Id)).Select(row => new { row.Id, row.SampleNumber, row.Status }).ToListAsync(cancellationToken).ConfigureAwait(false);

        SampleLinkView? Link(Guid? linkId) =>
            links.Where(row => row.Id == linkId).Select(row => new SampleLinkView(row.Id, row.SampleNumber, row.Status.ToString())).FirstOrDefault();

        SampleStepView? Step(DateTimeOffset? at, Guid? userId, Guid? branchId) =>
            at is not { } when ? null : new SampleStepView(
                when,
                userId,
                userId is { } user && users.TryGetValue(user, out var userName) ? userName : null,
                branchId,
                branchId is { } branch && branches.TryGetValue(branch, out var name) ? name.NameEn : null,
                branchId is { } branchAr && branches.TryGetValue(branchAr, out var nameAr) ? nameAr.NameAr : null);

        var rejection = sample.Rejection is { } value
            ? new SampleRejectionView(value.ReasonId, value.ReasonNameEn, value.ReasonNameAr, value.Note, Step(value.AtUtc, value.ByUserId, value.BranchId)!)
            : null;

        return new SampleDetail(
            summary,
            [.. sample.ServiceIds.Select(serviceId => ToServiceView(serviceId, services))],
            Step(sample.CollectedAtUtc, sample.CollectedByUserId, sample.CollectedBranchId),
            Step(sample.ReceivedAtUtc, sample.ReceivedByUserId, sample.ReceivedBranchId),
            rejection,
            Link(sample.ReplacedBySampleId),
            Link(sample.ReplacesSampleId),
            sample.CancelledAtUtc,
            sample.CancellationReason,
            sample.IsResultEntryAllowed);
    }

    public async Task<IReadOnlyList<SampleTimelineStep>> GetTimelineStepsAsync(Guid id, CancellationToken cancellationToken)
    {
        var steps = await Context.Set<SampleEvent>().AsNoTracking()
            .Where(step => step.SampleId == id)
            .OrderBy(step => step.Sequence)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (steps.Count == 0)
        {
            return [];
        }

        var snapshot = await Set.AsNoTracking().Where(sample => sample.Id == id).Select(sample => sample.Rejection).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        var users = await references.UsersAsync(steps.Select(step => step.ByUserId), cancellationToken).ConfigureAwait(false);
        var branches = await references.BranchesAsync(steps.Where(step => step.BranchId is not null).Select(step => step.BranchId!.Value), cancellationToken).ConfigureAwait(false);
        var reasons = await references.ReasonsAsync(steps.Where(step => step.ReasonId is not null).Select(step => step.ReasonId!.Value), cancellationToken).ConfigureAwait(false);

        return [.. steps.Select(step =>
        {
            string? reasonEn = null;
            string? reasonAr = null;
            if (step.ReasonId is { } reasonId)
            {
                if (reasons is not null && reasons.TryGetValue(reasonId, out var reason))
                {
                    (reasonEn, reasonAr) = (reason.NameEn, reason.NameAr);
                }
                else if (snapshot is not null && snapshot.ReasonId == reasonId)
                {
                    (reasonEn, reasonAr) = (snapshot.ReasonNameEn, snapshot.ReasonNameAr);
                }
            }

            var branch = step.BranchId is { } branchId && branches.TryGetValue(branchId, out var row) ? row : null;
            return new SampleTimelineStep(
                step.Id,
                step.Sequence,
                step.Action.ToString(),
                step.Status.ToString(),
                step.AtUtc,
                step.ByUserId,
                step.ByUserId is { } user && users.TryGetValue(user, out var name) ? name : null,
                step.BranchId,
                branch?.NameEn,
                branch?.NameAr,
                step.ReasonId,
                reasonEn,
                reasonAr,
                step.Note,
                null);
        })];
    }

    public async Task<SampleLabelDocument?> GetLabelAsync(Guid id, int labelCopies, CancellationToken cancellationToken)
    {
        var sample = await Set.AsNoTracking().FirstOrDefaultAsync(row => row.Id == id, cancellationToken).ConfigureAwait(false);
        return sample is null ? null : (await ToLabelsAsync([sample], labelCopies, cancellationToken).ConfigureAwait(false))[0];
    }

    public async Task<IReadOnlyList<SampleLabelDocument>> GetVisitLabelsAsync(Guid visitId, int labelCopies, CancellationToken cancellationToken)
    {
        var samples = await Set.AsNoTracking()
            .Where(sample => sample.VisitId == visitId && sample.Status != SampleStatus.Replaced && sample.Status != SampleStatus.Cancelled)
            .OrderBy(sample => sample.SampleNumber)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return samples.Count == 0 ? [] : await ToLabelsAsync(samples, labelCopies, cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<SampleListItem>> ToListItemsAsync(IReadOnlyList<Sample> samples, CancellationToken cancellationToken)
    {
        if (samples.Count == 0)
        {
            return [];
        }

        var patients = await references.PatientsAsync(samples.Select(sample => sample.PatientId), cancellationToken).ConfigureAwait(false);
        var groups = await references.ClinicalGroupsAsync(samples.Select(sample => sample.ClinicalGroupId), cancellationToken).ConfigureAwait(false);
        var types = await references.SampleTypesAsync(samples.Select(sample => sample.SampleTypeId), cancellationToken).ConfigureAwait(false);
        var conditions = await references.SampleConditionsAsync(samples.Where(sample => sample.SampleConditionId is not null).Select(sample => sample.SampleConditionId!.Value), cancellationToken).ConfigureAwait(false);

        return [.. samples.Select(sample =>
        {
            var patient = patients.GetValueOrDefault(sample.PatientId);
            var group = groups.GetValueOrDefault(sample.ClinicalGroupId);
            var type = types.GetValueOrDefault(sample.SampleTypeId);
            var condition = sample.SampleConditionId is { } conditionId ? conditions.GetValueOrDefault(conditionId) : null;
            var rejection = sample.Rejection;
            return new SampleListItem(
                sample.Id,
                sample.SampleNumber,
                sample.TubeId,
                sample.Generation,
                sample.VisitId,
                sample.Accession,
                sample.PatientId,
                patient?.Code,
                patient?.NameEn,
                patient?.NameAr,
                sample.BranchId,
                sample.ClinicalGroupId,
                group?.Code,
                group?.NameEn,
                group?.NameAr,
                sample.SampleTypeId,
                type?.NameEn,
                type?.NameAr,
                sample.SampleConditionId,
                condition?.NameEn,
                condition?.NameAr,
                sample.ServiceIds.Length,
                sample.Status.ToString(),
                sample.OrderedAtUtc,
                sample.StatusChangedAtUtc,
                sample.CollectedAtUtc,
                sample.ReceivedAtUtc,
                rejection?.ReasonId,
                rejection?.ReasonNameEn,
                rejection?.ReasonNameAr,
                rejection?.Note,
                rejection?.AtUtc,
                sample.ReplacedBySampleId,
                sample.ReplacesSampleId,
                sample.CollectedOutside);
        })];
    }

    private async Task<IReadOnlyList<SampleLabelDocument>> ToLabelsAsync(IReadOnlyList<Sample> samples, int labelCopies, CancellationToken cancellationToken)
    {
        var items = await ToListItemsAsync(samples, cancellationToken).ConfigureAwait(false);
        var services = await references.ServicesAsync(samples.SelectMany(sample => sample.ServiceIds), cancellationToken).ConfigureAwait(false);
        var branches = await references.BranchesAsync(samples.Select(sample => sample.BranchId), cancellationToken).ConfigureAwait(false);

        return [.. samples.Zip(items, (sample, item) =>
        {
            var serviceViews = sample.ServiceIds.Select(serviceId => ToServiceView(serviceId, services)).ToList();
            var serviceCopies = sample.ServiceIds.Select(serviceId => services.TryGetValue(serviceId, out var row) ? row.LabelCount : 0).DefaultIfEmpty(0).Max();
            var branch = branches.GetValueOrDefault(sample.BranchId);
            return new SampleLabelDocument(
                sample.Id,
                sample.SampleNumber,
                sample.Accession,
                item.UnitCode,
                item.ClinicalGroupNameEn,
                item.ClinicalGroupNameAr,
                sample.SampleTypeId,
                item.SampleTypeNameEn,
                item.SampleTypeNameAr,
                sample.SampleConditionId,
                item.SampleConditionNameEn,
                item.SampleConditionNameAr,
                sample.PatientId,
                item.PatientNumber,
                item.PatientName,
                item.PatientNameAr,
                sample.BranchId,
                branch?.NameEn,
                branch?.NameAr,
                sample.CollectedAtUtc ?? sample.OrderedAtUtc,
                serviceViews,
                Math.Clamp(Math.Max(labelCopies, serviceCopies), 1, 10),
                sample.CollectedOutside);
        })];
    }

    private static SampleServiceView ToServiceView(Guid serviceId, IReadOnlyDictionary<Guid, SampleReferenceReader.ServiceRow> services) =>
        services.TryGetValue(serviceId, out var row)
            ? new SampleServiceView(serviceId, row.Code, row.NameEn, row.NameAr)
            : new SampleServiceView(serviceId, null, null, null);
}
