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
}
