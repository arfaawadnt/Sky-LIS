using Microsoft.EntityFrameworkCore;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Catalogue;
using Platform.Contracts.Catalogue;
using Platform.Contracts.Common;
using Platform.Domain.Catalogue;

namespace Platform.Infrastructure.Persistence.Catalogue;

/// <summary>Text search of the catalogue-A registers: ILIKE with the wildcard characters of the input escaped.</summary>
internal static class CatalogueASearch
{
    public const string Escape = "\\";

    /// <summary><c>%text%</c> with <c>\</c>, <c>%</c> and <c>_</c> escaped, or null when there is nothing to search for.</summary>
    public static string? Pattern(string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            return null;
        }

        var escaped = search.Trim().Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal);
        return $"%{escaped}%";
    }

    public static int? Number(string? search) =>
        int.TryParse(search?.Trim(), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var number) ? number : null;
}

internal sealed class ClinicalGroupRepository(AppDbContext context) : Repository<ClinicalGroup>(context), IClinicalGroupRepository
{
    private static readonly SortColumns<ClinicalGroup> Sorts = new SortColumns<ClinicalGroup>(CatalogueSortKeys.ClinicalGroups)
        .Column("code", group => group.Code)
        .Column("nameEn", group => group.Name.En)
        .Column("nameAr", group => group.Name.Ar)
        .Column("displayOrder", group => group.DisplayOrder)
        .ThenBy(group => group.Code)
        .ThenBy(group => group.Id);

    public Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken)
    {
        var cleaned = (code ?? string.Empty).Trim().ToUpperInvariant();
        return Set.AnyAsync(group => group.Code == cleaned, cancellationToken);
    }

    public Task<bool?> ActiveStateAsync(Guid id, CancellationToken cancellationToken) =>
        Set.Where(group => group.Id == id).Select(group => (bool?)group.IsActive).FirstOrDefaultAsync(cancellationToken);

    public async Task<Paged<ClinicalGroupListItem>> ListAsync(CatalogueAListFilter filter, PageRequest page, CancellationToken cancellationToken)
    {
        var query = Filtered(filter);
        var total = await query.LongCountAsync(cancellationToken).ConfigureAwait(false);
        var ordered = Sorts.Apply(query, filter.Sort, rows => rows.OrderBy(group => group.DisplayOrder).ThenBy(group => group.Code));
        var items = await Project(ordered.Skip(page.Skip).Take(page.PageSize)).ToListAsync(cancellationToken).ConfigureAwait(false);
        return new Paged<ClinicalGroupListItem>(items, page.Page, page.PageSize, total);
    }

    public async Task<IReadOnlyList<ClinicalGroupListItem>> ExportAsync(CatalogueAListFilter filter, CancellationToken cancellationToken) =>
        await Project(Filtered(filter).OrderBy(group => group.DisplayOrder).ThenBy(group => group.Code).Take(CatalogueALimits.MaxExportRows)).ToListAsync(cancellationToken).ConfigureAwait(false);

    private IQueryable<ClinicalGroup> Filtered(CatalogueAListFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var query = Set.AsNoTracking();
        if (filter.IsActive is { } active)
        {
            query = query.Where(group => group.IsActive == active);
        }

        if (CatalogueASearch.Pattern(filter.Search) is { } pattern)
        {
            query = query.Where(group =>
                EF.Functions.ILike(group.Code, pattern, CatalogueASearch.Escape)
                || EF.Functions.ILike(group.UnitCode, pattern, CatalogueASearch.Escape)
                || EF.Functions.ILike(group.Name.En, pattern, CatalogueASearch.Escape)
                || EF.Functions.ILike(group.Name.Ar, pattern, CatalogueASearch.Escape));
        }

        return query;
    }

    private static IQueryable<ClinicalGroupListItem> Project(IQueryable<ClinicalGroup> query) =>
        query.Select(group => new ClinicalGroupListItem(group.Id, group.Code, group.UnitCode, group.Name.En, group.Name.Ar, group.ReportName, group.ReportTemplateId, group.DisplayOrder, group.IsActive));
}

internal sealed class SampleTypeRepository(AppDbContext context) : Repository<SampleType>(context), ISampleTypeRepository
{
    private static readonly SortColumns<SampleType> Sorts = new SortColumns<SampleType>(CatalogueSortKeys.SampleTypes)
        .Column("code", sampleType => sampleType.Code)
        .Column("nameEn", sampleType => sampleType.Name.En)
        .Column("nameAr", sampleType => sampleType.Name.Ar)
        .ThenBy(sampleType => sampleType.Code);

    public Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken)
    {
        var cleaned = (code ?? string.Empty).Trim().ToUpperInvariant();
        return Set.AnyAsync(sampleType => sampleType.Code == cleaned, cancellationToken);
    }

    public async Task<Paged<SampleTypeListItem>> ListAsync(SampleTypeListFilter filter, PageRequest page, CancellationToken cancellationToken)
    {
        var query = Filtered(filter);
        var total = await query.LongCountAsync(cancellationToken).ConfigureAwait(false);
        var ordered = Sorts.Apply(query, filter.Sort, rows => rows.OrderBy(sampleType => sampleType.Code));
        var items = await Project(ordered.Skip(page.Skip).Take(page.PageSize)).ToListAsync(cancellationToken).ConfigureAwait(false);
        return new Paged<SampleTypeListItem>(items, page.Page, page.PageSize, total);
    }

    public async Task<IReadOnlyList<SampleTypeListItem>> ExportAsync(SampleTypeListFilter filter, CancellationToken cancellationToken) =>
        await Project(Filtered(filter).OrderBy(sampleType => sampleType.Code).Take(CatalogueALimits.MaxExportRows)).ToListAsync(cancellationToken).ConfigureAwait(false);

    public Task<SampleTypeDetail?> GetDetailAsync(Guid id, CancellationToken cancellationToken) =>
        Set.AsNoTracking()
            .Where(sampleType => sampleType.Id == id)
            .Select(sampleType => new SampleTypeDetail(
                sampleType.Id,
                sampleType.Code,
                sampleType.ShortName,
                sampleType.Name.En,
                sampleType.Name.Ar,
                sampleType.IsActive,
                sampleType.Conditions.OrderBy(condition => condition.Code).Select(condition => new SampleConditionView(condition.Id, condition.Code, condition.Name.En, condition.Name.Ar, condition.IsActive)).ToList(),
                sampleType.ForCulture))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<CompatibleSampleTypeView>> ListCompatibilityAsync(CancellationToken cancellationToken) =>
        await Set.AsNoTracking()
            .OrderBy(sampleType => sampleType.Code)
            .Take(CatalogueALimits.MaxExportRows)
            .Select(sampleType => new CompatibleSampleTypeView(
                sampleType.Id,
                sampleType.Code,
                sampleType.ShortName,
                sampleType.Name.En,
                sampleType.Name.Ar,
                sampleType.IsActive,
                sampleType.Conditions.OrderBy(condition => condition.Code)
                    .Select(condition => new CompatibleConditionView(condition.Id, condition.Code, condition.Name.En, condition.Name.Ar, condition.IsActive, condition.MergesIntoConditionId))
                    .ToList()))
            .ToListAsync(cancellationToken).ConfigureAwait(false);

    private IQueryable<SampleType> Filtered(SampleTypeListFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var query = Set.AsNoTracking();
        if (filter.IsActive is { } active)
        {
            query = query.Where(sampleType => sampleType.IsActive == active);
        }

        if (filter.ForCulture is { } forCulture)
        {
            query = query.Where(sampleType => sampleType.ForCulture == forCulture);
        }

        if (CatalogueASearch.Pattern(filter.Search) is { } pattern)
        {
            query = query.Where(sampleType =>
                EF.Functions.ILike(sampleType.Code, pattern, CatalogueASearch.Escape)
                || EF.Functions.ILike(sampleType.ShortName, pattern, CatalogueASearch.Escape)
                || EF.Functions.ILike(sampleType.Name.En, pattern, CatalogueASearch.Escape)
                || EF.Functions.ILike(sampleType.Name.Ar, pattern, CatalogueASearch.Escape));
        }

        return query;
    }

    private static IQueryable<SampleTypeListItem> Project(IQueryable<SampleType> query) =>
        query.Select(sampleType => new SampleTypeListItem(
            sampleType.Id,
            sampleType.Code,
            sampleType.ShortName,
            sampleType.Name.En,
            sampleType.Name.Ar,
            sampleType.Conditions.Count(condition => condition.IsActive),
            sampleType.IsActive,
            sampleType.ForCulture));
}

internal sealed class MeasurementUnitRepository(AppDbContext context) : Repository<MeasurementUnit>(context), IMeasurementUnitRepository
{
    // The symbol sorts by its case-folded key (mg/dL beside MG/L), then by id so equal keys page stably.
    private static readonly SortColumns<MeasurementUnit> Sorts = new SortColumns<MeasurementUnit>(CatalogueSortKeys.Units)
        .Column("name", unit => unit.NameKey)
        .ThenBy(unit => unit.Id);

    public Task<bool> NameExistsAsync(string nameKey, Guid? exceptId, CancellationToken cancellationToken) =>
        Set.AnyAsync(unit => unit.NameKey == nameKey && (exceptId == null || unit.Id != exceptId), cancellationToken);

    public Task<bool?> ActiveStateAsync(Guid id, CancellationToken cancellationToken) =>
        Set.Where(unit => unit.Id == id).Select(unit => (bool?)unit.IsActive).FirstOrDefaultAsync(cancellationToken);

    public async Task<Paged<MeasurementUnitListItem>> ListAsync(CatalogueAListFilter filter, PageRequest page, CancellationToken cancellationToken)
    {
        var query = Filtered(filter);
        var total = await query.LongCountAsync(cancellationToken).ConfigureAwait(false);
        var items = await Sorts.Apply(query, filter.Sort, rows => rows.OrderBy(unit => unit.NameKey)).Skip(page.Skip).Take(page.PageSize)
            .Select(unit => new MeasurementUnitListItem(unit.Id, unit.Name, unit.IsActive))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return new Paged<MeasurementUnitListItem>(items, page.Page, page.PageSize, total);
    }

    public async Task<IReadOnlyList<MeasurementUnitListItem>> ExportAsync(CatalogueAListFilter filter, CancellationToken cancellationToken) =>
        await Filtered(filter).OrderBy(unit => unit.NameKey).Take(CatalogueALimits.MaxExportRows)
            .Select(unit => new MeasurementUnitListItem(unit.Id, unit.Name, unit.IsActive))
            .ToListAsync(cancellationToken).ConfigureAwait(false);

    private IQueryable<MeasurementUnit> Filtered(CatalogueAListFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var query = Set.AsNoTracking();
        if (filter.IsActive is { } active)
        {
            query = query.Where(unit => unit.IsActive == active);
        }

        if (CatalogueASearch.Pattern(filter.Search) is { } pattern)
        {
            query = query.Where(unit => EF.Functions.ILike(unit.Name, pattern, CatalogueASearch.Escape));
        }

        return query;
    }
}

internal sealed class PatientConditionRepository(AppDbContext context) : Repository<PatientCondition>(context), IPatientConditionRepository
{
    private static readonly SortColumns<PatientCondition> Sorts = new SortColumns<PatientCondition>(CatalogueSortKeys.PatientConditions)
        .Column("nameEn", condition => condition.Name.En)
        .Column("nameAr", condition => condition.Name.Ar)
        .ThenBy(condition => condition.Id);

    public Task<bool> NameExistsAsync(string nameKey, Guid? exceptId, CancellationToken cancellationToken) =>
        Set.AnyAsync(condition => condition.NameKey == nameKey && (exceptId == null || condition.Id != exceptId), cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, bool>> ActiveStatesAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ids);
        var list = ids.ToList();
        return await Set.AsNoTracking().Where(condition => list.Contains(condition.Id))
            .ToDictionaryAsync(condition => condition.Id, condition => condition.IsActive, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Paged<PatientConditionListItem>> ListAsync(CatalogueAListFilter filter, PageRequest page, CancellationToken cancellationToken)
    {
        var query = Filtered(filter);
        var total = await query.LongCountAsync(cancellationToken).ConfigureAwait(false);
        var items = await Sorts.Apply(query, filter.Sort, rows => rows.OrderBy(condition => condition.NameKey)).Skip(page.Skip).Take(page.PageSize)
            .Select(condition => new PatientConditionListItem(condition.Id, condition.Name.En, condition.Name.Ar, condition.IsActive))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return new Paged<PatientConditionListItem>(items, page.Page, page.PageSize, total);
    }

    public async Task<IReadOnlyList<PatientConditionListItem>> ExportAsync(CatalogueAListFilter filter, CancellationToken cancellationToken) =>
        await Filtered(filter).OrderBy(condition => condition.NameKey).Take(CatalogueALimits.MaxExportRows)
            .Select(condition => new PatientConditionListItem(condition.Id, condition.Name.En, condition.Name.Ar, condition.IsActive))
            .ToListAsync(cancellationToken).ConfigureAwait(false);

    private IQueryable<PatientCondition> Filtered(CatalogueAListFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var query = Set.AsNoTracking();
        if (filter.IsActive is { } active)
        {
            query = query.Where(condition => condition.IsActive == active);
        }

        if (CatalogueASearch.Pattern(filter.Search) is { } pattern)
        {
            query = query.Where(condition => EF.Functions.ILike(condition.Name.En, pattern, CatalogueASearch.Escape) || EF.Functions.ILike(condition.Name.Ar, pattern, CatalogueASearch.Escape));
        }

        return query;
    }
}
