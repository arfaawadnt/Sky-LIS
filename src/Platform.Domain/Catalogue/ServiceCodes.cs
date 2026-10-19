namespace Platform.Domain.Catalogue;

/// <summary>Refusal codes of <see cref="Service"/> (intake LIS-INT-CAT-001 §7, T9–T12).</summary>
public static class ServiceCodes
{
    public const string MembersInvalidForType = "CAT-021";
    public const string MemberKindNotAllowed = "CAT-022";
    public const string DuplicateMember = "CAT-023";
    public const string MemberOrderInvalid = "CAT-024";
    public const string SelfInclusion = "CAT-025";
    public const string NestedPackage = "CAT-026";
    public const string MemberUnavailable = "CAT-027";
    public const string SampleNotAllowed = "CAT-028";
    public const string SampleRequired = "CAT-029";
    public const string ConditionMismatch = "CAT-030";
    public const string SequenceInvalid = "CAT-031";
    public const string ShortcutInvalid = "CAT-032";
    public const string SequenceRepeated = "CAT-033";
    public const string AliasesInvalid = "CAT-034";
    public const string TypeImmutable = "CAT-035";
    public const string MemberTestTypeMismatch = "CAT-036";
    public const string ProfileSumInvalid = "CAT-037";
    public const string ClinicalGroupRequired = "CAT-041";

    public const string CodeExists = "CAT-104";
    public const string SequenceTaken = "CAT-105";
    public const string ShortcutTaken = "CAT-106";
    public const string NotFound = "CAT-194";
}
