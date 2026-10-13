namespace Platform.Domain.Patients;

/// <summary>Refusal codes of the Patients module (intake LIS-INT-PAT-001, sections 7–8).</summary>
public static class PatientCodes
{
    public const string NameInvalid = "PAT-001";
    public const string NameArInvalid = "PAT-002";
    public const string DateOfBirthInvalid = "PAT-005";
    public const string AgeInvalid = "PAT-006";
    public const string AgeOrDateOfBirthRequired = "PAT-007";
    public const string MobileInvalid = "PAT-008";
    public const string MobileRequired = "PAT-009";
    public const string NationalIdInvalid = "PAT-010";
    public const string NationalIdRequired = "PAT-011";
    public const string EmailInvalid = "PAT-012";
    public const string TextTooLong = "PAT-013";

    public const string PatientMerged = "PAT-020";
    public const string MergeWithItself = "PAT-021";
    public const string SurvivorNotActive = "PAT-022";
    public const string MergeReasonRequired = "PAT-024";

    public const string VipRequiresConfidential = "PAT-030";
    public const string VipFilterRequiresConfidential = "PAT-031";

    /// <summary>An identity field (title, names, gender, date of birth or age, condition, national id, nationality) may no longer change: a result of the visit (or of the patient) has passed the status the member's <c>DemographicsEditUpTo</c> right allows (change request 2026-10-13, item 1).</summary>
    public const string DemographicsLockedByResults = "PAT-032";

    public const string DocumentEmpty = "PAT-040";
    public const string DocumentTooLarge = "PAT-041";
    public const string DocumentTypeNotAllowed = "PAT-042";
    public const string DocumentContentMismatch = "PAT-043";
    public const string DocumentFileNameInvalid = "PAT-044";
    public const string DocumentNoteTooLong = "PAT-045";
    public const string DocumentInactive = "PAT-046";
    public const string DeactivationReasonRequired = "PAT-047";
    public const string DocumentContentUnavailable = "PAT-048";
    public const string DocumentInactiveContent = "PAT-049";

    public const string ExportTooLarge = "PAT-050";

    /// <summary>The two files have different genders; the merge needs <c>confirmGenderMismatch</c>.</summary>
    public const string GenderMismatchNotConfirmed = "PAT-100";

    public const string PatientNotFound = "PAT-404";
    public const string DocumentNotFound = "PAT-405";
}
