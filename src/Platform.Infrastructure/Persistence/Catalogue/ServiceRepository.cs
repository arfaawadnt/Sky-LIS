using Microsoft.EntityFrameworkCore;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Catalogue;
using Platform.Contracts.Catalogue;
using Platform.Contracts.Common;
using Platform.Domain.Catalogue;

namespace Platform.Infrastructure.Persistence.Catalogue;

internal sealed class ServiceRepository(AppDbContext context) : Repository<Service>(context), IServiceRepository
{
    private static readonly SortColumns<Service> Sorts = new SortColumns<Service>(CatalogueSortKeys.Services)
        .Column("sequence", service => service.Sequence)
        .Column("code", service => service.Code)
        .Column("nameEn", service => service.Name.En)
        .Column("nameAr", service => service.Name.Ar)
        .ThenBy(service => service.Code);

    public Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken)
    {
        var cleaned = (code ?? string.Empty).Trim().ToUpperInvariant();
        return Set.AnyAsync(service => service.Code == cleaned, cancellationToken);
    }

    public Task<bool> ShortcutTakenAsync(int shortcutNo, Guid? exceptId, CancellationToken cancellationToken) =>
        Set.AnyAsync(service => service.ShortcutNo == shortcutNo && (exceptId == null || service.Id != exceptId), cancellationToken);

    public async Task<int> NextSequenceAsync(CancellationToken cancellationToken) =>
        (await Set.MaxAsync(service => (int?)service.Sequence, cancellationToken).ConfigureAwait(false) ?? 0) + 1;

    public async Task<IReadOnlyDictionary<Guid, int>> SequencesAsync(CancellationToken cancellationToken) =>
        await Set.AsNoTracking().ToDictionaryAsync(service => service.Id, service => service.Sequence, cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<Service>> GetManyForUpdateAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ids);
        var list = ids.ToList();
        return await Set.Where(service => list.Contains(service.Id)).ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Service>> GetCarryingTestForUpdateAsync(Guid testId, CancellationToken cancellationToken) =>
        await Set.Where(service => service.Members.Any(member => member.Kind == ServiceMemberKind.Test && member.MemberId == testId))
            .OrderBy(service => service.Sequence)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyDictionary<Guid, ServiceMemberState>> MemberStatesAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ids);
        var list = ids.ToList();
        var rows = await Set.AsNoTracking().Where(service => list.Contains(service.Id))
            .Select(service => new { service.Id, service.Type, service.IsActive })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.ToDictionary(row => row.Id, row => new ServiceMemberState(row.Id, row.Type, row.IsActive));
    }

    public async Task<Paged<ServiceListItem>> ListAsync(ServiceListFilter filter, PageRequest page, CancellationToken cancellationToken)
    {
        var query = Filtered(filter);
        var total = await query.LongCountAsync(cancellationToken).ConfigureAwait(false);
        if (filter.Sort.Key is null)
        {
            var items = await ProjectAsync(query.OrderBy(service => service.Sequence).ThenBy(service => service.Code).Skip(page.Skip).Take(page.PageSize), cancellationToken).ConfigureAwait(false);
            return new Paged<ServiceListItem>(items, page.Page, page.PageSize, total);
        }

        // B-03: the page of the requested order is chosen on the server; the projection joins re-order nothing, so the
        // rows are put back in the order of the ids.
        var ids = await Sorts.Apply(query, filter.Sort, rows => rows.OrderBy(service => service.Sequence)).Skip(page.Skip).Take(page.PageSize)
            .Select(service => service.Id).ToListAsync(cancellationToken).ConfigureAwait(false);
        var projected = await ProjectAsync(Set.AsNoTracking().Where(service => ids.Contains(service.Id)), cancellationToken).ConfigureAwait(false);
        var position = ids.Select((id, index) => (id, index)).ToDictionary(entry => entry.id, entry => entry.index);
        return new Paged<ServiceListItem>(projected.OrderBy(item => position[item.Id]).ToList(), page.Page, page.PageSize, total);
    }

    public Task<IReadOnlyList<ServiceListItem>> ExportAsync(ServiceListFilter filter, CancellationToken cancellationToken) =>
        ProjectAsync(Filtered(filter).OrderBy(service => service.Sequence).ThenBy(service => service.Code).Take(CatalogueALimits.MaxExportRows), cancellationToken);

    public async Task<ServiceDetail?> GetDetailAsync(Guid id, CancellationToken cancellationToken)
    {
        var service = await Set.AsNoTracking().FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken).ConfigureAwait(false);
        if (service is null)
        {
            return null;
        }

        var testIds = service.Members.Where(member => member.Kind == ServiceMemberKind.Test).Select(member => member.MemberId).ToList();
        var serviceIds = service.Members.Where(member => member.Kind == ServiceMemberKind.Service).Select(member => member.MemberId).ToList();
        var names = new Dictionary<Guid, MemberName>();
        if (testIds.Count > 0)
        {
            var tests = await Context.Set<LabTest>().AsNoTracking().IgnoreAutoIncludes().Where(test => testIds.Contains(test.Id))
                .Select(test => new { test.Id, test.Code, NameEn = test.Name.En, NameAr = test.Name.Ar, test.Type, test.IsActive })
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            foreach (var test in tests)
            {
                names[test.Id] = new MemberName(test.Code, test.NameEn, test.NameAr, test.Type.ToString(), test.IsActive);
            }
        }

        if (serviceIds.Count > 0)
        {
            var services = await Set.AsNoTracking().IgnoreAutoIncludes().Where(candidate => serviceIds.Contains(candidate.Id))
                .Select(candidate => new { candidate.Id, candidate.Code, NameEn = candidate.Name.En, NameAr = candidate.Name.Ar, candidate.Type, candidate.IsActive })
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            foreach (var member in services)
            {
                names[member.Id] = new MemberName(member.Code, member.NameEn, member.NameAr, member.Type.ToString(), member.IsActive);
            }
        }

        var members = service.Members.OrderBy(member => member.Order).Select(member =>
        {
            var known = names.GetValueOrDefault(member.MemberId);
            return new ServiceMemberView(
                member.Kind.ToString(),
                member.MemberId,
                known?.Code ?? string.Empty,
                known?.NameEn ?? string.Empty,
                known?.NameAr ?? string.Empty,
                known?.Type ?? string.Empty,
                known?.IsActive ?? false,
                member.Order,
                member.InSum,
                member.Visible);
        }).ToList();

        return new ServiceDetail(
            service.Id,
            service.Code,
            service.Type.ToString(),
            service.ShortName,
            service.ShortcutNo,
            service.Name.En,
            service.Name.Ar,
            service.ReportName,
            service.Loinc,
            service.Cpt,
            service.ClinicalGroupId,
            service.SampleTypeId,
            service.SampleConditionId,
            service.Header,
            service.Aliases,
            service.Precaution,
            service.Gender.ToString(),
            service.TurnaroundHours,
            service.LabelCount,
            service.PrintAlone,
            service.IsOrderable,
            service.ShowOnWeb,
            service.ProfileSum,
            service.Sequence,
            service.IsActive,
            members);
    }

    public async Task<IReadOnlyList<ServiceSequenceRow>> SequenceRowsAsync(string? search, ServiceType? type, Guid? clinicalGroupId, CancellationToken cancellationToken)
    {
        var query = Filtered(new ServiceListFilter(search, type, clinicalGroupId, null));
        var rows = await (
            from service in query.OrderBy(service => service.Sequence).Take(CatalogueALimits.MaxExportRows)
            from grp in Context.Set<ClinicalGroup>().AsNoTracking().Where(grp => grp.Id == service.ClinicalGroupId).DefaultIfEmpty()
            select new
            {
                service.Id,
                service.Code,
                NameEn = service.Name.En,
                NameAr = service.Name.Ar,
                service.Type,
                service.ClinicalGroupId,
                GroupEn = grp == null ? null : grp.Name.En,
                GroupAr = grp == null ? null : grp.Name.Ar,
                service.Sequence,
                service.IsActive,
            }).ToListAsync(cancellationToken).ConfigureAwait(false);

        return rows.OrderBy(row => row.Sequence).Select(row => new ServiceSequenceRow(row.Id, row.Code, row.NameEn, row.NameAr, row.Type.ToString(), row.ClinicalGroupId, row.GroupEn, row.GroupAr, row.Sequence, row.IsActive)).ToList();
    }

    public async Task<Paged<ServiceLookupItem>> LookupAsync(string? search, ServiceType? type, bool? orderable, PageRequest page, CancellationToken cancellationToken)
    {
        var query = Filtered(new ServiceListFilter(search, type, null, true));
        if (orderable is { } wanted)
        {
            query = query.Where(service => service.IsOrderable == wanted);
        }

        var total = await query.LongCountAsync(cancellationToken).ConfigureAwait(false);
        var rows = await (
            from service in query.OrderBy(service => service.Sequence).ThenBy(service => service.Code).Skip(page.Skip).Take(page.PageSize)
            from grp in Context.Set<ClinicalGroup>().AsNoTracking().Where(grp => grp.Id == service.ClinicalGroupId).DefaultIfEmpty()
            select new
            {
                service.Id,
                service.Code,
                service.ShortName,
                service.ShortcutNo,
                NameEn = service.Name.En,
                NameAr = service.Name.Ar,
                service.Type,
                service.ClinicalGroupId,
                GroupEn = grp == null ? null : grp.Name.En,
                GroupAr = grp == null ? null : grp.Name.Ar,
                service.IsOrderable,
                service.Sequence,
            }).ToListAsync(cancellationToken).ConfigureAwait(false);

        var items = rows.OrderBy(row => row.Sequence).ThenBy(row => row.Code, StringComparer.Ordinal).Select(row => new ServiceLookupItem(row.Id, row.Code, row.ShortName, row.ShortcutNo, row.NameEn, row.NameAr, row.Type.ToString(), row.ClinicalGroupId, row.GroupEn, row.GroupAr, row.IsOrderable)).ToList();
        return new Paged<ServiceLookupItem>(items, page.Page, page.PageSize, total);
    }

    private IQueryable<Service> Filtered(ServiceListFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var query = Set.AsNoTracking().IgnoreAutoIncludes();
        if (filter.Type is { } type)
        {
            query = query.Where(service => service.Type == type);
        }

        if (filter.ClinicalGroupId is { } groupId)
        {
            query = query.Where(service => service.ClinicalGroupId == groupId);
        }

        if (filter.IsActive is { } active)
        {
            query = query.Where(service => service.IsActive == active);
        }

        if (CatalogueASearch.Pattern(filter.Search) is { } pattern)
        {
            var number = CatalogueASearch.Number(filter.Search);
            query = query.Where(service =>
                EF.Functions.ILike(service.Code, pattern, CatalogueASearch.Escape)
                || EF.Functions.ILike(service.ShortName, pattern, CatalogueASearch.Escape)
                || EF.Functions.ILike(service.Name.En, pattern, CatalogueASearch.Escape)
                || EF.Functions.ILike(service.Name.Ar, pattern, CatalogueASearch.Escape)
                || service.Aliases.Any(alias => EF.Functions.ILike(alias, pattern, CatalogueASearch.Escape))
                || (number != null && service.ShortcutNo == number));
        }

        return query;
    }

    private async Task<IReadOnlyList<ServiceListItem>> ProjectAsync(IQueryable<Service> query, CancellationToken cancellationToken)
    {
        var rows = await (
            from service in query
            from grp in Context.Set<ClinicalGroup>().AsNoTracking().Where(grp => grp.Id == service.ClinicalGroupId).DefaultIfEmpty()
            from sample in Context.Set<SampleType>().AsNoTracking().IgnoreAutoIncludes().Where(sample => sample.Id == service.SampleTypeId).DefaultIfEmpty()
            select new
            {
                service.Id,
                service.Code,
                service.ShortName,
                service.ShortcutNo,
                NameEn = service.Name.En,
                NameAr = service.Name.Ar,
                service.Type,
                service.ClinicalGroupId,
                GroupEn = grp == null ? null : grp.Name.En,
                GroupAr = grp == null ? null : grp.Name.Ar,
                service.SampleTypeId,
                SampleEn = sample == null ? null : sample.Name.En,
                SampleAr = sample == null ? null : sample.Name.Ar,
                service.Gender,
                service.Sequence,
                MemberCount = service.Members.Count,
                service.IsOrderable,
                service.PrintAlone,
                service.IsActive,
            }).ToListAsync(cancellationToken).ConfigureAwait(false);

        return rows.OrderBy(row => row.Sequence).ThenBy(row => row.Code, StringComparer.Ordinal).Select(row => new ServiceListItem(
            row.Id,
            row.Code,
            row.ShortName,
            row.ShortcutNo,
            row.NameEn,
            row.NameAr,
            row.Type.ToString(),
            row.ClinicalGroupId,
            row.GroupEn,
            row.GroupAr,
            row.SampleTypeId,
            row.SampleEn,
            row.SampleAr,
            row.Gender.ToString(),
            row.Sequence,
            row.MemberCount,
            row.IsOrderable,
            row.PrintAlone,
            row.IsActive)).ToList();
    }

    private sealed record MemberName(string Code, string NameEn, string NameAr, string Type, bool IsActive);
}
