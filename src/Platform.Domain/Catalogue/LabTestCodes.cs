namespace Platform.Domain.Catalogue;

/// <summary>Refusal codes of <see cref="LabTest"/> (intake LIS-INT-CAT-001 §7, T13–T19).</summary>
public static class LabTestCodes
{
    public const string ConditionMismatch = "CAT-030";
    public const string ClinicalGroupRequired = "CAT-041";

    public const string DecimalsOutOfRange = "CAT-050";
    public const string RangeBandsNotAllowed = "CAT-051";
    public const string RangeAgeInvalid = "CAT-052";
    public const string RangeLimitsInvalid = "CAT-053";
    public const string RangeBandsOverlap = "CAT-054";
    public const string RangeBandEmpty = "CAT-055";
    public const string ListValuesNotAllowed = "CAT-056";
    public const string ListValueInvalid = "CAT-057";
    public const string DefaultNotPlaceholder = "CAT-058";
    public const string MultipleDefaults = "CAT-059";
    public const string PlaceholderCritical = "CAT-060";

    /// <summary>A list value reads as Normal or as Critical, never both (validation v0.5.0 O-2).</summary>
    public const string ListValueNormalAndCritical = "CAT-061";
    public const string FormulaNotAllowed = "CAT-062";
    public const string FormulaInvalid = "CAT-063";
    public const string FormulaUnknownReference = "CAT-064";
    public const string FormulaSelfReference = "CAT-065";
    public const string FormulaCycle = "CAT-066";
    public const string ParagraphsNotAllowed = "CAT-068";
    public const string ParagraphInvalid = "CAT-069";
    public const string TimingInvalid = "CAT-070";
    public const string TypeChangeBlocked = "CAT-071";

    public const string CodeExists = "CAT-107";
    public const string NotFound = "CAT-195";
}
