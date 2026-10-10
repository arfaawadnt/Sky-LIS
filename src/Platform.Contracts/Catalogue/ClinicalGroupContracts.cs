namespace Platform.Contracts.Catalogue;

// Clinical groups (SRS CP-39). Shape only; the rules live in the aggregate.

public sealed record CreateClinicalGroupRequest(string Code, string UnitCode, string NameEn, string NameAr, string? ReportName, Guid? ReportTemplateId, int DisplayOrder);

public sealed record UpdateClinicalGroupRequest(string UnitCode, string NameEn, string NameAr, string? ReportName, Guid? ReportTemplateId, int DisplayOrder);

public sealed record ClinicalGroupListItem(
    Guid Id,
    string Code,
    string UnitCode,
    string NameEn,
    string NameAr,
    string? ReportName,
    Guid? ReportTemplateId,
    int DisplayOrder,
    bool IsActive);
