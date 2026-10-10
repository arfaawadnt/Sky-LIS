namespace Platform.Domain.Catalogue;

/// <summary>Refusal codes of <see cref="ClinicalGroup"/> (intake LIS-INT-CAT-001 §7, T1–T3).</summary>
public static class ClinicalGroupCodes
{
    public const string UnitCodeInvalid = "CAT-003";
    public const string CodeExists = "CAT-100";
    public const string NotFound = "CAT-190";
}
