namespace Platform.Contracts.Catalogue;

// Services (SRS CP-40), service sequence (CP-42) and additional services (CP-55). Enum values travel as their
// names: Type Test|Profile|Package|Culture|TextReport|Additional, Gender Any|Male|Female, Kind Test|Service.

public sealed record ServiceMemberItem(string Kind, Guid MemberId, int Order, bool InSum, bool Visible);

public sealed record CreateServiceRequest(
    string Code,
    string Type,
    string ShortName,
    int? ShortcutNo,
    string NameEn,
    string NameAr,
    string? ReportName,
    string? Loinc,
    string? Cpt,
    Guid? ClinicalGroupId,
    Guid? SampleTypeId,
    Guid? SampleConditionId,
    string? Header,
    IReadOnlyList<string>? Aliases,
    string? Precaution,
    string Gender,
    int TurnaroundHours,
    int LabelCount,
    bool PrintAlone,
    bool IsOrderable,
    bool ShowOnWeb,
    decimal? ProfileSum,
    IReadOnlyList<ServiceMemberItem>? Members);

public sealed record UpdateServiceRequest(
    string? Type,
    string ShortName,
    int? ShortcutNo,
    string NameEn,
    string NameAr,
    string? ReportName,
    string? Loinc,
    string? Cpt,
    Guid? ClinicalGroupId,
    Guid? SampleTypeId,
    Guid? SampleConditionId,
    string? Header,
    IReadOnlyList<string>? Aliases,
    string? Precaution,
    string Gender,
    int TurnaroundHours,
    int LabelCount,
    bool PrintAlone,
    bool IsOrderable,
    bool ShowOnWeb,
    decimal? ProfileSum);

public sealed record ReplaceServiceMembersRequest(IReadOnlyList<ServiceMemberItem> Members);

public sealed record ServiceSequenceChange(Guid ServiceId, int Sequence);

public sealed record SaveServiceSequenceRequest(IReadOnlyList<ServiceSequenceChange> Items);

public sealed record ServiceListItem(
    Guid Id,
    string Code,
    string ShortName,
    int? ShortcutNo,
    string NameEn,
    string NameAr,
    string Type,
    Guid? ClinicalGroupId,
    string? ClinicalGroupNameEn,
    string? ClinicalGroupNameAr,
    Guid? SampleTypeId,
    string? SampleTypeNameEn,
    string? SampleTypeNameAr,
    string Gender,
    int Sequence,
    int MemberCount,
    bool IsOrderable,
    bool PrintAlone,
    bool IsActive);

/// <summary>A member with the names of the test or service it points at; <paramref name="MemberType"/> is the member's own test or service type.</summary>
public sealed record ServiceMemberView(string Kind, Guid MemberId, string Code, string NameEn, string NameAr, string MemberType, bool IsActive, int Order, bool InSum, bool Visible);

public sealed record ServiceDetail(
    Guid Id,
    string Code,
    string Type,
    string ShortName,
    int? ShortcutNo,
    string NameEn,
    string NameAr,
    string? ReportName,
    string? Loinc,
    string? Cpt,
    Guid? ClinicalGroupId,
    Guid? SampleTypeId,
    Guid? SampleConditionId,
    string? Header,
    IReadOnlyList<string> Aliases,
    string? Precaution,
    string Gender,
    int TurnaroundHours,
    int LabelCount,
    bool PrintAlone,
    bool IsOrderable,
    bool ShowOnWeb,
    decimal? ProfileSum,
    int Sequence,
    bool IsActive,
    IReadOnlyList<ServiceMemberView> Members);

public sealed record ServiceSequenceRow(Guid Id, string Code, string NameEn, string NameAr, string Type, Guid? ClinicalGroupId, string? ClinicalGroupNameEn, string? ClinicalGroupNameAr, int Sequence, bool IsActive);

/// <summary>Lookup row for every module's service picker (active services only).</summary>
public sealed record ServiceLookupItem(
    Guid Id,
    string Code,
    string ShortName,
    int? ShortcutNo,
    string NameEn,
    string NameAr,
    string Type,
    Guid? ClinicalGroupId,
    string? ClinicalGroupNameEn,
    string? ClinicalGroupNameAr,
    bool IsOrderable);
