namespace Platform.Contracts.Catalogue;

// Compatibility groups of samples (change request LIS-CR-015, note M4): sample types / conditions set together so their
// tests share one sample in a visit (per processing unit); the sample takes the name of the first member ordered.

/// <summary>A member as the editor sends it: a sample type with one of its conditions, or with none (null). Its priority is its position.</summary>
public sealed record CompatibilityMemberItem(Guid SampleTypeId, Guid? SampleConditionId);

public sealed record CreateSampleCompatibilityGroupRequest(string Code, string NameEn, string NameAr, IReadOnlyList<CompatibilityMemberItem> Members);

/// <summary>Names and the full ordered member list; the code is immutable.</summary>
public sealed record UpdateSampleCompatibilityGroupRequest(string NameEn, string NameAr, IReadOnlyList<CompatibilityMemberItem> Members);

/// <summary>A member with the names of its sample type and condition; <paramref name="Available"/> = type and condition active (planning ignores the others).</summary>
public sealed record CompatibilityMemberView(
    Guid SampleTypeId,
    string SampleTypeCode,
    string SampleTypeNameEn,
    string SampleTypeNameAr,
    Guid? SampleConditionId,
    string? SampleConditionCode,
    string? SampleConditionNameEn,
    string? SampleConditionNameAr,
    int Priority,
    bool Available);

public sealed record SampleCompatibilityGroupView(Guid Id, string Code, string NameEn, string NameAr, bool IsActive, IReadOnlyList<CompatibilityMemberView> Members);
