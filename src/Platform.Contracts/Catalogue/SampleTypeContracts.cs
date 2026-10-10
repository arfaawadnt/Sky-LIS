namespace Platform.Contracts.Catalogue;

// Sample types and their conditions (SRS CP-43).

/// <summary>A condition as the editor sends it; <paramref name="Id"/> is null for a new one (its code is then required and immutable afterwards).</summary>
public sealed record SampleConditionItem(Guid? Id, string Code, string NameEn, string NameAr, bool IsActive);

public sealed record CreateSampleTypeRequest(string Code, string ShortName, string NameEn, string NameAr, IReadOnlyList<SampleConditionItem>? Conditions);

public sealed record UpdateSampleTypeRequest(string ShortName, string NameEn, string NameAr);

/// <summary>The full list of conditions: listed ones are updated or added, omitted existing ones are deactivated.</summary>
public sealed record SaveSampleConditionsRequest(IReadOnlyList<SampleConditionItem> Conditions);

public sealed record SampleTypeListItem(Guid Id, string Code, string ShortName, string NameEn, string NameAr, int ActiveConditionCount, bool IsActive);

public sealed record SampleConditionView(Guid Id, string Code, string NameEn, string NameAr, bool IsActive);

public sealed record SampleTypeDetail(Guid Id, string Code, string ShortName, string NameEn, string NameAr, bool IsActive, IReadOnlyList<SampleConditionView> Conditions);
