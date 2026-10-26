using Microsoft.EntityFrameworkCore;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Catalogue;
using Platform.Contracts.Catalogue;
using Platform.Contracts.Common;
using Platform.Domain.Catalogue;

namespace Platform.Infrastructure.Persistence.Catalogue;

/// <summary>
/// Compatibility groups of samples (LIS-CR-015 M4). Lists page and sort on the server (allow-listed keys) and name the
/// members afterwards from their sample types, so a page costs three queries whatever its size.
/// </summary>
internal sealed class SampleCompatibilityRepository(AppDbContext context) : Repository<SampleCompatibilityGroup>(context), ISampleCompatibilityGroupRepository
{
    private static readonly SortColumns<SampleCompatibilityGroup> Sorts = new SortColumns<SampleCompatibilityGroup>(CatalogueSortKeys.SampleCompatibilityGroups)
        .Column("code", group => group.Code)
        .Column("nameEn", group => group.Name.En)
        .Column("nameAr", group => group.Name.Ar)
        .ThenBy(group => group.Code);

    public Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken)
    {
        var cleaned = (code ?? string.Empty).Trim().ToUpperInvariant();
        return Set.AnyAsync(group => group.Code == cleaned, cancellationToken);
    }

    public async Task<IReadOnlyList<SampleType>> SampleTypesAsync(IReadOnlyCollection<Guid> sampleTypeIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sampleTypeIds);
        return sampleTypeIds.Count == 0
            ? []
            : await Context.Set<SampleType>().AsNoTracking().Where(type => sampleTypeIds.Contains(type.Id)).ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<CompatibilityConflict?> FindActiveHolderAsync(IReadOnlyCollection<CompatibleSamplePair> pairs, Guid excludeGroupId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(pairs);
        var typeIds = pairs.Select(pair => pair.SampleTypeId).Distinct().ToList();
        if (typeIds.Count == 0)
        {
            return null;
        }

        var others = await Set.AsNoTracking()
            .Where(group => group.IsActive && group.Id != excludeGroupId && group.Members.Any(member => typeIds.Contains(member.SampleTypeId)))
            .OrderBy(group => group.Code)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        foreach (var pair in pairs)
        {
            var holder = others.FirstOrDefault(group => group.Pairs.Contains(pair));
            if (holder is not null)
            {
                var type = await Context.Set<SampleType>().AsNoTracking().FirstOrDefaultAsync(candidate => candidate.Id == pair.SampleTypeId, cancellationToken).ConfigureAwait(false);
                var condition = pair.SampleConditionId is { } conditionId ? type?.Conditions.FirstOrDefault(candidate => candidate.Id == conditionId) : null;
                return new CompatibilityConflict(holder.Code, type?.Code ?? pair.SampleTypeId.ToString("N"), condition?.Code);
            }
        }

        return null;
    }

    public async Task<Paged<SampleCompatibilityGroupView>> ListAsync(CatalogueAListFilter filter, PageRequest page, CancellationToken cancellationToken)
    {
        var query = Filtered(filter);
        var total = await query.LongCountAsync(cancellationToken).ConfigureAwait(false);
        var ordered = Sorts.Apply(query, filter.Sort, rows => rows.OrderBy(group => group.Code));
        var rows = await ordered.Skip(page.Skip).Take(page.PageSize).ToListAsync(cancellationToken).ConfigureAwait(false);
        return new Paged<SampleCompatibilityGroupView>(await ViewsAsync(rows, cancellationToken).ConfigureAwait(false), page.Page, page.PageSize, total);
    }

    public async Task<IReadOnlyList<SampleCompatibilityGroupView>> ExportAsync(CatalogueAListFilter filter, CancellationToken cancellationToken)
    {
        var rows = await Filtered(filter).OrderBy(group => group.Code).Take(CatalogueALimits.MaxExportRows).ToListAsync(cancellationToken).ConfigureAwait(false);
        return await ViewsAsync(rows, cancellationToken).ConfigureAwait(false);
    }

    public async Task<SampleCompatibilityGroupView?> GetViewAsync(Guid id, CancellationToken cancellationToken)
    {
        var group = await Set.AsNoTracking().FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken).ConfigureAwait(false);
        return group is null ? null : (await ViewsAsync([group], cancellationToken).ConfigureAwait(false))[0];
    }

    private IQueryable<SampleCompatibilityGroup> Filtered(CatalogueAListFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var query = Set.AsNoTracking();
        if (filter.IsActive is { } active)
        {
            query = query.Where(group => group.IsActive == active);
        }

        if (CatalogueASearch.Pattern(filter.Search) is { } pattern)
        {
            var types = Context.Set<SampleType>();
            var conditions = Context.Set<SampleCondition>();
            query = query.Where(group =>
                EF.Functions.ILike(group.Code, pattern, CatalogueASearch.Escape)
                || EF.Functions.ILike(group.Name.En, pattern, CatalogueASearch.Escape)
                || EF.Functions.ILike(group.Name.Ar, pattern, CatalogueASearch.Escape)
                || group.Members.Any(member =>
                    types.Any(type => type.Id == member.SampleTypeId
                        && (EF.Functions.ILike(type.Code, pattern, CatalogueASearch.Escape) || EF.Functions.ILike(type.Name.En, pattern, CatalogueASearch.Escape) || EF.Functions.ILike(type.Name.Ar, pattern, CatalogueASearch.Escape)))
                    || conditions.Any(condition => condition.Id == member.SampleConditionId
                        && (EF.Functions.ILike(condition.Code, pattern, CatalogueASearch.Escape) || EF.Functions.ILike(condition.Name.En, pattern, CatalogueASearch.Escape) || EF.Functions.ILike(condition.Name.Ar, pattern, CatalogueASearch.Escape)))));
        }

        return query;
    }

    private async Task<IReadOnlyList<SampleCompatibilityGroupView>> ViewsAsync(IReadOnlyList<SampleCompatibilityGroup> groups, CancellationToken cancellationToken)
    {
        var typeIds = groups.SelectMany(group => group.Members.Select(member => member.SampleTypeId)).Distinct().ToList();
        var types = typeIds.Count == 0
            ? new Dictionary<Guid, SampleType>()
            : (await Context.Set<SampleType>().AsNoTracking().Where(type => typeIds.Contains(type.Id)).ToListAsync(cancellationToken).ConfigureAwait(false)).ToDictionary(type => type.Id);

        CompatibilityMemberView View(SampleCompatibilityMember member)
        {
            var type = types.GetValueOrDefault(member.SampleTypeId);
            var condition = member.SampleConditionId is { } conditionId ? type?.Conditions.FirstOrDefault(candidate => candidate.Id == conditionId) : null;
            var available = type is { IsActive: true } && (member.SampleConditionId is null || condition is { IsActive: true });
            return new CompatibilityMemberView(
                member.SampleTypeId,
                type?.Code ?? string.Empty,
                type?.Name.En ?? string.Empty,
                type?.Name.Ar ?? string.Empty,
                member.SampleConditionId,
                condition?.Code,
                condition?.Name.En,
                condition?.Name.Ar,
                member.Priority,
                available);
        }

        return [.. groups.Select(group => new SampleCompatibilityGroupView(group.Id, group.Code, group.Name.En, group.Name.Ar, group.IsActive, [.. group.Members.Select(View)]))];
    }
}
