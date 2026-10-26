using Platform.Domain.Catalogue;

namespace Platform.Application.Catalogue;

/// <summary>
/// The read port every later module uses to read catalogue half A (MODULES.md §1 "Cross-module reads"): services
/// with their members, sample, group, gender, turnaround, precaution and print rules (Registration, Pricing,
/// Samples); tests with ranges, list values, formula, decimals, unit, hidden flag, delta, frequency and test days
/// (Results, Worklist, Automation); clinical groups and sample types (labels, reports, rights). Reads are bounded to
/// the current tenant and include inactive rows (callers that select decide on <c>IsActive</c>, BR-24).
/// </summary>
public interface ICatalogueLookup
{
    Task<CatalogueServiceInfo?> GetServiceAsync(Guid serviceId, CancellationToken cancellationToken);

    Task<IReadOnlyList<CatalogueServiceInfo>> GetServicesAsync(IReadOnlyCollection<Guid> serviceIds, CancellationToken cancellationToken);

    Task<CatalogueTestInfo?> GetTestAsync(Guid testId, CancellationToken cancellationToken);

    Task<IReadOnlyList<CatalogueTestInfo>> GetTestsAsync(IReadOnlyCollection<Guid> testIds, CancellationToken cancellationToken);

    /// <summary>Tests by code (upper-case), e.g. the inputs of a formula.</summary>
    Task<IReadOnlyList<CatalogueTestInfo>> GetTestsByCodeAsync(IReadOnlyCollection<string> codes, CancellationToken cancellationToken);

    Task<CatalogueClinicalGroupInfo?> GetClinicalGroupAsync(Guid clinicalGroupId, CancellationToken cancellationToken);

    Task<IReadOnlyList<CatalogueClinicalGroupInfo>> GetClinicalGroupsAsync(IReadOnlyCollection<Guid> clinicalGroupIds, CancellationToken cancellationToken);

    Task<IReadOnlyList<CatalogueSampleTypeInfo>> GetSampleTypesAsync(IReadOnlyCollection<Guid> sampleTypeIds, CancellationToken cancellationToken);

    /// <summary>BR-08 for one test: the band that applies to the patient, or <see langword="null"/> (no flag). Uses <see cref="ReferenceRangeResolver"/>.</summary>
    Task<CatalogueRangeBandInfo?> ResolveRangeAsync(Guid testId, GenderRestriction sex, PatientAge age, Guid? patientConditionId, CancellationToken cancellationToken);
}

public sealed record CatalogueServiceMemberInfo(ServiceMemberKind Kind, Guid MemberId, int Order, bool InSum, bool Visible);

public sealed record CatalogueServiceInfo(
    Guid Id,
    string Code,
    string ShortName,
    int? ShortcutNo,
    string NameEn,
    string NameAr,
    string? ReportName,
    ServiceType Type,
    Guid? ClinicalGroupId,
    Guid? SampleTypeId,
    Guid? SampleConditionId,
    GenderRestriction Gender,
    int TurnaroundHours,
    int LabelCount,
    string? Precaution,
    bool PrintAlone,
    bool IsOrderable,
    bool ShowOnWeb,
    decimal? ProfileSum,
    int Sequence,
    bool IsActive,
    IReadOnlyList<CatalogueServiceMemberInfo> Members)
{
    /// <summary>The member tests in report order (test, profile, culture, text report).</summary>
    public IReadOnlyList<Guid> TestIds => Members.Where(member => member.Kind == ServiceMemberKind.Test).OrderBy(member => member.Order).Select(member => member.MemberId).ToList();

    /// <summary>The member services in order (package).</summary>
    public IReadOnlyList<Guid> MemberServiceIds => Members.Where(member => member.Kind == ServiceMemberKind.Service).OrderBy(member => member.Order).Select(member => member.MemberId).ToList();
}

public sealed record CatalogueRangeBandInfo(
    GenderRestriction Sex,
    int AgeFrom,
    int AgeTo,
    RangeAgeUnit AgeUnit,
    Guid? PatientConditionId,
    decimal? Low,
    decimal? High,
    decimal? PanicLow,
    decimal? PanicHigh,
    decimal? ReportableLow,
    decimal? ReportableHigh,
    string? TextRange,
    int DisplayOrder) : IReferenceBand;

public sealed record CatalogueListValueInfo(string Value, bool IsNormal, bool IsCritical, bool IsDefault, bool IsPlaceholder, string? Colour, int DisplayOrder);

public sealed record CatalogueParagraphInfo(string Title, string? Text, int DisplayOrder);

public sealed record CatalogueTestInfo(
    Guid Id,
    string Code,
    string NameEn,
    string NameAr,
    string? ReportName,
    LabTestType Type,
    Guid ClinicalGroupId,
    Guid? SampleTypeId,
    Guid? SampleConditionId,
    Guid? UnitId,
    string? UnitName,
    GenderRestriction Gender,
    string? Methodology,
    int Decimals,
    bool IsHiddenHelper,
    bool PrintReference,
    int LabelCount,
    string? Prefix,
    LabTestTiming Timing,
    string? FormulaExpression,
    IReadOnlyList<string> FormulaReferences,
    IReadOnlyList<CatalogueRangeBandInfo> RangeBands,
    IReadOnlyList<CatalogueListValueInfo> ListValues,
    IReadOnlyList<CatalogueParagraphInfo> Paragraphs,
    bool IsActive);

public sealed record CatalogueClinicalGroupInfo(Guid Id, string Code, string UnitCode, string NameEn, string NameAr, string? ReportName, Guid? ReportTemplateId, int DisplayOrder, bool IsActive);

/// <summary><paramref name="MergesIntoConditionId"/> (LIS-CR-015 M4): the compatible condition of the same type whose tube this condition's tests may join.</summary>
public sealed record CatalogueSampleConditionInfo(Guid Id, string Code, string NameEn, string NameAr, bool IsActive, Guid? MergesIntoConditionId = null);

public sealed record CatalogueSampleTypeInfo(Guid Id, string Code, string ShortName, string NameEn, string NameAr, bool IsActive, IReadOnlyList<CatalogueSampleConditionInfo> Conditions);
