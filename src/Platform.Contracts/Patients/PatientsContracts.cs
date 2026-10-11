namespace Platform.Contracts.Patients;

// Request and response shapes of the Patients module (intake LIS-INT-PAT-001). Validation here is shape only; the
// aggregate decides. Enumerations travel as their names: Title Mr|Mrs|Miss|Ms|Dr|Master|Baby|Prof, Gender
// Male|Female, Status Active|Merged, Match Exact|Mobile|Name. Dates of birth are ISO dates (yyyy-MM-dd).

/// <summary>An age in years, months and days.</summary>
public sealed record PatientAgeView(int Years, int Months, int Days);

/// <summary>
/// T1: a date of birth, or an age when the date is unknown (the date wins when both are sent). <paramref name="IsVip"/>
/// requires <c>patients.viewConfidential</c>; send <see langword="null"/> when the field is not shown.
/// </summary>
public sealed record RegisterPatientRequest(
    string Title,
    string FullName,
    string? NameAr,
    string Gender,
    DateOnly? DateOfBirth,
    PatientAgeView? Age,
    Guid? PatientConditionId,
    string? Mobile,
    string? NationalId,
    string? Nationality,
    string? Email,
    Guid? GovernorateId,
    string? City,
    string? Address,
    string? Comments,
    bool? IsVip);

/// <summary>T2: the same fields; <paramref name="IsVip"/> <see langword="null"/> keeps the flag; <paramref name="RowVersion"/> (from the detail) refuses a stale edit with CONCURRENCY-409.</summary>
public sealed record UpdatePatientRequest(
    string Title,
    string FullName,
    string? NameAr,
    string Gender,
    DateOnly? DateOfBirth,
    PatientAgeView? Age,
    Guid? PatientConditionId,
    string? Mobile,
    string? NationalId,
    string? Nationality,
    string? Email,
    Guid? GovernorateId,
    string? City,
    string? Address,
    string? Comments,
    bool? IsVip,
    uint? RowVersion);

/// <summary>The response of T1: the id and the number the file received.</summary>
public sealed record CreatedPatient(Guid Id, string Number);

/// <summary>T3: the route's patient is the survivor; <paramref name="MergedPatientId"/> becomes Merged.</summary>
public sealed record MergePatientsRequest(Guid MergedPatientId, string Reason, bool ConfirmGenderMismatch);

/// <summary>
/// A row of the patient register. <see cref="DateOfBirth"/> is the known or the estimated date (see
/// <see cref="DateOfBirthIsEstimated"/>); <see cref="Age"/> is computed for today. <see cref="IsVip"/> is
/// <see langword="null"/> for users without <c>patients.viewConfidential</c>. <see cref="VisitCount"/> and
/// <see cref="LastVisitAtUtc"/> come from Registration (CP-11 "visit count, last visit"; UAT A-09).
/// </summary>
public sealed record PatientListItem(
    Guid Id,
    string Number,
    string Title,
    string FullName,
    string? NameAr,
    string Gender,
    DateOnly? DateOfBirth,
    bool DateOfBirthIsEstimated,
    PatientAgeView Age,
    string? Mobile,
    string? NationalId,
    Guid? PatientConditionId,
    bool? IsVip,
    string Status,
    Guid? MergedIntoPatientId,
    string? MergedIntoNumber,
    DateOnly RegisteredOn,
    int VisitCount,
    DateTimeOffset? LastVisitAtUtc);

public sealed record PatientMergeView(Guid SurvivorId, string SurvivorNumber, DateTimeOffset MergedAtUtc, Guid MergedByUserId, string? MergedByName, string Reason);

/// <summary>A file merged into this one.</summary>
public sealed record MergedPatientReference(Guid Id, string Number, DateTimeOffset? MergedAtUtc);

public sealed record PatientDetail(
    Guid Id,
    string Number,
    string Title,
    string FullName,
    string? NameAr,
    string Gender,
    DateOnly? DateOfBirth,
    bool DateOfBirthIsEstimated,
    PatientAgeView Age,
    PatientAgeView? StatedAge,
    DateOnly? AgeRecordedOn,
    DateOnly RegisteredOn,
    Guid? PatientConditionId,
    string? Mobile,
    string? NationalId,
    string? Nationality,
    string? Email,
    Guid? GovernorateId,
    string? City,
    string? Address,
    string? Comments,
    bool? IsVip,
    string Status,
    PatientMergeView? Merge,
    IReadOnlyList<MergedPatientReference> MergedFiles,
    DateTimeOffset CreatedAtUtc,
    string? CreatedByName,
    DateTimeOffset? ModifiedAtUtc,
    string? ModifiedByName,
    uint RowVersion);

/// <summary>A similar existing file (BR-01, REG-002). Match: Exact (name and mobile), Mobile (same mobile), Name (similar name).</summary>
public sealed record SimilarPatient(
    Guid Id,
    string Number,
    string Title,
    string FullName,
    string? NameAr,
    string Gender,
    DateOnly? DateOfBirth,
    bool DateOfBirthIsEstimated,
    PatientAgeView Age,
    string? Mobile,
    string? NationalId,
    string Match);

/// <summary>The candidates (at most 10) and whether the tenant shows the warning (<c>similarNameWarning</c>).</summary>
public sealed record SimilarPatientsResponse(bool WarningEnabled, IReadOnlyList<SimilarPatient> Candidates);

/// <summary>The tenant parameters a patient form honours (CP-57).</summary>
public sealed record PatientFormSettings(bool MandatoryMobile, bool MandatoryNationalId, bool SimilarNameWarning);

/// <summary>
/// The summary other modules read through <c>IPatientLookup</c> (Registration, Results): unmasked (the consumer
/// decides what to show). <see cref="SurvivorPatientId"/> is the active file a Merged one ends in.
/// </summary>
public sealed record PatientSummary(
    Guid Id,
    string Number,
    string Title,
    string FullName,
    string? NameAr,
    string Gender,
    DateOnly? DateOfBirth,
    bool DateOfBirthIsEstimated,
    PatientAgeView Age,
    Guid? PatientConditionId,
    bool IsVip,
    string? Mobile,
    string? NationalId,
    string Status,
    Guid? SurvivorPatientId);

/// <summary>
/// A document of a file, or of a file merged into it (<see cref="PatientNumber"/> tells which). Uploaded and
/// deactivated names are the display names as of the action.
/// </summary>
public sealed record PatientDocumentItem(
    Guid Id,
    Guid PatientId,
    string PatientNumber,
    string FileName,
    string ContentType,
    long SizeBytes,
    string? Note,
    Guid UploadedByUserId,
    string? UploadedByName,
    DateTimeOffset UploadedAtUtc,
    bool IsActive,
    string? DeactivatedByName,
    DateTimeOffset? DeactivatedAtUtc,
    string? DeactivationReason);

public sealed record DeactivatePatientDocumentRequest(string Reason);
