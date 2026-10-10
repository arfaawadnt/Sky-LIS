namespace Platform.Contracts.Catalogue;

// Request and response shapes of Catalogue B (intake LIS-INT-CTB-001). Validation here is shape only; rules live in
// the aggregates. Enum values travel as their names (AnswerType, Gender, Kind, Type, Language). Codes are immutable,
// so the update requests carry none.

/// <summary>A service linked to a Catalogue B record, with the labels Catalogue A holds for it.</summary>
public sealed record LinkedServiceView(Guid Id, string Code, string NameEn, string NameAr, bool IsActive);

// CP-46 Preparations

public sealed record CreatePreparationRequest(string Code, string QuestionEn, string QuestionAr, string AnswerType, IReadOnlyList<string>? Options, string Gender, IReadOnlyList<Guid>? ServiceIds);

public sealed record UpdatePreparationRequest(string QuestionEn, string QuestionAr, string AnswerType, IReadOnlyList<string>? Options, string Gender, IReadOnlyList<Guid>? ServiceIds);

/// <summary><paramref name="Services"/> empty = the question is asked for every service.</summary>
public sealed record PreparationListItem(Guid Id, string Code, string QuestionEn, string QuestionAr, string AnswerType, IReadOnlyList<string> Options, string Gender, IReadOnlyList<LinkedServiceView> Services, bool IsActive);

// CP-47 Comment library

public sealed record CreateCommentTemplateRequest(string ShortName, string TextEn, string TextAr, Guid? ClinicalGroupId);

public sealed record UpdateCommentTemplateRequest(string TextEn, string TextAr, Guid? ClinicalGroupId);

public sealed record CommentTemplateListItem(Guid Id, string ShortName, string TextEn, string TextAr, Guid? ClinicalGroupId, string? ClinicalGroupNameEn, string? ClinicalGroupNameAr, bool IsActive);

// CP-48 Doctors

public sealed record CreateDoctorRequest(string Code, string NameEn, string NameAr, string? Specialty, string? Occupation, IReadOnlyList<Guid>? BranchIds, decimal SharePercent, string? Phone);

public sealed record UpdateDoctorRequest(string NameEn, string NameAr, string? Specialty, string? Occupation, IReadOnlyList<Guid>? BranchIds, decimal SharePercent, string? Phone);

/// <summary><paramref name="BranchIds"/> empty = offered in every branch.</summary>
public sealed record DoctorListItem(Guid Id, string Code, string NameEn, string NameAr, string? Specialty, string? Occupation, IReadOnlyList<Guid> BranchIds, decimal SharePercent, string? Phone, bool IsActive);

// CP-49 Diagnoses

public sealed record CreateDiagnosisRequest(string IcdCode, string NameEn, string NameAr, IReadOnlyList<Guid>? ServiceIds);

public sealed record UpdateDiagnosisRequest(string NameEn, string NameAr, IReadOnlyList<Guid>? ServiceIds);

public sealed record DiagnosisListItem(Guid Id, string IcdCode, string NameEn, string NameAr, IReadOnlyList<LinkedServiceView> Services, bool IsActive);

// CP-50 Sending labs

public sealed record SendingLabRequest(string NameEn, string NameAr, Guid PayerId, Guid ContractTierId, string CourierEn, string CourierAr, string? Phone, Guid? DoctorId);

public sealed record SendingLabListItem(Guid Id, string NameEn, string NameAr, Guid PayerId, Guid ContractTierId, string CourierEn, string CourierAr, string? Phone, Guid? DoctorId, string? DoctorNameEn, string? DoctorNameAr, bool IsActive);

// CP-51 Shortcut sets

public sealed record CreateShortcutSetRequest(string Code, string NameEn, string NameAr, IReadOnlyList<Guid> ServiceIds);

public sealed record UpdateShortcutSetRequest(string NameEn, string NameAr, IReadOnlyList<Guid> ServiceIds);

public sealed record ShortcutSetListItem(Guid Id, string Code, string NameEn, string NameAr, IReadOnlyList<LinkedServiceView> Services, bool IsActive);

// CP-52 Test rules

public sealed record TestRuleRequest(string NameEn, string NameAr, Guid? ClinicalGroupId, IReadOnlyList<Guid> TriggerServiceIds, IReadOnlyList<Guid> AddedServiceIds);

public sealed record TestRuleListItem(Guid Id, string NameEn, string NameAr, Guid? ClinicalGroupId, string? ClinicalGroupNameEn, string? ClinicalGroupNameAr, IReadOnlyList<LinkedServiceView> Triggers, IReadOnlyList<LinkedServiceView> Added, bool IsActive);

/// <summary>"Check rule" (T5): <paramref name="RuleId"/> is the rule being edited (excluded from the comparison), null for a new one.</summary>
public sealed record ValidateTestRuleRequest(Guid? RuleId, IReadOnlyList<Guid> TriggerServiceIds, IReadOnlyList<Guid> AddedServiceIds);

/// <summary>One finding: <paramref name="Severity"/> is <c>Error</c> or <c>Warning</c>; the code is a CAT-2xx refusal code.</summary>
public sealed record TestRuleIssue(string Code, string Severity, string Message, Guid? ServiceId, Guid? OtherRuleId);

public sealed record TestRuleValidationResult(bool IsValid, IReadOnlyList<TestRuleIssue> Issues);

// CP-53 Reasons

public sealed record CreateReasonRequest(string Kind, string NameEn, string NameAr);

public sealed record UpdateReasonRequest(string NameEn, string NameAr);

public sealed record ReasonListItem(Guid Id, string Kind, string NameEn, string NameAr, bool IsActive);

// CP-54 Report templates

/// <summary>Header and footer: both languages or none.</summary>
public sealed record ReportTemplateRequest(
    string NameEn,
    string NameAr,
    string Type,
    Guid? LogoFileId,
    Guid? SignatureFileId,
    bool ShowQr,
    string Language,
    string? HeaderTextEn,
    string? HeaderTextAr,
    string? FooterTextEn,
    string? FooterTextAr);

public sealed record ReportTemplateListItem(
    Guid Id,
    string NameEn,
    string NameAr,
    string Type,
    Guid? LogoFileId,
    Guid? SignatureFileId,
    bool ShowQr,
    string Language,
    string? HeaderTextEn,
    string? HeaderTextAr,
    string? FooterTextEn,
    string? FooterTextAr,
    bool IsActive);

// CP-56 / SA-14 Microbiology

public sealed record DrugTypeRequest(string NameEn, string NameAr);

public sealed record DrugTypeListItem(Guid Id, string NameEn, string NameAr, bool IsActive);

public sealed record CommercialNameRequest(Guid DrugTypeId, string NameEn, string NameAr);

public sealed record CommercialNameListItem(Guid Id, Guid DrugTypeId, string DrugTypeNameEn, string DrugTypeNameAr, string NameEn, string NameAr, bool IsActive);

public sealed record CreateAntibioticRequest(string Code, string NameEn, string NameAr, string Abbreviation, Guid? CommercialNameId);

public sealed record UpdateAntibioticRequest(string NameEn, string NameAr, string Abbreviation, Guid? CommercialNameId);

public sealed record AntibioticListItem(Guid Id, string Code, string NameEn, string NameAr, string Abbreviation, Guid? CommercialNameId, string? CommercialNameEn, string? CommercialNameAr, bool IsActive);

public sealed record CreateOrganismRequest(string Code, string NameEn, string NameAr);

public sealed record UpdateOrganismRequest(string NameEn, string NameAr);

public sealed record OrganismListItem(Guid Id, string Code, string NameEn, string NameAr, bool IsActive);

public sealed record CreateSensitivityValueRequest(string Code, string NameEn, string NameAr, int DisplayOrder);

public sealed record UpdateSensitivityValueRequest(string NameEn, string NameAr, int DisplayOrder);

public sealed record SensitivityValueListItem(Guid Id, string Code, string NameEn, string NameAr, int DisplayOrder, bool IsActive);

// Export (CSV, catalogue.export)

/// <summary>A generated file: the SPA saves <paramref name="Content"/> under <paramref name="FileName"/>.</summary>
public sealed record CatalogueBExportFile(string FileName, string ContentType, byte[] Content);
