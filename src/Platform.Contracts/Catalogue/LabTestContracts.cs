namespace Platform.Contracts.Catalogue;

// Tests (SRS CP-41). Enum values travel as names: Type Numeric|NonNumeric|OptionalList|RestrictedList|Formula|
// FormattedText|Culture, Gender/Sex Any|Male|Female, AgeUnit Days|Months|Years, FrequencyAction Alert|Prevent.

public sealed record CreateLabTestRequest(
    string Code,
    string NameEn,
    string NameAr,
    string? ReportName,
    string? Loinc,
    string? Cpt,
    Guid ClinicalGroupId,
    string Type,
    Guid? SampleTypeId,
    Guid? SampleConditionId,
    Guid? UnitId,
    string Gender,
    string? Methodology,
    int Decimals,
    bool IsHiddenHelper,
    bool PrintReference,
    int LabelCount,
    string? Prefix);

public sealed record UpdateLabTestRequest(
    string NameEn,
    string NameAr,
    string? ReportName,
    string? Loinc,
    string? Cpt,
    Guid ClinicalGroupId,
    string Type,
    Guid? SampleTypeId,
    Guid? SampleConditionId,
    Guid? UnitId,
    string Gender,
    string? Methodology,
    int Decimals,
    bool IsHiddenHelper,
    bool PrintReference,
    int LabelCount,
    string? Prefix);

/// <summary>Turnaround and repeat controls; <paramref name="TestDays"/> is a weekday mask (bit 0 = Sunday … bit 6 = Saturday).</summary>
public sealed record LabTestTimingItem(int DurationHours, int MinDurationHours, int? FrequencyCheckDays, string FrequencyAction, decimal? DeltaPercent, int? DeltaWindowDays, int TestDays);

public sealed record RangeBandItem(
    string Sex,
    int AgeFrom,
    int AgeTo,
    string AgeUnit,
    Guid? PatientConditionId,
    decimal? Low,
    decimal? High,
    decimal? PanicLow,
    decimal? PanicHigh,
    decimal? ReportableLow,
    decimal? ReportableHigh,
    string? TextRange);

public sealed record ReplaceRangeBandsRequest(IReadOnlyList<RangeBandItem> Bands);

public sealed record ListValueItem(string Value, bool IsNormal, bool IsCritical, bool IsDefault, bool IsPlaceholder, string? Colour);

public sealed record ReplaceListValuesRequest(IReadOnlyList<ListValueItem> Values);

/// <summary>An empty or null expression clears the formula.</summary>
public sealed record SetFormulaRequest(string? Expression);

public sealed record ParagraphItem(string Title, string? Text);

public sealed record ReplaceParagraphsRequest(IReadOnlyList<ParagraphItem> Paragraphs);

/// <summary><paramref name="TestId"/> is the test being edited (for the self-reference and cycle checks), or null for a new one.</summary>
public sealed record CheckFormulaRequest(string Expression, Guid? TestId);

/// <summary>The verdict of the formula check: the normalised expression and its references, or the refusal code, message and 1-based position.</summary>
public sealed record FormulaCheckResult(bool Valid, string? Normalized, IReadOnlyList<string> References, string? Code, string? Message, int? Position);

public sealed record LabTestListItem(
    Guid Id,
    string Code,
    string NameEn,
    string NameAr,
    string Type,
    Guid ClinicalGroupId,
    string ClinicalGroupNameEn,
    string ClinicalGroupNameAr,
    string? UnitName,
    string? SampleTypeNameEn,
    string? SampleTypeNameAr,
    string Gender,
    bool IsHiddenHelper,
    bool IsActive);

public sealed record LabTestDetail(
    Guid Id,
    string Code,
    string NameEn,
    string NameAr,
    string? ReportName,
    string? Loinc,
    string? Cpt,
    Guid ClinicalGroupId,
    string Type,
    Guid? SampleTypeId,
    Guid? SampleConditionId,
    Guid? UnitId,
    string Gender,
    string? Methodology,
    int Decimals,
    bool IsHiddenHelper,
    bool PrintReference,
    int LabelCount,
    string? Prefix,
    LabTestTimingItem Timing,
    string? FormulaExpression,
    IReadOnlyList<string> FormulaReferences,
    IReadOnlyList<RangeBandItem> RangeBands,
    IReadOnlyList<ListValueItem> ListValues,
    IReadOnlyList<ParagraphItem> Paragraphs,
    IReadOnlyList<LabTestParentView> ParentServices,
    bool IsActive);

/// <summary>A service that carries the test as a member (CP-41 "parent services", intake T21): its identity, type, activity and the test's order in it.</summary>
public sealed record LabTestParentView(Guid ServiceId, string Code, string NameEn, string NameAr, string Type, bool IsActive, int Order);

/// <summary>The services that are to carry the test after the call (T21): the test joins the new ones and leaves the omitted ones.</summary>
public sealed record SetParentServicesRequest(IReadOnlyList<Guid> ServiceIds);

/// <summary>Lookup row for every module's test picker (active tests only).</summary>
public sealed record TestLookupItem(Guid Id, string Code, string NameEn, string NameAr, string Type, Guid ClinicalGroupId, string ClinicalGroupNameEn, string ClinicalGroupNameAr, string? UnitName);
