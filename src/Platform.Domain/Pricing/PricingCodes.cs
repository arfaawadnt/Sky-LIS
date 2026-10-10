namespace Platform.Domain.Pricing;

/// <summary>Refusal codes of the Pricing module (intake LIS-INT-PRC-001, sections 7–8).</summary>
public static class PricingCodes
{
    // 001–099: validation and state
    public const string ListCodeInvalid = "PRC-001";
    public const string SearchKeyInvalid = "PRC-002";
    public const string WebUsernameRequired = "PRC-003";
    public const string WebUsernameInvalid = "PRC-004";
    public const string YearInvalid = "PRC-005";
    public const string PriceInvalid = "PRC-006";
    public const string FactorInvalid = "PRC-007";
    public const string FormulaSizeInvalid = "PRC-008";
    public const string FormulaDuplicateList = "PRC-009";
    public const string EffectiveDatesInvalid = "PRC-010";
    public const string CopyOperationInvalid = "PRC-011";
    public const string CopyResultOutOfRange = "PRC-012";
    public const string SharesInvalid = "PRC-013";
    public const string AlertInvalid = "PRC-014";
    public const string ServiceUnknown = "PRC-015";
    public const string DuplicateServiceLine = "PRC-016";

    public const string DefaultPayerLocked = "PRC-020";
    public const string DefaultTierLocked = "PRC-021";

    public const string FormulaRequired = "PRC-030";
    public const string NoActiveList = "PRC-031";
    public const string ServiceNotPriced = "PRC-032";
    public const string PriceNotExcluded = "PRC-033";
    public const string CopySameTier = "PRC-034";
    public const string SourceNotConfigured = "PRC-035";
    public const string TierPayerMismatch = "PRC-036";
    public const string TierRequired = "PRC-037";
    public const string FormulaListUnknown = "PRC-038";

    // 100–199: conflicts with existing data
    public const string SearchKeyExists = "PRC-100";
    public const string ListCodeExists = "PRC-101";
    public const string WebUsernameExists = "PRC-102";
    public const string TierNameExists = "PRC-103";

    // not found
    public const string PayerNotFound = "PRC-404";
    public const string TierNotFound = "PRC-405";
    public const string BaseListNotFound = "PRC-406";
    public const string ContractPricingNotFound = "PRC-407";
}
