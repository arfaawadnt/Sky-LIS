namespace Platform.Domain.Catalogue;

/// <summary>Refusal codes of <see cref="SampleType"/> and its conditions (intake LIS-INT-CAT-001 §7, T4–T6).</summary>
public static class SampleTypeCodes
{
    public const string ShortNameInvalid = "CAT-010";
    public const string ConditionCodeDuplicate = "CAT-011";
    public const string ConditionNotFound = "CAT-012";
    public const string TooManyConditions = "CAT-013";
    public const string CodeExists = "CAT-101";
    public const string NotFound = "CAT-191";

    // Compatible conditions (LIS-CR-015 M4): a condition merges into the tube of another condition of the same type.

    /// <summary>A condition of the compatibility map (or its target) is not a condition of this sample type.</summary>
    public const string CompatibleConditionUnknown = "CAT-280";

    /// <summary>A condition cannot merge into itself.</summary>
    public const string CompatibleConditionSelf = "CAT-281";

    /// <summary>The target condition is inactive.</summary>
    public const string CompatibleTargetInactive = "CAT-282";

    /// <summary>The target condition merges into another condition (one level only).</summary>
    public const string CompatibleTargetMerges = "CAT-283";

    /// <summary>A condition that others merge into cannot merge into another condition.</summary>
    public const string CompatibleSourceIsTarget = "CAT-284";

    /// <summary>A condition is listed twice in the compatibility map.</summary>
    public const string CompatibleConditionListedTwice = "CAT-285";
}
