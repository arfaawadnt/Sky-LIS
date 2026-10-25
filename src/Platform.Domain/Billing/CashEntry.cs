using Platform.SharedKernel.MultiTenancy;
using Platform.SharedKernel.Primitives;

namespace Platform.Domain.Billing;

/// <summary>Money leaving the drawer (an expense) or entering it without a visit (a float, other income).</summary>
public enum CashEntryKind
{
    CashOut = 0,
    CashIn = 1,
}

/// <summary>
/// What a cash entry is for (change request LIS-CR-011, C-15). Cash out: a supplier paid, petty cash, a bank deposit, other;
/// cash in: an opening float, other income, other. <see cref="CashEntry.Allows"/> pairs them with the kind (BIL-062).
/// </summary>
public enum CashEntryCategory
{
    Supplier = 0,
    PettyCash = 1,
    BankDeposit = 2,
    OpeningFloat = 3,
    OtherIncome = 4,
    Other = 5,
}

/// <summary>Posted counts in the ledger and the drawer; Voided (with a reason) counts nowhere and is terminal.</summary>
public enum CashEntryStatus
{
    Posted = 0,
    Voided = 1,
}

/// <summary>
/// A cash entry of the cash ledger (SRS CP-70, change request LIS-CR-011 point C-15): money taken out of the drawer (supplier,
/// petty cash, bank deposit) or put into it (opening float, other income) without a visit. The cashier is the session user
/// and the branch the session branch (pitfall P-30); the entry is posted at the moment it is recorded and never edited — the
/// only later change is the void with a reason (<c>billing.approve</c>). Nothing is posted or voided inside a closed cash
/// closing of the cashier (BIL-061): closed periods are read-only. Only entries of kind cash move the drawer
/// (<see cref="CashEffect"/>); another method is recorded for the ledger's completeness.
/// </summary>
public sealed class CashEntry : AggregateRoot, ITenantScoped
{
    public const int MaxNumberLength = 20;
    public const int MaxReasonLength = 200;
    public const int MaxNoteLength = 500;
    public const int MaxVoidReasonLength = 500;
    public const string NumberKind = "cash-entry";
    public const string NumberPrefix = "CSH";

    private CashEntry()
    {
        Number = string.Empty;
        Reason = string.Empty;
    }

    private CashEntry(string number, CashEntryKind kind, CashEntryCategory category, decimal amount, PaymentMethodUse method, string? reference, string reason, string? note, Cashier cashier, DateTimeOffset nowUtc)
        : base(Guid.CreateVersion7())
    {
        Number = number;
        Kind = kind;
        Category = category;
        Amount = amount;
        Method = method.Kind;
        MethodCode = method.Code;
        Reference = reference;
        Reason = reason;
        Note = note;
        CashierUserId = cashier.UserId;
        BranchId = cashier.BranchId;
        PostedAtUtc = nowUtc;
        Status = CashEntryStatus.Posted;
    }

    public Guid TenantId { get; private set; }

    /// <summary><c>CSH-&lt;year&gt;-&lt;sequence&gt;</c>, unique per tenant.</summary>
    public string Number { get; private set; }

    public CashEntryKind Kind { get; private set; }

    public CashEntryCategory Category { get; private set; }

    public decimal Amount { get; private set; }

    /// <summary>The money semantics (cash moves the drawer; the other kinds are recorded only).</summary>
    public PaymentMethod Method { get; private set; }

    /// <summary>The laboratory's payment method definition at posting time; null when the request named the kind only.</summary>
    public string? MethodCode { get; private set; }

    /// <summary>Supplier invoice, deposit slip or transfer number; mandatory when the method requires it (BIL-003).</summary>
    public string? Reference { get; private set; }

    /// <summary>Why the money moved (free text, required: BIL-060).</summary>
    public string Reason { get; private set; }

    public string? Note { get; private set; }

    /// <summary>The session user who recorded the entry.</summary>
    public Guid CashierUserId { get; private set; }

    /// <summary>The session branch (CP-04), never a request field.</summary>
    public Guid BranchId { get; private set; }

    public DateTimeOffset PostedAtUtc { get; private set; }

    public CashEntryStatus Status { get; private set; }

    public string? VoidReason { get; private set; }

    public Guid? VoidedByUserId { get; private set; }

    public DateTimeOffset? VoidedAtUtc { get; private set; }

    public uint RowVersion { get; private set; }

    public bool IsPosted => Status == CashEntryStatus.Posted;

    /// <summary>The signed change of the drawer: + cash in, − cash out, 0 when voided or not cash.</summary>
    public decimal CashEffect => EffectOf(Kind, Method, Status, Amount);

    /// <summary>The drawer rule of <see cref="CashEffect"/> for readers that have the columns only (ledger, closing totals).</summary>
    public static decimal EffectOf(CashEntryKind kind, PaymentMethod method, CashEntryStatus status, decimal amount) =>
        status != CashEntryStatus.Posted || method != PaymentMethod.Cash ? 0m : kind == CashEntryKind.CashIn ? amount : -amount;

    /// <summary>The categories of a kind: cash out = supplier, petty cash, bank deposit, other; cash in = opening float, other income, other.</summary>
    public static bool Allows(CashEntryKind kind, CashEntryCategory category) => (kind, category) switch
    {
        (_, CashEntryCategory.Other) => true,
        (CashEntryKind.CashOut, CashEntryCategory.Supplier or CashEntryCategory.PettyCash or CashEntryCategory.BankDeposit) => true,
        (CashEntryKind.CashIn, CashEntryCategory.OpeningFloat or CashEntryCategory.OtherIncome) => true,
        _ => false,
    };

    /// <summary>The guards of T6 without creating anything, so the handler refuses before it draws an entry number (numbers are never reused, P-12).</summary>
    public static void CheckPost(CashEntryKind kind, CashEntryCategory category, decimal amount, PaymentMethodUse method, string? reference, string? reason, string? note, Cashier cashier, bool insideClosedPeriod) =>
        Guard(kind, category, amount, method, reference, reason, note, cashier, insideClosedPeriod);

    /// <summary>
    /// T6 Post cash entry (<c>billing.create</c>): a known cashier and branch (AUTH-001 / TENANT-004), not inside a closed period
    /// (BIL-061, <paramref name="insideClosedPeriod"/> resolved by the handler), amount valid (BIL-001/002), category of the kind
    /// (BIL-062), reason required (BIL-060), reference when the method requires it (BIL-003), texts within their lengths
    /// (BIL-007). Raises <see cref="CashEntryPosted"/>.
    /// </summary>
    public static CashEntry Post(
        string number,
        CashEntryKind kind,
        CashEntryCategory category,
        decimal amount,
        PaymentMethodUse method,
        string? reference,
        string? reason,
        string? note,
        Cashier cashier,
        bool insideClosedPeriod,
        DateTimeOffset nowUtc)
    {
        var (cleanedReference, cleanedReason, cleanedNote) = Guard(kind, category, amount, method, reference, reason, note, cashier, insideClosedPeriod);
        var cleanedNumber = (number ?? string.Empty).Trim();
        Require(cleanedNumber.Length >= 1 && cleanedNumber.Length <= MaxNumberLength, BillingCodes.NumberRequired, "An entry number is required.", DomainFault.Validation);

        var entry = new CashEntry(cleanedNumber, kind, category, amount, method, cleanedReference, cleanedReason, cleanedNote, cashier, nowUtc);
        entry.Raise(new CashEntryPosted(entry.Id, cleanedNumber, kind.ToString(), category.ToString(), amount, method.Kind.ToString(), cashier.UserId, cashier.BranchId, cleanedReason, nowUtc));
        return entry;
    }

    /// <summary>
    /// T7 Void cash entry (<c>billing.approve</c>): Posted only (BIL-063), not inside a closed period (BIL-061,
    /// <paramref name="frozenByClosing"/> resolved by the handler), reason required (BIL-005). Raises <see cref="CashEntryVoided"/>.
    /// </summary>
    public void Void(Guid actorUserId, string? reason, bool frozenByClosing, DateTimeOffset nowUtc)
    {
        if (!IsPosted)
        {
            throw new InvalidStateTransitionException(BillingCodes.CashEntryNotPosted, "cash entry", Status.ToString(), "void");
        }

        Require(!frozenByClosing, BillingCodes.PeriodClosed, "The entry is inside a closed cash closing; record a new entry instead.", DomainFault.Conflict);
        var cleaned = (reason ?? string.Empty).Trim();
        Require(cleaned.Length >= 1, BillingCodes.VoidReasonRequired, "A void needs a reason.", DomainFault.Validation);
        Require(cleaned.Length <= MaxVoidReasonLength, BillingCodes.TextTooLong, $"The reason may have at most {MaxVoidReasonLength} characters.", DomainFault.Validation);

        Status = CashEntryStatus.Voided;
        VoidReason = cleaned;
        VoidedByUserId = actorUserId;
        VoidedAtUtc = nowUtc;
        Raise(new CashEntryVoided(Id, Number, Kind.ToString(), Amount, actorUserId, cleaned, nowUtc));
    }

    private static (string? Reference, string Reason, string? Note) Guard(
        CashEntryKind kind,
        CashEntryCategory category,
        decimal amount,
        PaymentMethodUse method,
        string? reference,
        string? reason,
        string? note,
        Cashier cashier,
        bool insideClosedPeriod)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(cashier);
        cashier.RequireKnown();
        ClosedPeriods.RequireOpen(insideClosedPeriod);
        BillingAmounts.RequireValid(amount);
        Require(Enum.IsDefined(kind) && Enum.IsDefined(category) && Allows(kind, category), BillingCodes.CashEntryCategoryInvalid, "Choose a category of this kind of entry.", DomainFault.Validation);

        var cleanedReason = (reason ?? string.Empty).Trim();
        Require(cleanedReason.Length >= 1, BillingCodes.CashEntryReasonRequired, "A cash entry needs a reason.", DomainFault.Validation);
        Require(cleanedReason.Length <= MaxReasonLength, BillingCodes.TextTooLong, $"The reason may have at most {MaxReasonLength} characters.", DomainFault.Validation);

        var cleanedReference = string.IsNullOrWhiteSpace(reference) ? null : reference.Trim();
        Require(!method.RequiresReference || cleanedReference is not null, BillingCodes.ReferenceRequired, "A transaction reference is required for this payment method.", DomainFault.Validation);
        Require(cleanedReference is null || cleanedReference.Length <= Payment.MaxReferenceLength, BillingCodes.TextTooLong, $"The reference may have at most {Payment.MaxReferenceLength} characters.", DomainFault.Validation);

        var cleanedNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        Require(cleanedNote is null || cleanedNote.Length <= MaxNoteLength, BillingCodes.TextTooLong, $"The note may have at most {MaxNoteLength} characters.", DomainFault.Validation);
        return (cleanedReference, cleanedReason, cleanedNote);
    }
}

/// <summary>
/// Closed periods are read-only (change request LIS-CR-011, C-15): a payment, refund or cash entry whose moment falls inside a
/// closed cash closing of its cashier and branch is refused with BIL-061. Movements after a close fall into the next period.
/// </summary>
public static class ClosedPeriods
{
    /// <summary>BIL-061 when <paramref name="insideClosedPeriod"/> (the handler asked <c>ICashClosingRepository.CoversAsync</c>).</summary>
    public static void RequireOpen(bool insideClosedPeriod)
    {
        if (insideClosedPeriod)
        {
            throw new DomainException(BillingCodes.PeriodClosed, "This moment is inside a closed cash closing of the cashier; closed periods are read-only.", DomainFault.Conflict);
        }
    }
}
