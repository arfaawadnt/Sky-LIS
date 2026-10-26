using Microsoft.EntityFrameworkCore;
using Platform.Application.Catalogue;
using Platform.Domain.Catalogue;

namespace Platform.Infrastructure.Persistence.Catalogue;

/// <summary>
/// The adapter of <see cref="ICatalogueLookup"/>: no-tracking reads of catalogue half A's tables in the current
/// tenant (query filter and RLS), mapped to the port's records. Inactive rows are returned with their flag.
/// </summary>
internal sealed class CatalogueLookup(AppDbContext context) : ICatalogueLookup
{
    public async Task<CatalogueServiceInfo?> GetServiceAsync(Guid serviceId, CancellationToken cancellationToken) =>
        (await GetServicesAsync([serviceId], cancellationToken).ConfigureAwait(false)).FirstOrDefault();

    public async Task<IReadOnlyList<CatalogueServiceInfo>> GetServicesAsync(IReadOnlyCollection<Guid> serviceIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(serviceIds);
        var ids = serviceIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        var services = await context.Set<Service>().AsNoTracking().Where(service => ids.Contains(service.Id)).ToListAsync(cancellationToken).ConfigureAwait(false);
        return services.Select(ToInfo).ToList();
    }

    public async Task<CatalogueTestInfo?> GetTestAsync(Guid testId, CancellationToken cancellationToken) =>
        (await GetTestsAsync([testId], cancellationToken).ConfigureAwait(false)).FirstOrDefault();

    public async Task<IReadOnlyList<CatalogueTestInfo>> GetTestsAsync(IReadOnlyCollection<Guid> testIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(testIds);
        var ids = testIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        var tests = await context.Set<LabTest>().AsNoTracking().Where(test => ids.Contains(test.Id)).ToListAsync(cancellationToken).ConfigureAwait(false);
        return await ToInfosAsync(tests, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<CatalogueTestInfo>> GetTestsByCodeAsync(IReadOnlyCollection<string> codes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(codes);
        var cleaned = codes.Select(code => code.Trim().ToUpperInvariant()).Distinct(StringComparer.Ordinal).ToList();
        if (cleaned.Count == 0)
        {
            return [];
        }

        var tests = await context.Set<LabTest>().AsNoTracking().Where(test => cleaned.Contains(test.Code)).ToListAsync(cancellationToken).ConfigureAwait(false);
        return await ToInfosAsync(tests, cancellationToken).ConfigureAwait(false);
    }

    public async Task<CatalogueClinicalGroupInfo?> GetClinicalGroupAsync(Guid clinicalGroupId, CancellationToken cancellationToken) =>
        (await GetClinicalGroupsAsync([clinicalGroupId], cancellationToken).ConfigureAwait(false)).FirstOrDefault();

    public async Task<IReadOnlyList<CatalogueClinicalGroupInfo>> GetClinicalGroupsAsync(IReadOnlyCollection<Guid> clinicalGroupIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(clinicalGroupIds);
        var ids = clinicalGroupIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        return await context.Set<ClinicalGroup>().AsNoTracking().Where(group => ids.Contains(group.Id))
            .Select(group => new CatalogueClinicalGroupInfo(group.Id, group.Code, group.UnitCode, group.Name.En, group.Name.Ar, group.ReportName, group.ReportTemplateId, group.DisplayOrder, group.IsActive))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<CatalogueSampleTypeInfo>> GetSampleTypesAsync(IReadOnlyCollection<Guid> sampleTypeIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sampleTypeIds);
        var ids = sampleTypeIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        var sampleTypes = await context.Set<SampleType>().AsNoTracking().Where(sampleType => ids.Contains(sampleType.Id)).ToListAsync(cancellationToken).ConfigureAwait(false);
        return sampleTypes.Select(sampleType => new CatalogueSampleTypeInfo(
            sampleType.Id,
            sampleType.Code,
            sampleType.ShortName,
            sampleType.Name.En,
            sampleType.Name.Ar,
            sampleType.IsActive,
            sampleType.Conditions.OrderBy(condition => condition.Code, StringComparer.Ordinal)
                .Select(condition => new CatalogueSampleConditionInfo(condition.Id, condition.Code, condition.Name.En, condition.Name.Ar, condition.IsActive, condition.MergesIntoConditionId)).ToList())).ToList();
    }

    public async Task<CatalogueRangeBandInfo?> ResolveRangeAsync(Guid testId, GenderRestriction sex, PatientAge age, Guid? patientConditionId, CancellationToken cancellationToken)
    {
        var test = await GetTestAsync(testId, cancellationToken).ConfigureAwait(false);
        return test is null ? null : ReferenceRangeResolver.Instance.Resolve(test.RangeBands, sex, age, patientConditionId);
    }

    private static CatalogueServiceInfo ToInfo(Service service) => new(
        service.Id,
        service.Code,
        service.ShortName,
        service.ShortcutNo,
        service.Name.En,
        service.Name.Ar,
        service.ReportName,
        service.Type,
        service.ClinicalGroupId,
        service.SampleTypeId,
        service.SampleConditionId,
        service.Gender,
        service.TurnaroundHours,
        service.LabelCount,
        service.Precaution,
        service.PrintAlone,
        service.IsOrderable,
        service.ShowOnWeb,
        service.ProfileSum,
        service.Sequence,
        service.IsActive,
        service.Members.OrderBy(member => member.Order).Select(member => new CatalogueServiceMemberInfo(member.Kind, member.MemberId, member.Order, member.InSum, member.Visible)).ToList());

    private async Task<IReadOnlyList<CatalogueTestInfo>> ToInfosAsync(List<LabTest> tests, CancellationToken cancellationToken)
    {
        var unitIds = tests.Where(test => test.UnitId is not null).Select(test => test.UnitId!.Value).Distinct().ToList();
        var units = unitIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await context.Set<MeasurementUnit>().AsNoTracking().Where(unit => unitIds.Contains(unit.Id)).ToDictionaryAsync(unit => unit.Id, unit => unit.Name, cancellationToken).ConfigureAwait(false);

        return tests.Select(test => new CatalogueTestInfo(
            test.Id,
            test.Code,
            test.Name.En,
            test.Name.Ar,
            test.ReportName,
            test.Type,
            test.ClinicalGroupId,
            test.SampleTypeId,
            test.SampleConditionId,
            test.UnitId,
            test.UnitId is { } unitId ? units.GetValueOrDefault(unitId) : null,
            test.Gender,
            test.Methodology,
            test.Decimals,
            test.IsHiddenHelper,
            test.PrintReference,
            test.LabelCount,
            test.Prefix,
            test.Timing,
            test.FormulaExpression,
            test.FormulaReferences,
            test.RangeBands.OrderBy(band => band.DisplayOrder).Select(band => new CatalogueRangeBandInfo(
                band.Sex, band.AgeFrom, band.AgeTo, band.AgeUnit, band.PatientConditionId, band.Low, band.High, band.PanicLow, band.PanicHigh,
                band.ReportableLow, band.ReportableHigh, band.TextRange, band.DisplayOrder)).ToList(),
            test.ListValues.OrderBy(value => value.DisplayOrder).Select(value => new CatalogueListValueInfo(value.Value, value.IsNormal, value.IsCritical, value.IsDefault, value.IsPlaceholder, value.Colour, value.DisplayOrder)).ToList(),
            test.Paragraphs.OrderBy(paragraph => paragraph.DisplayOrder).Select(paragraph => new CatalogueParagraphInfo(paragraph.Title, paragraph.Text, paragraph.DisplayOrder)).ToList(),
            test.IsActive)).ToList();
    }
}
