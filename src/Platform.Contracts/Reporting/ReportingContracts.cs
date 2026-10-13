namespace Platform.Contracts.Reporting;

// Work amount report (change request 2026-10-13 item 3; intake LIS-INT-REP-001 §14b). The same response serves the
// screen, the printed document and (flattened) the CSV export.

/// <summary>
/// One group of the work amount report. <see cref="Visits"/> counts the distinct visits with at least one live line in
/// the group; <see cref="Services"/> the live top-level service lines; <see cref="Gross"/> is the sum of the list prices,
/// <see cref="Discount"/> the allocated discounts, <see cref="CashIncome"/> the patient shares (net of discount),
/// <see cref="CreditIncome"/> the payer shares and <see cref="Net"/> cash + credit. <see cref="Code"/> is the branch,
/// doctor or service code when the group has one.
/// </summary>
public sealed record WorkAmountRow(
    string Key,
    string LabelEn,
    string LabelAr,
    string? Code,
    int Visits,
    int Services,
    decimal Gross,
    decimal Discount,
    decimal CashIncome,
    decimal CreditIncome,
    decimal Net);

/// <summary>The identity of the report as it prints: the laboratory and branch names of the letterhead.</summary>
public sealed record WorkAmountLetterhead(string LabNameEn, string LabNameAr, string? BranchNameEn, string? BranchNameAr);

/// <summary>
/// The work amount report: the period it covers (laboratory-calendar days, inclusive, echoed back after defaults
/// applied), the grouping, the branch, the rows ordered by label (by day for the Day grouping) and the grand total.
/// </summary>
public sealed record WorkAmountReport(
    DateOnly FromDay,
    DateOnly ToDay,
    string GroupBy,
    Guid BranchId,
    string TimeZone,
    WorkAmountLetterhead Letterhead,
    IReadOnlyList<WorkAmountRow> Rows,
    WorkAmountRow Totals,
    string GeneratedBy,
    DateTimeOffset GeneratedAtUtc);
