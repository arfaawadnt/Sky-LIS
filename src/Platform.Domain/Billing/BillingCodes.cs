namespace Platform.Domain.Billing;

/// <summary>Refusal codes of the Billing module (intake LIS-INT-BIL-001, sections 7–8).</summary>
public static class BillingCodes
{
    public const string AmountInvalid = "BIL-001";
    public const string AmountScale = "BIL-002";
    public const string ReferenceRequired = "BIL-003";
    public const string RefundReasonRequired = "BIL-004";
    public const string VoidReasonRequired = "BIL-005";
    public const string DeviceRequired = "BIL-006";
    public const string TextTooLong = "BIL-007";
    public const string NumberRequired = "BIL-008";

    public const string AmountExceedsRemaining = "BIL-010";
    public const string AmountExceedsRefundable = "BIL-011";
    public const string PaymentNotPosted = "BIL-012";
    public const string PaymentFrozen = "BIL-013";
    public const string AmountExceedsBatchRemaining = "BIL-014";
    public const string BilledThroughClaims = "BIL-015";
    public const string RefundReasonInvalid = "BIL-016";

    public const string TreasuryNotApproved = "BIL-020";
    public const string TreasuryInactive = "BIL-021";
    public const string TreasuryOfAnotherBranch = "BIL-022";

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

    public const string ExportTooLarge = "BIL-050";

    public const string ClosingOverlaps = "BIL-120";

    public const string VisitNotFound = "BIL-404";
    public const string PaymentNotFound = "BIL-405";
    public const string BatchNotFound = "BIL-406";
    public const string RefundNotFound = "BIL-407";
    public const string ClosingNotFound = "BIL-408";
    public const string TreasuryNotFound = "BIL-409";
}
