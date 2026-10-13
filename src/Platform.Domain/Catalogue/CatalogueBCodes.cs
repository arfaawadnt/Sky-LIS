namespace Platform.Domain.Catalogue;

/// <summary>
/// Refusal codes of Catalogue B (intake LIS-INT-CTB-001 §7–§8), range CAT-200…CAT-399 (Catalogue A owns the rest):
/// 200–299 validation and list rules, 300–349 conflicts, 380–399 not found.
/// </summary>
public static class CatalogueBCodes
{
    public const string CodeInvalid = "CAT-200";
    public const string TextInvalid = "CAT-201";
    public const string PhoneInvalid = "CAT-202";
    public const string ServiceUnknown = "CAT-203";
    public const string ClinicalGroupUnknown = "CAT-204";
    public const string BranchUnknown = "CAT-205";
    public const string PayerUnknown = "CAT-206";
    public const string ContractTierUnknown = "CAT-207";
    public const string DoctorUnknown = "CAT-208";
    public const string TooManyLinks = "CAT-209";

    public const string PreparationOptionsRequired = "CAT-210";
    public const string PreparationOptionInvalid = "CAT-211";

    public const string SharePercentInvalid = "CAT-220";

    public const string ShortcutSetServicesRequired = "CAT-230";

    public const string TestRuleTriggersRequired = "CAT-240";
    public const string TestRuleAddedRequired = "CAT-241";
    public const string TestRuleServiceOnBothSides = "CAT-242";

    /// <summary>Warning of the rule validation (T5): another active rule has exactly the same triggers.</summary>
    public const string TestRuleDuplicateTriggers = "CAT-243";

    /// <summary>Warning of the rule validation (T5): the rules feed each other back into this rule's triggers.</summary>
    public const string TestRuleChainLoop = "CAT-244";

    /// <summary>Warning of the rule validation (T5): a linked service is inactive.</summary>
    public const string TestRuleInactiveService = "CAT-245";

    public const string DrugTypeUnknown = "CAT-260";
    public const string CommercialNameUnknown = "CAT-261";
    public const string DisplayOrderInvalid = "CAT-262";

    /// <summary>A report-template image (logo or signature) is empty.</summary>
    public const string TemplateImageEmpty = "CAT-270";

    /// <summary>A report-template image exceeds 512 KB.</summary>
    public const string TemplateImageTooLarge = "CAT-271";

    /// <summary>A report-template image is not a PNG, JPEG or WebP image (decided by its bytes).</summary>
    public const string TemplateImageTypeNotAllowed = "CAT-272";

    public const string CodeExists = "CAT-300";
    public const string NameExists = "CAT-301";
    public const string ReferenceInactive = "CAT-310";

    public const string PreparationNotFound = "CAT-380";
    public const string CommentTemplateNotFound = "CAT-381";
    public const string DoctorNotFound = "CAT-382";
    public const string DiagnosisNotFound = "CAT-383";
    public const string SendingLabNotFound = "CAT-384";
    public const string ShortcutSetNotFound = "CAT-385";
    public const string TestRuleNotFound = "CAT-386";
    public const string ReasonNotFound = "CAT-387";
    public const string ReportTemplateNotFound = "CAT-388";
    public const string DrugTypeNotFound = "CAT-389";
    public const string CommercialNameNotFound = "CAT-390";
    public const string AntibioticNotFound = "CAT-391";
    public const string OrganismNotFound = "CAT-392";
    public const string SensitivityValueNotFound = "CAT-393";

    /// <summary>The report template has no logo (or no signature) to show or remove.</summary>
    public const string TemplateImageNotFound = "CAT-394";
}
