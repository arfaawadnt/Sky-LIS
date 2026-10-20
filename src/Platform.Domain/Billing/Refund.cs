using Platform.SharedKernel.MultiTenancy;
using Platform.SharedKernel.Primitives;

namespace Platform.Domain.Billing;

/// <summary>
/// Money returned on a visit whose balance is negative (SRS CP-24, BR-14; LDM <c>Return Money</c> §10.8): an entity of
/// <see cref="VisitAccount"/> stored in <c>app.refunds</c> (MODULES.md §5). Immutable once recorded (INV-BIL-004).
/// </summary>
public sealed class Refund : Entity, ITenantScoped
{
    public const int MaxNumberLength = 20;
    public const int MaxNoteLength = 500;
    public const string NumberKind = "refund";
    public const string NumberPrefix = "RFD";

    private Refund()
    {
        Number = string.Empty;
    }

    internal Refund(Guid visitId, string number, decimal amount, PaymentMethodUse method, string? reference, Guid reasonId, string? note, Cashier cashier, DateTimeOffset nowUtc)
        : base(Guid.CreateVersion7())
    {
        VisitId = visitId;
        Number = number;
        Amount = amount;
        Method = method.Kind;
        MethodCode = method.Code;
        Reference = reference;
        ReasonId = reasonId;
        Note = note;
        CashierUserId = cashier.UserId;
        BranchId = cashier.BranchId;
        RefundedAtUtc = nowUtc;
    }

    public Guid TenantId { get; private set; }

    public Guid VisitId { get; private set; }

    /// <summary><c>RFD-&lt;year&gt;-&lt;sequence&gt;</c>, unique per tenant.</summary>
    public string Number { get; private set; }

    public decimal Amount { get; private set; }

    public PaymentMethod Method { get; private set; }

    /// <summary>The code of the payment method definition at posting time (change request 2026-10-19); null on legacy rows.</summary>
    public string? MethodCode { get; private set; }

    public string? Reference { get; private set; }

    /// <summary>A catalogue reason of kind Refund (Catalogue B, <c>app.reasons</c>), referenced by id.</summary>
    public Guid ReasonId { get; private set; }

    public string? Note { get; private set; }

    /// <summary>The treasury of a movement posted before the treasury was removed (LIS-CR-004, 2026-10-20); null on every new refund, kept as history.</summary>
    public Guid? TreasuryId { get; private set; }

    /// <summary>The browser device of a movement posted under BR-27 (withdrawn 2026-10-20); null on every new refund, kept as history.</summary>
    public string? DeviceId { get; private set; }

    public Guid CashierUserId { get; private set; }

    public Guid BranchId { get; private set; }

    public DateTimeOffset RefundedAtUtc { get; private set; }
}
