namespace Platform.Domain.Reporting;

/// <summary>Problem codes of the Reporting module (<c>REP-nnn</c>; intake LIS-INT-REP-001 §16 and change request 2026-10-13 item 3).</summary>
public static class ReportingCodes
{
    /// <summary>The period starts before the earliest day the member may report on (<c>SpecialRights.ReportDaysLimit</c>); refused (403).</summary>
    public const string ReportPeriodBeyondLimit = "REP-001";

    /// <summary>The period is not a day range the report accepts (end before start, or longer than <see cref="MaxReportSpanDays"/> days).</summary>
    public const string ReportPeriodInvalid = "REP-002";

    /// <summary>The branch asked for is neither the session branch nor one the member may select; refused (403).</summary>
    public const string ReportBranchNotAllowed = "REP-003";

    /// <summary>The longest period one work amount report covers (a leap year), whatever the member's days limit.</summary>
    public const int MaxReportSpanDays = 366;
}
