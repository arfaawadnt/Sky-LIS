using Platform.SharedKernel.Primitives;

namespace Platform.Domain.Billing;

// The collections report of the Payments register (change request 2026-10-13, item 2; LDM "Total Daily
// Income" §10.11): the posted payments and the refunds of a period of whole laboratory-calendar days, grouped by
// branch, cashier or payment method, with the amount per method and the net. Pure arithmetic over the cells the read
// port returns; the handler decides the scope (own movements or the whole branch) and the period.

/// <summary>Whose collections the report covers: the current user's own, or every cashier of the session branch.</summary>
public enum CollectionsReportScope
{
    User = 0,
    Branch = 1,
}

/// <summary>One row per branch, per cashier (user) or per payment method.</summary>
public enum CollectionsReportGrouping
{
    Branch = 0,
    User = 1,
    Method = 2,
}

/// <summary>The sums of one (branch, cashier, method) cell: posted payments and refunds with their counts.</summary>
public sealed record CollectionsCell(Guid BranchId, Guid CashierUserId, PaymentMethod Method, decimal Payments, int PaymentCount, decimal Refunds, int RefundCount);

/// <summary>Payments in, refunds out and the net of one method.</summary>
public sealed record MethodFigures(decimal Payments, decimal Refunds)
{
    public static readonly MethodFigures Zero = new(0m, 0m);

    public decimal Net => Payments - Refunds;

    public MethodFigures Add(decimal payments, decimal refunds) => new(Payments + payments, Refunds + refunds);
}

/// <summary>The figures of one row (or of the whole report): per method, counts and totals.</summary>
public sealed record CollectionsFigures(MethodFigures Cash, MethodFigures Card, MethodFigures Pos, MethodFigures Transfer, int PaymentCount, int RefundCount)
{
    public static readonly CollectionsFigures Empty = new(MethodFigures.Zero, MethodFigures.Zero, MethodFigures.Zero, MethodFigures.Zero, 0, 0);

    public decimal TotalPayments => Cash.Payments + Card.Payments + Pos.Payments + Transfer.Payments;

    public decimal TotalRefunds => Cash.Refunds + Card.Refunds + Pos.Refunds + Transfer.Refunds;

    public decimal Net => TotalPayments - TotalRefunds;

    public MethodFigures Of(PaymentMethod method) => method switch
    {
        PaymentMethod.Cash => Cash,
        PaymentMethod.Card => Card,
        PaymentMethod.Pos => Pos,
        PaymentMethod.Transfer => Transfer,
        _ => throw new ArgumentOutOfRangeException(nameof(method), method, "Unknown payment method."),
    };

    public CollectionsFigures Add(CollectionsCell cell)
    {
        ArgumentNullException.ThrowIfNull(cell);
        var counts = this with { PaymentCount = PaymentCount + cell.PaymentCount, RefundCount = RefundCount + cell.RefundCount };
        return cell.Method switch
        {
            PaymentMethod.Cash => counts with { Cash = Cash.Add(cell.Payments, cell.Refunds) },
            PaymentMethod.Card => counts with { Card = Card.Add(cell.Payments, cell.Refunds) },
            PaymentMethod.Pos => counts with { Pos = Pos.Add(cell.Payments, cell.Refunds) },
            PaymentMethod.Transfer => counts with { Transfer = Transfer.Add(cell.Payments, cell.Refunds) },
            _ => throw new ArgumentOutOfRangeException(nameof(cell), cell.Method, "Unknown payment method."),
        };
    }
}

/// <summary>A grouped row: the key is the branch id, the cashier's user id or the method name, by the grouping.</summary>
public sealed record CollectionsGroup(string Key, CollectionsFigures Figures);

public static class CollectionsReport
{
    /// <summary>The laboratory's calendar day of an instant (the UTC day without a zone).</summary>
    public static DateOnly LocalDay(DateTimeOffset nowUtc, TimeZoneInfo? zone) =>
        DateOnly.FromDateTime(zone is null ? nowUtc.UtcDateTime : TimeZoneInfo.ConvertTime(nowUtc, zone).DateTime);

    /// <summary>
    /// The half-open UTC period <c>[from 00:00, (to + 1 day) 00:00)</c> of whole laboratory days: "to" is inclusive, so a
    /// movement at 23:59:59.999 local on that day is counted. BIL-030 when the period ends before it starts.
    /// </summary>
    public static (DateTimeOffset FromUtc, DateTimeOffset ToUtc) Period(DateOnly from, DateOnly to, TimeZoneInfo? zone)
    {
        if (to < from)
        {
            throw new DomainException(BillingCodes.PeriodInvalid, "The period must start before it ends.", DomainFault.Validation);
        }

        return (StartOfDayUtc(from, zone), StartOfDayUtc(to.AddDays(1), zone));
    }

    /// <summary>BIL-045 when the period starts before the member's earliest allowed day (<see langword="null"/> = no limit).</summary>
    public static void RequireWithinLimit(DateOnly from, DateOnly? earliestAllowed)
    {
        if (earliestAllowed is { } earliest && from < earliest)
        {
            throw new DomainException(BillingCodes.ReportPeriodBeyondLimit, $"Your role may report from {earliest:yyyy-MM-dd} onwards only.", DomainFault.Refused);
        }
    }

    /// <summary>The rows of the report (unordered; the handler names and sorts them) and the grand total.</summary>
    public static (IReadOnlyList<CollectionsGroup> Groups, CollectionsFigures Totals) Group(IEnumerable<CollectionsCell> cells, CollectionsReportGrouping grouping)
    {
        ArgumentNullException.ThrowIfNull(cells);
        var groups = new Dictionary<string, CollectionsFigures>(StringComparer.Ordinal);
        var totals = CollectionsFigures.Empty;
        foreach (var cell in cells)
        {
            var key = KeyOf(cell, grouping);
            groups[key] = (groups.TryGetValue(key, out var figures) ? figures : CollectionsFigures.Empty).Add(cell);
            totals = totals.Add(cell);
        }

        return ([.. groups.Select(pair => new CollectionsGroup(pair.Key, pair.Value))], totals);
    }

    private static string KeyOf(CollectionsCell cell, CollectionsReportGrouping grouping) => grouping switch
    {
        CollectionsReportGrouping.Branch => cell.BranchId.ToString("D"),
        CollectionsReportGrouping.User => cell.CashierUserId.ToString("D"),
        CollectionsReportGrouping.Method => cell.Method.ToString(),
        _ => throw new ArgumentOutOfRangeException(nameof(grouping), grouping, "Unknown grouping."),
    };

    /// <summary>
    /// Local midnight as a UTC instant. The offset comes from <see cref="TimeZoneInfo.GetUtcOffset(DateTime)"/>, which answers
    /// the standard offset for a midnight that a spring-forward skips (Egypt switches at 00:00): that is exactly the instant
    /// the shortened day begins, where <c>ConvertTimeToUtc</c> would throw.
    /// </summary>
    private static DateTimeOffset StartOfDayUtc(DateOnly day, TimeZoneInfo? zone)
    {
        var local = DateTime.SpecifyKind(day.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);
        var offset = zone?.GetUtcOffset(local) ?? TimeSpan.Zero;
        return new DateTimeOffset(local, offset).ToUniversalTime();
    }
}
