using Platform.SharedKernel.MultiTenancy;
using Platform.SharedKernel.Primitives;

namespace Platform.Domain.Billing;

/// <summary>
/// The money of one visit (intake LIS-INT-BIL-001 §5): the consistency boundary of BR-14 and BR-26, because "amount ≤
/// remaining" spans every payment and refund of the visit. Its id is the visit id; it is opened by the first movement.
/// Every transition updates the running totals on the root row, so the <c>xmin</c> token serialises two cashiers working
/// on the same visit (CONCURRENCY-409) and a concurrent first opening fails on the key (UNIQUE-409). The patient share
/// comes from Registration (<c>app.visits</c>) and is handed in by the handler at the moment of the movement.
/// </summary>
public sealed class VisitAccount : AggregateRoot, ITenantScoped
{
    private readonly List<Payment> _payments = [];
    private readonly List<Refund> _refunds = [];

    private VisitAccount()
    {
    }

    private VisitAccount(Guid visitId)
        : base(visitId)
    {
    }

    public Guid TenantId { get; private set; }

    /// <summary>Σ posted payments.</summary>
    public decimal PaidTotal { get; private set; }

    /// <summary>Σ refunds.</summary>
    public decimal RefundedTotal { get; private set; }

    public uint RowVersion { get; private set; }

    public Guid VisitId => Id;

    public IReadOnlyCollection<Payment> Payments => _payments.AsReadOnly();

    public IReadOnlyCollection<Refund> Refunds => _refunds.AsReadOnly();

    /// <summary>Opens the account of a visit (by the first payment or refund); the handler checks that the visit exists (BIL-404).</summary>
    public static VisitAccount Open(Guid visitId)
    {
        Require(visitId != Guid.Empty, BillingCodes.VisitNotFound, "A visit account needs a visit.", DomainFault.Validation);
        return new VisitAccount(visitId);
    }

    public VisitBalanceFigures BalanceFor(decimal patientShareTotal) => new(patientShareTotal, PaidTotal, RefundedTotal);

    /// <summary>
    /// The guards of T1 without recording anything, so a handler can refuse before it draws a receipt number (numbers
    /// are never reused, P-12): not billed through claims (BIL-015), station usable (BR-27: BIL-006/021/022/020), amount
    /// valid (BIL-001/002) and within the remaining balance (BIL-010, BR-26), reference for non-cash methods (BIL-003).
    /// </summary>
    public void CheckPayment(decimal amount, PaymentMethod method, string? reference, CashierStation station, VisitCharge charge) =>
        GuardPayment(amount, method, reference, station, charge);

    /// <summary>
    /// T1 Record payment (<c>billing.create</c>; T4 per member with <paramref name="batchId"/>): the guards of
    /// <see cref="CheckPayment"/>, then the Posted payment; raises <see cref="PaymentRecorded"/>.
    /// </summary>
    public Payment RecordPayment(string receiptNumber, decimal amount, PaymentMethod method, string? reference, CashierStation station, VisitCharge charge, DateTimeOffset nowUtc, Guid? batchId = null)
    {
        var (device, cleanedReference) = GuardPayment(amount, method, reference, station, charge);
        var number = RequireNumber(receiptNumber, Payment.MaxNumberLength);

        var payment = new Payment(Id, number, amount, method, cleanedReference, station, device, batchId, nowUtc);
        _payments.Add(payment);
        PaidTotal += amount;
        Raise(new PaymentRecorded(payment.Id, Id, number, amount, method.ToString(), station.TreasuryId, station.CashierUserId, station.SessionBranchId, batchId, nowUtc));
        return payment;
    }

    /// <summary>
    /// T2 Void payment (<c>billing.cancel</c>): Posted only (BIL-012), not inside a closed cash closing (BIL-013,
    /// <paramref name="frozenByClosing"/> resolved by the handler), reason required (BIL-005). Raises <see cref="PaymentVoided"/>.
    /// </summary>
    public void VoidPayment(Guid paymentId, Guid actorUserId, string reason, bool frozenByClosing, DateTimeOffset nowUtc)
    {
        var payment = _payments.FirstOrDefault(candidate => candidate.Id == paymentId)
            ?? throw new DomainException(BillingCodes.PaymentNotFound, "Payment not found.", DomainFault.NotFound);
        if (!payment.IsPosted)
        {
            throw new InvalidStateTransitionException(BillingCodes.PaymentNotPosted, "payment", payment.Status.ToString(), "void");
        }

        Require(!frozenByClosing, BillingCodes.PaymentFrozen, "The payment is inside a closed cash closing; record a refund instead.", DomainFault.Conflict);
        var cleaned = (reason ?? string.Empty).Trim();
        Require(cleaned.Length >= 1, BillingCodes.VoidReasonRequired, "A void needs a reason.", DomainFault.Validation);
        Require(cleaned.Length <= Payment.MaxReasonLength, BillingCodes.TextTooLong, $"The reason may have at most {Payment.MaxReasonLength} characters.", DomainFault.Validation);

        payment.Void(actorUserId, cleaned, nowUtc);
        PaidTotal -= payment.Amount;
        Raise(new PaymentVoided(payment.Id, Id, payment.ReceiptNumber, payment.Amount, actorUserId, cleaned, nowUtc));
    }

    /// <summary>
    /// The guards of T3 without recording anything: station usable (BR-27), amount valid and within the refundable amount
    /// (BIL-011, BR-14), a reason (BIL-004; its kind is checked by the handler, BIL-016), reference for non-cash (BIL-003).
    /// </summary>
    public void CheckRefund(decimal amount, PaymentMethod method, string? reference, Guid reasonId, string? note, CashierStation station, VisitCharge charge) =>
        GuardRefund(amount, method, reference, reasonId, note, station, charge);

    /// <summary>T3 Record refund (<c>billing.approve</c>): the guards of <see cref="CheckRefund"/>, then the refund; raises <see cref="RefundRecorded"/>.</summary>
    public Refund RecordRefund(string number, decimal amount, PaymentMethod method, string? reference, Guid reasonId, string? note, CashierStation station, VisitCharge charge, DateTimeOffset nowUtc)
    {
        var (device, cleanedReference, cleanedNote) = GuardRefund(amount, method, reference, reasonId, note, station, charge);
        var cleanedNumber = RequireNumber(number, Refund.MaxNumberLength);

        var refund = new Refund(Id, cleanedNumber, amount, method, cleanedReference, reasonId, cleanedNote, station, device, nowUtc);
        _refunds.Add(refund);
        RefundedTotal += amount;
        Raise(new RefundRecorded(refund.Id, Id, cleanedNumber, amount, method.ToString(), reasonId, station.TreasuryId, station.CashierUserId, station.SessionBranchId, cleanedNote, nowUtc));
        return refund;
    }

    private (string Device, string? Reference) GuardPayment(decimal amount, PaymentMethod method, string? reference, CashierStation station, VisitCharge charge)
    {
        ArgumentNullException.ThrowIfNull(station);
        ArgumentNullException.ThrowIfNull(charge);
        Require(!charge.BilledThroughClaims, BillingCodes.BilledThroughClaims, "This visit belongs to a lab-to-lab batch and is billed to the sending laboratory through claims.", DomainFault.Conflict);
        var device = station.RequireUsable();
        BillingAmounts.RequireValid(amount);
        var cleanedReference = RequireReference(method, reference);
        var remaining = BillingAmounts.Round(BalanceFor(charge.PatientShareTotal).Remaining);
        Require(amount <= remaining, BillingCodes.AmountExceedsRemaining, $"The amount exceeds the remaining balance ({Math.Max(remaining, 0m)}).", DomainFault.Conflict);
        return (device, cleanedReference);
    }

    private (string Device, string? Reference, string? Note) GuardRefund(decimal amount, PaymentMethod method, string? reference, Guid reasonId, string? note, CashierStation station, VisitCharge charge)
    {
        ArgumentNullException.ThrowIfNull(station);
        ArgumentNullException.ThrowIfNull(charge);
        var device = station.RequireUsable();
        BillingAmounts.RequireValid(amount);
        var cleanedReference = RequireReference(method, reference);
        Require(reasonId != Guid.Empty, BillingCodes.RefundReasonRequired, "A refund needs a reason.", DomainFault.Validation);
        var cleanedNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        Require(cleanedNote is null || cleanedNote.Length <= Refund.MaxNoteLength, BillingCodes.TextTooLong, $"The note may have at most {Refund.MaxNoteLength} characters.", DomainFault.Validation);
        var refundable = BillingAmounts.Round(BalanceFor(charge.PatientShareTotal).Refundable);
        Require(amount <= refundable, BillingCodes.AmountExceedsRefundable, $"The amount exceeds the refundable balance ({refundable}).", DomainFault.Conflict);
        return (device, cleanedReference, cleanedNote);
    }

    private static string RequireNumber(string number, int maxLength)
    {
        var cleaned = (number ?? string.Empty).Trim();
        Require(cleaned.Length >= 1 && cleaned.Length <= maxLength, BillingCodes.NumberRequired, "A reference number is required.", DomainFault.Validation);
        return cleaned;
    }

    private static string? RequireReference(PaymentMethod method, string? reference)
    {
        var cleaned = string.IsNullOrWhiteSpace(reference) ? null : reference.Trim();
        Require(method == PaymentMethod.Cash || cleaned is not null, BillingCodes.ReferenceRequired, "A reference is required for every method except cash.", DomainFault.Validation);
        Require(cleaned is null || cleaned.Length <= Payment.MaxReferenceLength, BillingCodes.TextTooLong, $"The reference may have at most {Payment.MaxReferenceLength} characters.", DomainFault.Validation);
        return cleaned;
    }
}
