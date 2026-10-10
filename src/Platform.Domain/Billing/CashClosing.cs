using Platform.SharedKernel.MultiTenancy;
using Platform.SharedKernel.Primitives;

namespace Platform.Domain.Billing;

/// <summary>
/// The figures of a drawer for a treasury, cashier scope and period (SRS CP-30; LDM <c>Total Daily Income</c> §10.11),
/// computed from the posted payments and the refunds of the period. Stored with the closing as its snapshot.
/// </summary>
/// <param name="OutstandingBalance">Σ positive remaining of the visits touched in the period, as of closing (intake Q7).</param>
/// <param name="DiscountTotal">Σ visit discount of the visits touched in the period (intake Q7).</param>
public sealed record ClosingTotals(
    decimal CashPayments,
    decimal CardPayments,
    decimal PosPayments,
    decimal TransferPayments,
    int PaymentCount,
    decimal CashRefunds,
    decimal CardRefunds,
    decimal PosRefunds,
    decimal TransferRefunds,
    int RefundCount,
    decimal OutstandingBalance,
    decimal DiscountTotal,
    int VisitCount)
{
    public static ClosingTotals Empty { get; } = new(0m, 0m, 0m, 0m, 0, 0m, 0m, 0m, 0m, 0, 0m, 0m, 0);

    public decimal TotalPayments => CashPayments + CardPayments + PosPayments + TransferPayments;

    public decimal TotalRefunds => CashRefunds + CardRefunds + PosRefunds + TransferRefunds;

    /// <summary>BR-16: expected cash = cash payments − cash refunds; card, POS and transfer amounts are not in the drawer.</summary>
    public decimal ExpectedCash => CashPayments - CashRefunds;
}

/// <summary>
/// A reconciled drawer (SRS CP-30, BR-16, WF-09 last step): the totals snapshot of a treasury for one cashier (or all
/// cashiers) and a half-open period <c>[from, to)</c>, the counted cash and the difference. Immutable once created; the
/// payments of its period can no longer be voided (INV-BIL-006). Closed periods never overlap (INV-BIL-005, handler).
/// </summary>
public sealed class CashClosing : AggregateRoot, ITenantScoped
{
    public const int MaxNumberLength = 20;
    public const int MaxNoteLength = 1000;
    public const int MaxPeriodDays = 31;
    public const string NumberKind = "closing";
    public const string NumberPrefix = "CLS";

    private CashClosing()
    {
        Number = string.Empty;
        Totals = ClosingTotals.Empty;
    }

    private CashClosing(string number, Guid treasuryId, Guid branchId, Guid? cashierUserId, DateTimeOffset fromUtc, DateTimeOffset toUtc, ClosingTotals totals, decimal? countedCash, decimal? difference, string? note, Guid closedByUserId, DateTimeOffset nowUtc)
        : base(Guid.CreateVersion7())
    {
        Number = number;
        TreasuryId = treasuryId;
        BranchId = branchId;
        CashierUserId = cashierUserId;
        PeriodFromUtc = fromUtc;
        PeriodToUtc = toUtc;
        Totals = totals;
        ExpectedCash = totals.ExpectedCash;
        CountedCash = countedCash;
        Difference = difference;
        Note = note;
        ClosedByUserId = closedByUserId;
        ClosedAtUtc = nowUtc;
    }

    public Guid TenantId { get; private set; }

    /// <summary><c>CLS-&lt;year&gt;-&lt;sequence&gt;</c>, unique per tenant.</summary>
    public string Number { get; private set; }

    public Guid TreasuryId { get; private set; }

    /// <summary>The treasury's branch (equal to the closer's session branch, BIL-022).</summary>
    public Guid BranchId { get; private set; }

    /// <summary>The cashier whose movements are closed; <see langword="null"/> = every cashier of the treasury.</summary>
    public Guid? CashierUserId { get; private set; }

    public DateTimeOffset PeriodFromUtc { get; private set; }

    /// <summary>Exclusive end of the period.</summary>
    public DateTimeOffset PeriodToUtc { get; private set; }

    public ClosingTotals Totals { get; private set; }

    /// <summary>BR-16, stored for readers of the table (Reporting).</summary>
    public decimal ExpectedCash { get; private set; }

    public decimal? CountedCash { get; private set; }

    /// <summary>Counted − expected; <see langword="null"/> when no count was entered.</summary>
    public decimal? Difference { get; private set; }

    public string? Note { get; private set; }

    public Guid ClosedByUserId { get; private set; }

    public DateTimeOffset ClosedAtUtc { get; private set; }

    public uint RowVersion { get; private set; }

    /// <summary>The period rule shared by the preview and the close: from &lt; to (BIL-030), to not in the future (BIL-031), at most <see cref="MaxPeriodDays"/> days (BIL-033).</summary>
    public static void RequirePeriod(DateTimeOffset fromUtc, DateTimeOffset toUtc, DateTimeOffset nowUtc)
    {
        Require(fromUtc < toUtc, BillingCodes.PeriodInvalid, "The period must start before it ends.", DomainFault.Validation);
        Require(toUtc <= nowUtc, BillingCodes.PeriodInFuture, "The period cannot end in the future.", DomainFault.Validation);
        Require(toUtc - fromUtc <= TimeSpan.FromDays(MaxPeriodDays), BillingCodes.PeriodTooLong, $"A closing covers at most {MaxPeriodDays} days.", DomainFault.Validation);
    }

    /// <summary>The scope rule shared by the preview and the close: the treasury belongs to the session branch (BIL-022) and the period is valid.</summary>
    public static void RequireScope(Guid treasuryBranchId, Guid sessionBranchId, DateTimeOffset fromUtc, DateTimeOffset toUtc, DateTimeOffset nowUtc)
    {
        Require(treasuryBranchId == sessionBranchId, BillingCodes.TreasuryOfAnotherBranch, "The treasury belongs to another branch than the session's.", DomainFault.Refused);
        RequirePeriod(fromUtc, toUtc, nowUtc);
    }

    /// <summary>The guards of T5 without creating anything, so the handler refuses before it draws a closing number.</summary>
    public static void CheckClose(Guid treasuryId, Guid treasuryBranchId, Guid sessionBranchId, DateTimeOffset fromUtc, DateTimeOffset toUtc, ClosingTotals totals, decimal? countedCash, string? note, DateTimeOffset nowUtc) =>
        Guard(treasuryId, treasuryBranchId, sessionBranchId, fromUtc, toUtc, totals, countedCash, note, nowUtc);

    /// <summary>
    /// T5 Close cash drawer (<c>billing.manage</c>): treasury of the session branch (BIL-022), period valid (BIL-030/031/033),
    /// counted cash ≥ 0 with two decimals (BIL-032), a non-zero difference explained by a note (BIL-034). Overlaps are
    /// refused by the handler before (BIL-120). Raises <see cref="CashClosingClosed"/>.
    /// </summary>
    public static CashClosing Close(
        string number,
        Guid treasuryId,
        Guid treasuryBranchId,
        Guid sessionBranchId,
        Guid? cashierUserId,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        ClosingTotals totals,
        decimal? countedCash,
        string? note,
        Guid closedByUserId,
        DateTimeOffset nowUtc)
    {
        var (difference, cleanedNote) = Guard(treasuryId, treasuryBranchId, sessionBranchId, fromUtc, toUtc, totals, countedCash, note, nowUtc);
        var cleanedNumber = (number ?? string.Empty).Trim();
        Require(cleanedNumber.Length >= 1 && cleanedNumber.Length <= MaxNumberLength, BillingCodes.NumberRequired, "A closing number is required.", DomainFault.Validation);

        var closing = new CashClosing(cleanedNumber, treasuryId, treasuryBranchId, cashierUserId, fromUtc, toUtc, totals, countedCash, difference, cleanedNote, closedByUserId, nowUtc);
        closing.Raise(new CashClosingClosed(closing.Id, cleanedNumber, treasuryId, cashierUserId, fromUtc, toUtc, totals.ExpectedCash, countedCash, difference, closedByUserId, cleanedNote, nowUtc));
        return closing;
    }

    private static (decimal? Difference, string? Note) Guard(Guid treasuryId, Guid treasuryBranchId, Guid sessionBranchId, DateTimeOffset fromUtc, DateTimeOffset toUtc, ClosingTotals totals, decimal? countedCash, string? note, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(totals);
        Require(treasuryId != Guid.Empty, BillingCodes.TreasuryNotFound, "A closing needs a treasury.", DomainFault.Validation);
        RequireScope(treasuryBranchId, sessionBranchId, fromUtc, toUtc, nowUtc);
        if (countedCash is { } counted)
        {
            Require(counted >= 0m && counted <= BillingAmounts.Max && BillingAmounts.HasValidScale(counted), BillingCodes.CountedCashInvalid, "The counted cash must be zero or more, with at most two decimals.", DomainFault.Validation);
        }

        var difference = countedCash is { } value ? value - totals.ExpectedCash : (decimal?)null;
        var cleanedNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        Require(cleanedNote is null || cleanedNote.Length <= MaxNoteLength, BillingCodes.TextTooLong, $"The note may have at most {MaxNoteLength} characters.", DomainFault.Validation);
        Require(difference is null or 0m || cleanedNote is not null, BillingCodes.DifferenceNeedsNote, "Explain the difference between counted and expected cash in the note.", DomainFault.Validation);
        return (difference, cleanedNote);
    }

    /// <summary>True when this closing covers a movement of <paramref name="cashierUserId"/> on <paramref name="treasuryId"/> at <paramref name="atUtc"/>.</summary>
    public bool Covers(Guid treasuryId, Guid cashierUserId, DateTimeOffset atUtc) =>
        TreasuryId == treasuryId && (CashierUserId is null || CashierUserId == cashierUserId) && atUtc >= PeriodFromUtc && atUtc < PeriodToUtc;

    /// <summary>INV-BIL-005: same treasury, intersecting periods, intersecting cashier scopes (a specific cashier and "all" intersect).</summary>
    public bool Overlaps(Guid treasuryId, Guid? cashierUserId, DateTimeOffset fromUtc, DateTimeOffset toUtc) =>
        TreasuryId == treasuryId
        && (CashierUserId is null || cashierUserId is null || CashierUserId == cashierUserId)
        && fromUtc < PeriodToUtc && PeriodFromUtc < toUtc;
}
