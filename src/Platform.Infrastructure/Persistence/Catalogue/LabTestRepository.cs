using Microsoft.EntityFrameworkCore;
using Platform.Application.Catalogue;
using Platform.Contracts.Catalogue;
using Platform.Contracts.Common;
using Platform.Domain.Catalogue;

namespace Platform.Infrastructure.Persistence.Catalogue;

internal sealed class LabTestRepository(AppDbContext context) : Repository<LabTest>(context), ILabTestRepository
{
    public Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken)
    {
        var cleaned = (code ?? string.Empty).Trim().ToUpperInvariant();
        return Set.AnyAsync(test => test.Code == cleaned, cancellationToken);
    }

    public async Task<IReadOnlyDictionary<Guid, LabTestMemberState>> MemberStatesAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ids);
        var list = ids.ToList();
        var rows = await Set.AsNoTracking().IgnoreAutoIncludes().Where(test => list.Contains(test.Id))
            .Select(test => new { test.Id, test.Type, test.IsActive })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.ToDictionary(row => row.Id, row => new LabTestMemberState(row.Id, row.Type, row.IsActive));
    }

    public async Task<IReadOnlyList<LabTestFormulaNode>> FormulaGraphAsync(CancellationToken cancellationToken)
    {
        var rows = await Set.AsNoTracking().IgnoreAutoIncludes()
            .Select(test => new { test.Id, test.Code, test.IsActive, test.FormulaReferences })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.Select(row => new LabTestFormulaNode(row.Id, row.Code, row.IsActive, row.FormulaReferences)).ToList();
    }

    public async Task<Paged<LabTestListItem>> ListAsync(LabTestListFilter filter, PageRequest page, CancellationToken cancellationToken)
    {
        var query = Filtered(filter);
        var total = await query.LongCountAsync(cancellationToken).ConfigureAwait(false);
        var items = await ProjectAsync(query.OrderBy(test => test.Code).Skip(page.Skip).Take(page.PageSize), cancellationToken).ConfigureAwait(false);
        return new Paged<LabTestListItem>(items, page.Page, page.PageSize, total);
    }

    public Task<IReadOnlyList<LabTestListItem>> ExportAsync(LabTestListFilter filter, CancellationToken cancellationToken) =>
        ProjectAsync(Filtered(filter).OrderBy(test => test.Code).Take(CatalogueALimits.MaxExportRows), cancellationToken);

    public async Task<LabTestDetail?> GetDetailAsync(Guid id, CancellationToken cancellationToken)
    {
        var test = await Set.AsNoTracking().FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken).ConfigureAwait(false);
        if (test is null)
        {
            return null;
        }

        var timing = new LabTestTimingItem(test.DurationHours, test.MinDurationHours, test.FrequencyCheckDays, test.FrequencyAction.ToString(), test.DeltaPercent, test.DeltaWindowDays, test.TestDays);
        return new LabTestDetail(
            test.Id,
            test.Code,
            test.Name.En,
            test.Name.Ar,
            test.ReportName,
            test.Loinc,
            test.Cpt,
            test.ClinicalGroupId,
            test.Type.ToString(),
            test.SampleTypeId,
            test.SampleConditionId,
            test.UnitId,
            test.Gender.ToString(),
            test.Methodology,
            test.Decimals,
            test.IsHiddenHelper,
            test.PrintReference,
            test.LabelCount,
            test.Prefix,
            timing,
            test.FormulaExpression,
            test.FormulaReferences,
            test.RangeBands.OrderBy(band => band.DisplayOrder).Select(band => new RangeBandItem(
                band.Sex.ToString(),
                band.AgeFrom,
                band.AgeTo,
                band.AgeUnit.ToString(),
                band.PatientConditionId,
                band.Low,
                band.High,
                band.PanicLow,
                band.PanicHigh,
                band.ReportableLow,
                band.ReportableHigh,
                band.TextRange)).ToList(),
            test.ListValues.OrderBy(value => value.DisplayOrder).Select(value => new ListValueItem(value.Value, value.IsNormal, value.IsCritical, value.IsDefault, value.IsPlaceholder, value.Colour)).ToList(),
            test.Paragraphs.OrderBy(paragraph => paragraph.DisplayOrder).Select(paragraph => new ParagraphItem(paragraph.Title, paragraph.Text)).ToList(),
            test.IsActive);
    }

    public async Task<Paged<TestLookupItem>> LookupAsync(string? search, LabTestType? type, PageRequest page, CancellationToken cancellationToken)
    {
        var query = Filtered(new LabTestListFilter(search, type, null, true));
        var total = await query.LongCountAsync(cancellationToken).ConfigureAwait(false);
        var rows = await (
            from test in query.OrderBy(test => test.Code).Skip(page.Skip).Take(page.PageSize)
            join grp in Context.Set<ClinicalGroup>().AsNoTracking() on test.ClinicalGroupId equals grp.Id
            from unit in Context.Set<MeasurementUnit>().AsNoTracking().Where(unit => unit.Id == test.UnitId).DefaultIfEmpty()
            select new
            {
                test.Id,
                test.Code,
                NameEn = test.Name.En,
                NameAr = test.Name.Ar,
                test.Type,
                test.ClinicalGroupId,
                GroupEn = grp.Name.En,
                GroupAr = grp.Name.Ar,
                UnitName = unit == null ? null : unit.Name,
            }).ToListAsync(cancellationToken).ConfigureAwait(false);

        var items = rows.OrderBy(row => row.Code, StringComparer.Ordinal).Select(row => new TestLookupItem(row.Id, row.Code, row.NameEn, row.NameAr, row.Type.ToString(), row.ClinicalGroupId, row.GroupEn, row.GroupAr, row.UnitName)).ToList();
        return new Paged<TestLookupItem>(items, page.Page, page.PageSize, total);
    }

    private IQueryable<LabTest> Filtered(LabTestListFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var query = Set.AsNoTracking().IgnoreAutoIncludes();
        if (filter.Type is { } type)
        {
            query = query.Where(test => test.Type == type);
        }

        if (filter.ClinicalGroupId is { } groupId)
        {
            query = query.Where(test => test.ClinicalGroupId == groupId);
        }

        if (filter.IsActive is { } active)
        {
            query = query.Where(test => test.IsActive == active);
        }

        if (CatalogueASearch.Pattern(filter.Search) is { } pattern)
        {
            query = query.Where(test =>
                EF.Functions.ILike(test.Code, pattern, CatalogueASearch.Escape)
                || EF.Functions.ILike(test.Name.En, pattern, CatalogueASearch.Escape)
                || EF.Functions.ILike(test.Name.Ar, pattern, CatalogueASearch.Escape)
                || (test.ReportName != null && EF.Functions.ILike(test.ReportName, pattern, CatalogueASearch.Escape)));
        }

        return query;
    }

    private async Task<IReadOnlyList<LabTestListItem>> ProjectAsync(IQueryable<LabTest> query, CancellationToken cancellationToken)
    {
        var rows = await (
            from test in query
            join grp in Context.Set<ClinicalGroup>().AsNoTracking() on test.ClinicalGroupId equals grp.Id
            from unit in Context.Set<MeasurementUnit>().AsNoTracking().Where(unit => unit.Id == test.UnitId).DefaultIfEmpty()
            from sample in Context.Set<SampleType>().AsNoTracking().IgnoreAutoIncludes().Where(sample => sample.Id == test.SampleTypeId).DefaultIfEmpty()
            select new
            {
                test.Id,
                test.Code,
                NameEn = test.Name.En,
                NameAr = test.Name.Ar,
                test.Type,
                test.ClinicalGroupId,
                GroupEn = grp.Name.En,
                GroupAr = grp.Name.Ar,
                UnitName = unit == null ? null : unit.Name,
                SampleEn = sample == null ? null : sample.Name.En,
                SampleAr = sample == null ? null : sample.Name.Ar,
                test.Gender,
                test.IsHiddenHelper,
                test.IsActive,
            }).ToListAsync(cancellationToken).ConfigureAwait(false);

        return rows.OrderBy(row => row.Code, StringComparer.Ordinal).Select(row => new LabTestListItem(
            row.Id,
            row.Code,
            row.NameEn,
            row.NameAr,
            row.Type.ToString(),
            row.ClinicalGroupId,
            row.GroupEn,
            row.GroupAr,
            row.UnitName,
            row.SampleEn,
            row.SampleAr,
            row.Gender.ToString(),
            row.IsHiddenHelper,
            row.IsActive)).ToList();
    }
}
