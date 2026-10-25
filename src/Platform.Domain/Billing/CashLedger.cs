using Platform.SharedKernel.Primitives;

namespace Platform.Domain.Billing;

// The cash ledger of a branch drawer (SRS CP-70, change request LIS-CR-011 point C-15): a day book of the money that moved
// in and out of one cashier's drawer (or every cashier's) over whole laboratory days — receipts, refunds, cash entries and
// the closings of the drawer — with the opening balance, a running balance and the closing balance. Pure arithmetic over
// the lines the read port returns; the handler decides the scope and the period.

/// <summary>The kind of a ledger line; a closing sorts before a movement of the same instant (its period is half-open).</summary>
public enum CashLedgerLineKind
{
    Closing = 0,
    Receipt = 1,
    Refund = 2,
    CashIn = 3,
    CashOut = 4,
}

/// <summary>
/// One line of the ledger as the read port returns it. <paramref name="IsVoided"/> marks a voided receipt or entry (shown,
/// counted nowhere). A closing line sits at the end of its period with its stored expected and counted cash and difference;
/// <paramref name="CashierUserId"/> is then the closer.
/// </summary>
public sealed record CashLedgerLine(
    CashLedgerLineKind Kind,
    Guid Id,
    string Number,
    DateTimeOffset AtUtc,
    Guid CashierUserId,
    Guid? VisitId,
    PaymentMethod? Method,
    string? MethodCode,
    string? Category,
    string? Text,
    decimal Amount,
    bool IsVoided,
    decimal? Expected = null,
    decimal? Counted = null,
    decimal? Difference = null)
{
    /// <summary>The signed change of the drawer: cash receipts and cash in add, cash refunds and cash out remove; voided and non-cash lines are 0. A closing resets the balance instead (<see cref="CashLedger.Build"/>).</summary>
    public decimal CashEffect => Kind switch
    {
        CashLedgerLineKind.Receipt => !IsVoided && Method == PaymentMethod.Cash ? Amount : 0m,
        CashLedgerLineKind.Refund => Method == PaymentMethod.Cash ? -Amount : 0m,
        CashLedgerLineKind.CashIn => !IsVoided && Method == PaymentMethod.Cash ? Amount : 0m,
        CashLedgerLineKind.CashOut => !IsVoided && Method == PaymentMethod.Cash ? -Amount : 0m,
        _ => 0m,
    };
}

/// <summary>
/// The opening of a ledger: the counted cash (else the expected cash) of the previous closing of the same scope plus the cash
/// moved between the end of that closing and the start of the ledger; 0 (and no closing) when the drawer was never closed.
/// </summary>
public sealed record CashLedgerOpening(decimal Balance, Guid? ClosingId, string? ClosingNumber, DateTimeOffset? ClosedThroughUtc)
{
    public static CashLedgerOpening None { get; } = new(0m, null, null, null);
}

/// <summary>Amounts per method kind.</summary>
public sealed record MethodAmounts(decimal Cash, decimal Card, decimal Pos, decimal Transfer)
{
    public static MethodAmounts Zero { get; } = new(0m, 0m, 0m, 0m);

    public decimal Total => Cash + Card + Pos + Transfer;

    public MethodAmounts Add(PaymentMethod? method, decimal amount) => method switch
    {
        PaymentMethod.Cash => this with { Cash = Cash + amount },
        PaymentMethod.Card => this with { Card = Card + amount },
        PaymentMethod.Pos => this with { Pos = Pos + amount },
        PaymentMethod.Transfer => this with { Transfer = Transfer + amount },
        _ => this,
    };
}

/// <summary>A ledger line with what it did to the drawer: money in, money out and the balance after it.</summary>
public sealed record CashLedgerRow(CashLedgerLine Line, decimal MoneyIn, decimal MoneyOut, decimal Balance);

/// <summary>
/// The figures of a ledger: the opening balance, posted receipts and refunds per method (with counts), the voided receipts
/// and entries (count, amount), cash entries in and out (cash method only) with their count, the count differences found by
/// the closings of the period and the closing balance (= the running balance after the last line).
/// </summary>
public sealed record CashLedgerSummary(
    decimal OpeningBalance,
    MethodAmounts Receipts,
    int ReceiptCount,
    MethodAmounts Refunds,
    int RefundCount,
    int VoidedCount,
    decimal VoidedAmount,
    decimal CashIn,
    decimal CashOut,
    int EntryCount,
    decimal CountDifferences,
    int ClosingCount,
    decimal ClosingBalance)
{
    /// <summary>The cash the period added to the drawer, closings' count differences included.</summary>
    public decimal NetCash => ClosingBalance - OpeningBalance;
}

public static class CashLedger
{
    /// <summary>The ledger reads at most this many lines; a longer period is refused (BIL-064) rather than shown half.</summary>
    public const int MaxLines = 10_000;

    /// <summary>A ledger covers at most this many laboratory days (BIL-033), the longest closing.</summary>
    public const int MaxPeriodDays = CashClosing.MaxPeriodDays;

    /// <summary>BIL-064 when the read port returned more than <see cref="MaxLines"/> lines (it was asked for one more).</summary>
    public static void RequireWithinLimit(int lineCount)
    {
        if (lineCount > MaxLines)
        {
            throw new DomainException(BillingCodes.LedgerTooLarge, $"The ledger is limited to {MaxLines} lines; choose a shorter period.", DomainFault.Validation);
        }
    }

    /// <summary>BIL-033 when the period of whole days <c>[from, to]</c> is longer than <see cref="MaxPeriodDays"/> days.</summary>
    public static void RequirePeriodLength(DateOnly fromDay, DateOnly toDay)
    {
        if (toDay.DayNumber - fromDay.DayNumber + 1 > MaxPeriodDays)
        {
            throw new DomainException(BillingCodes.PeriodTooLong, $"A ledger covers at most {MaxPeriodDays} days.", DomainFault.Validation);
        }
    }

    /// <summary>
    /// Orders the lines (time, a closing before a movement of the same instant, number) and walks them from the opening:
    /// every movement adds its <see cref="CashLedgerLine.CashEffect"/>; a closing with a count sets the balance to the counted
    /// cash (its adjustment is reported as money in or out and summed in <see cref="CashLedgerSummary.CountDifferences"/>).
    /// </summary>
    public static (IReadOnlyList<CashLedgerRow> Rows, CashLedgerSummary Summary) Build(CashLedgerOpening opening, IEnumerable<CashLedgerLine> lines)
    {
        ArgumentNullException.ThrowIfNull(opening);
        ArgumentNullException.ThrowIfNull(lines);

        var ordered = lines
            .OrderBy(line => line.AtUtc)
            .ThenBy(line => line.Kind == CashLedgerLineKind.Closing ? 0 : 1)
            .ThenBy(line => line.Number, StringComparer.Ordinal)
            .ThenBy(line => line.Id)
            .ToList();

        var balance = opening.Balance;
        var receipts = MethodAmounts.Zero;
        var refunds = MethodAmounts.Zero;
        int receiptCount = 0, refundCount = 0, voidedCount = 0, entryCount = 0, closingCount = 0;
        decimal voidedAmount = 0m, cashIn = 0m, cashOut = 0m, differences = 0m;
        var rows = new List<CashLedgerRow>(ordered.Count);

        foreach (var line in ordered)
        {
            decimal change;
            if (line.Kind == CashLedgerLineKind.Closing)
            {
                closingCount++;
                change = line.Counted is { } counted ? counted - balance : 0m;
                differences += change;
            }
            else
            {
                change = line.CashEffect;
                if (line.IsVoided)
                {
                    voidedCount++;
                    voidedAmount += line.Amount;
                }
                else
                {
                    switch (line.Kind)
                    {
                        case CashLedgerLineKind.Receipt:
                            receipts = receipts.Add(line.Method, line.Amount);
                            receiptCount++;
                            break;
                        case CashLedgerLineKind.Refund:
                            refunds = refunds.Add(line.Method, line.Amount);
                            refundCount++;
                            break;
                        case CashLedgerLineKind.CashIn:
                            entryCount++;
                            cashIn += change;
                            break;
                        case CashLedgerLineKind.CashOut:
                            entryCount++;
                            cashOut -= change;
                            break;
                    }
                }
            }

            balance += change;
            rows.Add(new CashLedgerRow(line, change > 0m ? change : 0m, change < 0m ? -change : 0m, balance));
        }

        var summary = new CashLedgerSummary(opening.Balance, receipts, receiptCount, refunds, refundCount, voidedCount, voidedAmount, cashIn, cashOut, entryCount, differences, closingCount, balance);
        return (rows, summary);
    }

    /// <summary>
    /// The period a day close from the ledger would cover: from the later of the ledger's start and the end of the scope's last
    /// closing, up to <paramref name="nowUtc"/>; <see langword="null"/> when that is empty or the ledger lies wholly in the past.
    /// </summary>
    public static (DateTimeOffset FromUtc, DateTimeOffset ToUtc)? CloseWindow(DateTimeOffset ledgerFromUtc, DateTimeOffset ledgerToUtc, CashLedgerOpening opening, IEnumerable<CashLedgerLine> lines, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(opening);
        ArgumentNullException.ThrowIfNull(lines);
        if (nowUtc <= ledgerFromUtc || nowUtc > ledgerToUtc)
        {
            return null;
        }

        var from = ledgerFromUtc;
        if (opening.ClosedThroughUtc is { } openingEnd && openingEnd > from)
        {
            from = openingEnd;
        }

        foreach (var line in lines.Where(line => line.Kind == CashLedgerLineKind.Closing && line.AtUtc > from))
        {
            from = line.AtUtc;
        }

        return from < nowUtc ? (from, nowUtc) : null;
    }
}
