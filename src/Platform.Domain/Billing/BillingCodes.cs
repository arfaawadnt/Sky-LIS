namespace Platform.Domain.Billing;

/// <summary>Refusal codes of the Billing module (intake LIS-INT-BIL-001, sections 7–8).</summary>
/// <remarks>
/// Retired with the treasury (change request LIS-CR-004, 2026-10-20) and never reused: BIL-006 (device id), BIL-020
/// (treasury not approved, BR-27), BIL-021 (treasury inactive), BIL-022 (treasury of another branch), BIL-409 (treasury
/// not found).
/// </remarks>
public static class BillingCodes
{
    public const string AmountInvalid = "BIL-001";
    public const string AmountScale = "BIL-002";
    public const string ReferenceRequired = "BIL-003";
    public const string RefundReasonRequired = "BIL-004";
    public const string VoidReasonRequired = "BIL-005";
    public const string TextTooLong = "BIL-007";
    public const string NumberRequired = "BIL-008";

    public const string AmountExceedsRemaining = "BIL-010";
    public const string AmountExceedsRefundable = "BIL-011";
    public const string PaymentNotPosted = "BIL-012";
    public const string PaymentFrozen = "BIL-013";
    public const string AmountExceedsBatchRemaining = "BIL-014";
    public const string BilledThroughClaims = "BIL-015";
    public const string RefundReasonInvalid = "BIL-016";

    public const string PeriodInvalid = "BIL-030";
    public const string PeriodInFuture = "BIL-031";
    public const string CountedCashInvalid = "BIL-032";
    public const string PeriodTooLong = "BIL-033";
    public const string DifferenceNeedsNote = "BIL-034";

    /// <summary>The CP-37 special rights of the role (Organization, <c>IUserPrivileges.SpecialRights</c>), checked besides the permission policy.</summary>
    public const string PayRightRequired = "BIL-040";
    public const string RefundRightRequired = "BIL-041";
    public const string CancelRightRequired = "BIL-042";
    public const string PrintInvoiceRightRequired = "BIL-043";

    /// <summary>The collections report over the whole branch needs the CP-37 right "view branch collections" (change request 2026-10-13, item 2).</summary>
    public const string BranchCollectionsRightRequired = "BIL-044";

    /// <summary>A report period starting before the member's earliest allowed day (<c>SpecialRights.ReportDaysLimit</c>).</summary>
    public const string ReportPeriodBeyondLimit = "BIL-045";

    public const string ExportTooLarge = "BIL-050";

    /// <summary>Payment method definitions (change request 2026-10-19, P-C10): code shape, the last active cash method, an unknown or inactive code on a movement, kind and sequence shape.</summary>
    public const string MethodCodeInvalid = "BIL-051";
    public const string LastActiveCashMethod = "BIL-052";
    public const string MethodUnknownOrInactive = "BIL-053";
    public const string MethodSequenceInvalid = "BIL-054";
    public const string MethodKindInvalid = "BIL-055";

    public const string ClosingOverlaps = "BIL-120";
    public const string MethodCodeExists = "BIL-121";

    public const string VisitNotFound = "BIL-404";
    public const string PaymentNotFound = "BIL-405";
    public const string BatchNotFound = "BIL-406";
    public const string RefundNotFound = "BIL-407";
    public const string ClosingNotFound = "BIL-408";
    public const string MethodNotFound = "BIL-410";
}
