namespace Platform.Contracts.Catalogue;

// Units of measurement (SRS CP-44) and patient conditions (SRS CP-45).

public sealed record MeasurementUnitRequest(string Name);

public sealed record MeasurementUnitListItem(Guid Id, string Name, bool IsActive);

public sealed record PatientConditionRequest(string NameEn, string NameAr);

public sealed record PatientConditionListItem(Guid Id, string NameEn, string NameAr, bool IsActive);
