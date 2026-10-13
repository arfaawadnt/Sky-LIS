using Platform.Contracts.Reporting;

namespace Platform.Application.Reporting;

/// <summary>The bilingual caption of one group of the work amount report, resolved by the handler from the read ports.</summary>
public sealed record WorkAmountGroupLabel(string LabelEn, string LabelAr, string? Code = null);

/// <summary>
/// Folds the live service lines of the work amount report into rows (intake LIS-INT-REP-001 §14b): per group the distinct
/// visits, the service lines, gross (Σ list price), discount, cash income (Σ patient share), credit income (Σ payer share)
/// and net (cash + credit); plus the grand total over every line, whose visit count is distinct across the groups. Pure,
/// so the arithmetic is unit-tested without a database.
/// </summary>
public static class WorkAmountAggregator
{
    public const string TotalKey = "total";
    public const string TotalLabelEn = "Total";
    public const string TotalLabelAr = "الإجمالي";

    /// <summary>
    /// The rows of the report: one per distinct key of <paramref name="keyOf"/>, captioned by <paramref name="labelOf"/>,
    /// ordered by English label then Arabic label (case-insensitive) and key — or by key alone when
    /// <paramref name="orderByKey"/> (the Day grouping: ISO days sort chronologically).
    /// </summary>
    public static IReadOnlyList<WorkAmountRow> Rows(IReadOnlyList<WorkAmountLine> lines, Func<WorkAmountLine, string> keyOf, Func<string, WorkAmountGroupLabel> labelOf, bool orderByKey = false)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(keyOf);
        ArgumentNullException.ThrowIfNull(labelOf);

        var rows = lines
            .GroupBy(keyOf, StringComparer.Ordinal)
            .Select(group =>
            {
                var label = labelOf(group.Key);
                return Row(group.Key, label.LabelEn, label.LabelAr, label.Code, group.ToList());
            });

        return orderByKey
            ? [.. rows.OrderBy(row => row.Key, StringComparer.Ordinal)]
            : [.. rows.OrderBy(row => row.LabelEn, StringComparer.OrdinalIgnoreCase).ThenBy(row => row.LabelAr, StringComparer.OrdinalIgnoreCase).ThenBy(row => row.Key, StringComparer.Ordinal)];
    }

    /// <summary>The grand total row over every line (visits counted once even when a grouping splits them).</summary>
    public static WorkAmountRow Total(IReadOnlyList<WorkAmountLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        return Row(TotalKey, TotalLabelEn, TotalLabelAr, null, lines);
    }

    private static WorkAmountRow Row(string key, string labelEn, string labelAr, string? code, IReadOnlyCollection<WorkAmountLine> lines)
    {
        var cash = lines.Sum(line => line.PatientShare);
        var credit = lines.Sum(line => line.PayerShare);
        return new WorkAmountRow(
            key,
            labelEn,
            labelAr,
            code,
            lines.Select(line => line.VisitId).Distinct().Count(),
            lines.Count,
            lines.Sum(line => line.ListPrice),
            lines.Sum(line => line.Discount),
            cash,
            credit,
            cash + credit);
    }
}
