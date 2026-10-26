namespace Platform.Domain.Registration;

/// <summary>
/// Refusal and warning codes of the Registration module (intake LIS-INT-REG-001 §7–§8). 001–099 validation and
/// privilege refusals, 100–199 conflicts, 201–299 advisory warnings (returned by the planner, never thrown),
/// 404–406 not found.
/// </summary>
public static class RegistrationCodes
{
    public const string PatientNotFound = "REG-001";
    public const string PatientMerged = "REG-002";
    public const string LinesRequired = "REG-003";
    public const string TooManyLines = "REG-004";
    public const string PayerInvalid = "REG-005";
    public const string TierInvalid = "REG-006";
    public const string DoctorRequired = "REG-007";
    public const string DoctorNotSelectable = "REG-008";
    public const string MobileRequired = "REG-009";
    public const string NationalIdRequired = "REG-010";
    public const string DiagnosisInvalid = "REG-011";
    public const string ServiceUnknown = "REG-012";
    public const string ServiceNotOrderable = "REG-013";
    public const string ServiceUnpriced = "REG-014";
    public const string ServiceGenderRestricted = "REG-015";
    public const string PreparationInvalid = "REG-016";
    public const string DiscountReasonRequired = "REG-017";
    public const string DiscountReasonInvalid = "REG-018";
    public const string DiscountInvalid = "REG-019";
    public const string HeaderInvalid = "REG-020";
    public const string CancellationReasonRequired = "REG-021";
    public const string TransferReasonRequired = "REG-022";
    public const string SharesInvalid = "REG-023";
    public const string LineNotOnVisit = "REG-024";
    public const string PackageMemberAction = "REG-025";

    /// <summary>LIS-CR-015: a line ordered for a formula operand follows its formula line; it is not cancelled, released or adjusted alone.</summary>
    public const string FormulaOperandAction = "REG-026";
    public const string BookingAddressRequired = "REG-027";
    public const string BookingPatientRequired = "REG-028";
    public const string BookingTimeInvalid = "REG-029";
    public const string PhlebotomistUnknown = "REG-030";
    public const string SendingLabInvalid = "REG-031";
    public const string BatchMembersInvalid = "REG-032";
    public const string BatchDuplicateMember = "REG-033";
    public const string BatchContractRequired = "REG-034";
    public const string BatchGroupNameRequired = "REG-035";
    public const string ReasonInvalid = "REG-037";
    public const string TransferSameContract = "REG-038";
    public const string NothingToDiscount = "REG-039";

    /// <summary>LIS-CR-008 C-02: a "collected outside" sample entry names no tube of the order (stale tube key or sample type).</summary>
    public const string OutsideSampleUnknown = "REG-040";

    /// <summary>LIS-CR-008 C-02: the time a sample was collected outside lies in the future or more than 30 days back.</summary>
    public const string OutsideCollectedAtInvalid = "REG-041";
    public const string ExportTooLarge = "REG-050";

    /// <summary>Privilege refusals (403): the special rights of the actor's role (Organization, CP-37).</summary>
    public const string DiscountAboveCap = "REG-060";
    public const string CancelRightRequired = "REG-061";
    public const string TransferRightRequired = "REG-062";

    public const string DuplicateService = "REG-100";
    public const string FrequencyPrevented = "REG-101";
    public const string FrequencyConfirmationRequired = "REG-102";
    public const string VisitNotOpen = "REG-103";
    public const string LineNotLive = "REG-104";
    public const string LineNotReserved = "REG-105";
    public const string VerifiedResults = "REG-106";
    public const string BookingNotBooked = "REG-107";
    public const string BookingPatientMismatch = "REG-108";

    /// <summary>Validation O-3: every test the service orders is already on the visit through another service (assumption).</summary>
    public const string DuplicateTests = "REG-109";

    /// <summary>Advisory warnings of the order planner (dry run and registration responses).</summary>
    public const string WarningFrequency = "REG-201";
    public const string WarningContractAlert = "REG-202";
    public const string WarningPrecaution = "REG-203";
    public const string WarningAddedByRule = "REG-204";
    public const string WarningAddedByDiagnosis = "REG-205";

    /// <summary>Validation O-3: the service adds new tests but repeats some already on the visit; the detail names them.</summary>
    public const string WarningRepeatedTests = "REG-206";
    public const string WarningSkippedCompanion = "REG-207";

    public const string VisitNotFound = "REG-404";
    public const string BookingNotFound = "REG-405";
    public const string BatchNotFound = "REG-406";
}
