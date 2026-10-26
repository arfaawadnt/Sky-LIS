namespace Platform.Domain.Catalogue;

/// <summary>Refusal codes of <see cref="SampleCompatibilityGroup"/> (change request LIS-CR-015, note M4).</summary>
public static class SampleCompatibilityCodes
{
    /// <summary>A group has fewer than two members.</summary>
    public const string MembersTooFew = "CAT-280";

    /// <summary>A newly listed member's sample type does not exist or is inactive.</summary>
    public const string MemberSampleTypeUnavailable = "CAT-281";

    /// <summary>A newly listed member's condition is not an active condition of its sample type.</summary>
    public const string MemberConditionUnavailable = "CAT-282";

    /// <summary>The same sample type and condition is listed twice in the group.</summary>
    public const string MemberDuplicate = "CAT-283";

    /// <summary>A member is already in another active group of the laboratory (the message names it).</summary>
    public const string MemberInOtherGroup = "CAT-284";

    /// <summary>A group with this code already exists.</summary>
    public const string CodeExists = "CAT-285";

    /// <summary>The group does not exist.</summary>
    public const string NotFound = "CAT-286";

    /// <summary>A group has more members than allowed.</summary>
    public const string MembersTooMany = "CAT-287";
}
