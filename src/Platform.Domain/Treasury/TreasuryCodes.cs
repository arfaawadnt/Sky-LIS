namespace Platform.Domain.Treasury;

/// <summary>Refusal codes of the Treasury module (intake LIS-INT-TRS-001, sections 7–9).</summary>
public static class TreasuryCodes
{
    public const string CodeInvalid = "TRS-001";
    public const string NameRequired = "TRS-002";
    public const string BranchRequired = "TRS-003";
    public const string StatusInvalid = "TRS-005";

    public const string TreasuryInactive = "TRS-010";
    public const string DeviceRequired = "TRS-011";
    public const string LiveRequestExists = "TRS-012";
    public const string RequestNotPending = "TRS-013";
    public const string RequestNotApproved = "TRS-014";

    /// <summary>The treasury belongs to a branch the requester is not assigned to (validation v0.5.0 O-4; intake Q3, an assumption).</summary>
    public const string TreasuryOutsideUserBranches = "TRS-015";

    public const string RejectReasonRequired = "TRS-020";
    public const string RevokeReasonRequired = "TRS-021";
    public const string NotRequester = "TRS-030";

    public const string CodeExists = "TRS-100";
    public const string TreasuryNotFound = "TRS-404";
    public const string RequestNotFound = "TRS-405";

    /// <summary>SOD-TRS-001: the approver (or rejecter) must not be the requester. Fixed, not configurable.</summary>
    public const string SodApproverIsRequester = "SOD-TRS-001";
}
