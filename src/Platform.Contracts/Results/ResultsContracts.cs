namespace Platform.Contracts.Results;

// Request and response shapes of the Results module (intake LIS-INT-RES-001 §14). Statuses, flags, sources, test types
// and colours travel as their names. Validation here is shape only; the rules live in the aggregates.

// ---- Entry -------------------------------------------------------------------------------------------------------

public sealed record FormattedParagraphDto(string Title, string Text);

public sealed record CultureSensitivityEntryDto(Guid AntibioticId, string Interpretation, string? Mic);

public sealed record CultureIsolateEntryDto(Guid OrganismId, string? Quantity, string? Comment, IReadOnlyList<CultureSensitivityEntryDto>? Sensitivities);

public sealed record CultureEntryDto(bool NoGrowth, IReadOnlyList<CultureIsolateEntryDto>? Isolates);

/// <summary>A value by test type: <paramref name="NumericValue"/> (or <paramref name="TextValue"/> as a number) for numeric tests, <paramref name="TextValue"/> for text and lists, <paramref name="Paragraphs"/> for formatted text, <paramref name="Culture"/> for cultures.</summary>
public sealed record EnterResultValueRequest(decimal? NumericValue, string? TextValue, IReadOnlyList<FormattedParagraphDto>? Paragraphs, CultureEntryDto? Culture);

public sealed record ResultValueItem(Guid ResultId, decimal? NumericValue, string? TextValue, IReadOnlyList<FormattedParagraphDto>? Paragraphs, CultureEntryDto? Culture);

/// <summary>Save of a grid (Ctrl+S): every changed row of a visit or sample at once.</summary>
public sealed record SaveResultValuesRequest(IReadOnlyList<ResultValueItem> Values);

public sealed record AmendResultRequest(decimal? NumericValue, string? TextValue, IReadOnlyList<FormattedParagraphDto>? Paragraphs, CultureEntryDto? Culture, string Reason);

public sealed record UndoResultRequest(string Reason);

public sealed record AcknowledgeDeltaRequest(string? Comment);

public sealed record ResultTextRequest(string? Text);

/// <summary>Review / verify of several results (the "Review" and "Verify" buttons of a report unit): at most 500 results.</summary>
public sealed record ResultIdsRequest(IReadOnlyList<Guid> ResultIds);

public sealed record ResultRefusal(Guid ResultId, string Code, string Message);

/// <summary>The outcome of a bulk action: the results that changed and those refused with their codes (the others still commit).</summary>
public sealed record BulkResultOutcome(IReadOnlyList<Guid> Changed, IReadOnlyList<ResultRefusal> Refused);

// ---- Results of a visit / of a sample (CP-19, CP-20) --------------------------------------------------------------

public sealed record VisitHeaderView(
    Guid VisitId,
    string Accession,
    DateTimeOffset RegisteredAtUtc,
    Guid BranchId,
    string? BranchNameEn,
    string? BranchNameAr,
    Guid? PayerId,
    string? PayerNameEn,
    string? PayerNameAr,
    Guid? DoctorId,
    string? DoctorNameEn,
    string? DoctorNameAr,
    bool Urgent);

public sealed record PatientHeaderView(
    Guid PatientId,
    string? Number,
    string? FullName,
    string? NameAr,
    string? Gender,
    DateOnly? DateOfBirth,
    int? AgeYears,
    int? AgeMonths,
    int? AgeDays,
    Guid? PatientConditionId,
    string? ConditionNameEn,
    string? ConditionNameAr,
    bool IsVip);

public sealed record UnitSampleView(Guid TubeId, string? SampleNumber, string? Status, DateTimeOffset? CollectedAtUtc, DateTimeOffset? ReceivedAtUtc);

/// <summary>Affordance only: what the current user may do on the unit's results (the server decides again on every action).</summary>
public sealed record ReportUnitRightsView(bool CanEdit, bool CanReview, bool CanVerify, bool CanPrint, bool CanModifyVerified, bool CanPreviewWithoutVerify);

/// <summary>A profile or package banner (BR-04): the lowest status of its member results.</summary>
public sealed record ServiceBannerView(Guid ServiceId, string? Code, string? NameEn, string? NameAr, string? ReportName, string Status, int ResultCount);

public sealed record RangeView(decimal? Low, decimal? High, decimal? PanicLow, decimal? PanicHigh, decimal? ReportableLow, decimal? ReportableHigh, string? Text, string Display);

public sealed record DeltaView(bool Flag, decimal? Percent, decimal? PreviousValue, Guid? PreviousResultId, bool Acknowledged, string? AcknowledgedByName, DateTimeOffset? AcknowledgedAtUtc, string? Comment);

public sealed record ActorStampView(Guid? UserId, string? Name, DateTimeOffset AtUtc);

public sealed record ListOptionView(string Value, bool IsNormal, bool IsCritical, bool IsPlaceholder, string? Colour);

public sealed record ParagraphTemplateView(string Title, string? Text);

public sealed record CultureSensitivityView(Guid AntibioticId, string Code, string Abbreviation, string NameEn, string NameAr, string Interpretation, string? Mic);

public sealed record CultureIsolateView(Guid OrganismId, string OrganismCode, string NameEn, string NameAr, string? Quantity, string? Comment, IReadOnlyList<CultureSensitivityView> Sensitivities);

public sealed record CultureView(bool NoGrowth, IReadOnlyList<CultureIsolateView> Isolates);

public sealed record ResultRowView(
    Guid Id,
    Guid LineId,
    Guid ServiceId,
    Guid TestId,
    string? TestCode,
    string? TestNameEn,
    string? TestNameAr,
    string? ReportName,
    string TestType,
    bool IsHiddenHelper,
    bool IsFormula,
    Guid? TubeId,
    string? SampleNumber,
    string? SampleStatus,
    bool SampleReceived,
    string Status,
    decimal? NumericValue,
    string? TextValue,
    IReadOnlyList<FormattedParagraphDto>? Paragraphs,
    CultureView? Culture,
    string? Unit,
    int Decimals,
    RangeView? Range,
    string? Flag,
    bool IsPanic,
    DeltaView? Delta,
    string Source,
    string? AnalyserCode,
    string? AnalyserFlags,
    ActorStampView? Entered,
    ActorStampView? Reviewed,
    ActorStampView? Verified,
    string? VerifiedByName,
    int PrintCount,
    DateTimeOffset? LastPrintedAtUtc,
    int AmendmentCount,
    string? ReportComment,
    string? InternalNote,
    IReadOnlyList<ListOptionView> ListOptions,
    IReadOnlyList<ParagraphTemplateView> ParagraphTemplates,
    DateTimeOffset ExpectedAtUtc);

public sealed record ReportUnitView(
    string Key,
    Guid ClinicalGroupId,
    string? GroupCode,
    string? GroupNameEn,
    string? GroupNameAr,
    Guid? PrintAloneServiceId,
    string? ServiceNameEn,
    string? ServiceNameAr,
    string Status,
    bool IsPrinted,
    int PrintCount,
    bool HasMissingValues,
    bool HasPanic,
    bool HasDelta,
    IReadOnlyList<UnitSampleView> Samples,
    ReportUnitRightsView Rights,
    IReadOnlyList<ServiceBannerView> Services,
    IReadOnlyList<ResultRowView> Results);

public sealed record ResultSettingsView(bool ReviewBeforeVerify, bool PrintUnverified, string ResultLanguage);

/// <summary>The case of CP-19 / CP-20: header, then one tab per report unit the user may see (<paramref name="HiddenUnits"/> counts the others).</summary>
public sealed record ResultCaseView(VisitHeaderView Visit, PatientHeaderView Patient, IReadOnlyList<ReportUnitView> Units, int HiddenUnits, string? ScannedSampleNumber, ResultSettingsView Settings);

/// <summary>A visit found by the query panel of CP-19.</summary>
public sealed record ResultVisitListItem(
    Guid VisitId,
    string Accession,
    DateTimeOffset RegisteredAtUtc,
    Guid PatientId,
    string? PatientNumber,
    string? PatientName,
    string? PatientNameAr,
    Guid BranchId,
    string Status,
    int Results,
    int Units,
    bool HasPanic,
    bool HasDelta,
    bool AnyPrinted);

public sealed record ResultLogItem(
    Guid Id,
    string Kind,
    string? FromStatus,
    string? ToStatus,
    string? ValueBefore,
    string? ValueAfter,
    string? Source,
    Guid? AnalyserId,
    DateTimeOffset AtUtc,
    Guid? ByUserId,
    string? ByName,
    string? Detail);

public sealed record PreviousResultItem(
    Guid ResultId,
    Guid VisitId,
    string Accession,
    DateTimeOffset RegisteredAtUtc,
    string Status,
    decimal? NumericValue,
    string? TextValue,
    string? Unit,
    string? Flag,
    DateTimeOffset? VerifiedAtUtc);

// ---- Worklist (CP-18) -----------------------------------------------------------------------

public sealed record WorklistItem(
    Guid ResultId,
    Guid VisitId,
    string Accession,
    Guid PatientId,
    string? PatientNumber,
    string? PatientName,
    string? PatientNameAr,
    string? Gender,
    int? AgeYears,
    Guid TestId,
    string? TestCode,
    string? TestNameEn,
    string? TestNameAr,
    Guid ClinicalGroupId,
    string? GroupNameEn,
    string? GroupNameAr,
    string Status,
    Guid? TubeId,
    string? SampleNumber,
    string? SampleStatus,
    DateTimeOffset? CollectedAtUtc,
    DateTimeOffset ExpectedAtUtc,
    long DelaySeconds,
    decimal ConsumedPercent,
    string DelayState,
    bool Urgent,
    string? TextValue,
    string? Flag,
    bool IsPrinted,
    string Source);

public sealed record WorklistCounters(long Requests, long Tests, long Samples, long Delayed);

public sealed record WorklistPage(IReadOnlyList<WorklistItem> Items, int Page, int PageSize, long TotalCount, WorklistCounters Counters, int WarnPercent, int LatePercent);

// ---- Documents --------------------------------------------------------------------------------------------------

public sealed record ReportLetterhead(string LabNameEn, string LabNameAr, string? BranchNameEn, string? BranchNameAr);

public sealed record ReportTemplateInfo(
    Guid TemplateId,
    string NameEn,
    string Language,
    bool ShowQr,
    Guid? LogoFileId,
    Guid? SignatureFileId,
    string? HeaderTextEn,
    string? HeaderTextAr,
    string? FooterTextEn,
    string? FooterTextAr);

public sealed record ReportPatientBlock(string? Number, string? FullName, string? NameAr, string? Gender, int? AgeYears, int? AgeMonths, int? AgeDays, DateOnly? DateOfBirth);

public sealed record ReportVisitBlock(string Accession, DateTimeOffset RegisteredAtUtc, string? PayerNameEn, string? PayerNameAr, string? DoctorNameEn, string? DoctorNameAr);

public sealed record ReportSampleLine(string? SampleNumber, DateTimeOffset? CollectedAtUtc, DateTimeOffset? ReceivedAtUtc);

public sealed record ReportLine(
    Guid ResultId,
    string NameEn,
    string NameAr,
    string TestType,
    string? Value,
    string? Unit,
    string? Range,
    string? Flag,
    bool IsPanic,
    string Status,
    string? Comment,
    IReadOnlyList<FormattedParagraphDto>? Paragraphs,
    CultureView? Culture,
    string? Methodology);

public sealed record ReportSection(Guid ServiceId, string? HeadingEn, string? HeadingAr, bool IsProfile, IReadOnlyList<ReportLine> Lines);

public sealed record ReportSignature(string? VerifiedByName, DateTimeOffset? VerifiedAtUtc, string? ReviewedByName, DateTimeOffset? ReviewedAtUtc);

/// <summary>The laboratory report of one report unit (SRS documents; LDM §5.7, §12): hidden helpers excluded (BR-20).</summary>
public sealed record LaboratoryReportDocument(
    ReportLetterhead Letterhead,
    ReportTemplateInfo? Template,
    string Language,
    string ReportUnitKey,
    string TitleEn,
    string TitleAr,
    ReportPatientBlock Patient,
    ReportVisitBlock Visit,
    IReadOnlyList<ReportSampleLine> Samples,
    IReadOnlyList<ReportSection> Sections,
    ReportSignature Signature,
    bool IsPreliminary,
    bool IsPreview,
    int PrintCount,
    int AmendmentCount,
    string? PrintListNumber,
    string QrValue,
    DateTimeOffset GeneratedAtUtc,
    string? GeneratedBy);

/// <summary>The bench list of the worklist filter (at most 2 000 rows; <paramref name="Truncated"/> says when more matched).</summary>
public sealed record WorklistDocument(ReportLetterhead Letterhead, DateTimeOffset GeneratedAtUtc, string? GeneratedBy, DateTimeOffset? FromUtc, DateTimeOffset? ToUtc, string? GroupNameEn, string? GroupNameAr, IReadOnlyList<WorklistItem> Rows, bool Truncated);

// ---- Entry lookups -----------------------------------------------------------------------------------------------

public sealed record CommentTemplateOption(Guid Id, string ShortName, string TextEn, string TextAr, Guid? ClinicalGroupId);

public sealed record OrganismOption(Guid Id, string Code, string NameEn, string NameAr);

public sealed record AntibioticOption(Guid Id, string Code, string Abbreviation, string NameEn, string NameAr);

public sealed record SensitivityOption(Guid Id, string Code, string NameEn, string NameAr, int DisplayOrder);

public sealed record MicrobiologyLookups(IReadOnlyList<OrganismOption> Organisms, IReadOnlyList<AntibioticOption> Antibiotics, IReadOnlyList<SensitivityOption> Sensitivities);
