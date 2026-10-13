namespace Platform.Domain.Results;

/// <summary>Refusal codes of the Results module (intake LIS-INT-RES-001, sections 7–8).</summary>
public static class ResultsCodes
{
    // 001–049: validation and state of a result.
    public const string ReferenceRequired = "RES-001";
    public const string ValueRequired = "RES-002";
    public const string NumberInvalid = "RES-003";
    public const string FormulaNotTyped = "RES-004";
    public const string PlaceholderNotAResult = "RES-005";
    public const string ValueNotInList = "RES-006";
    public const string ParagraphsInvalid = "RES-007";
    public const string CultureInvalid = "RES-008";
    public const string TextTooLong = "RES-009";
    public const string SampleNotReceived = "RES-010";
    public const string NotEditable = "RES-011";
    public const string NotReviewable = "RES-012";
    public const string NotVerifiable = "RES-013";
    public const string NoValue = "RES-014";
    public const string DeltaNotAcknowledged = "RES-015";
    public const string ReviewRequired = "RES-016";
    public const string NothingToUndo = "RES-017";
    public const string PrintedNotUndoable = "RES-018";
    public const string ReasonRequired = "RES-019";
    public const string NoDeltaToAcknowledge = "RES-020";
    public const string NotPrintable = "RES-021";
    public const string PreviewIncomplete = "RES-022";
    public const string NotAmendable = "RES-023";
    public const string CommentLocked = "RES-024";
    public const string CommentTooLong = "RES-025";
    public const string Cancelled = "RES-026";

    // 030–039: print lists.
    public const string PrintListPrinted = "RES-030";
    public const string PrintListEmpty = "RES-031";
    public const string PrintListTooLarge = "RES-032";

    // 040–049: culture references.
    public const string OrganismUnknown = "RES-040";
    public const string AntibioticUnknown = "RES-041";
    public const string SensitivityValueUnknown = "RES-042";

    // 050–059: result rights (CP-37, LDM §8 S R V E P) and special rights.
    public const string NoEditRight = "RES-050";
    public const string NoReviewRight = "RES-051";
    public const string NoVerifyRight = "RES-052";
    public const string NoPrintRight = "RES-053";
    public const string NoSeeRight = "RES-054";
    public const string NoModifyVerifiedRight = "RES-055";

    /// <summary>The report of a VIP patient prints only for a user with the See-VIP special right (F-06, RES Q19).</summary>
    public const string NoSeeVipToPrint = "RES-056";

    // Not found.
    public const string ResultNotFound = "RES-404";
    public const string ReportUnitNotFound = "RES-405";
    public const string PrintListNotFound = "RES-406";
    public const string SampleNotFound = "RES-407";
    public const string VisitNotFound = "RES-408";
}
