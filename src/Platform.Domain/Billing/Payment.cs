using Platform.SharedKernel.MultiTenancy;
using Platform.SharedKernel.Primitives;

namespace Platform.Domain.Billing;

/// <summary>
/// A payment on a visit (SRS CP-23, BR-26, BR-27): an entity of <see cref="VisitAccount"/>, stored in <c>app.payments</c>
/// (the MODULES.md §5 contract). Created by <see cref="VisitAccount.RecordPayment"/>; the only later change is the void
/// (T2), so the row is otherwise immutable (INV-BIL-004, enforced again by a trigger).
/// </summary>
public sealed class Payment : Entity, ITenantScoped
{
    public const int MaxNumberLength = 20;
    public const int MaxReferenceLength = 64;
    public const int MaxReasonLength = 500;
    public const string NumberKind = "receipt";
    public const string NumberPrefix = "RCT";

    private Payment()
    {
        ReceiptNumber = string.Empty;
        DeviceId = string.Empty;
    }

    internal Payment(Guid visitId, string receiptNumber, decimal amount, PaymentMethod method, string? reference, CashierStation station, string deviceId, Guid? batchId, DateTimeOffset nowUtc)
        : base(Guid.CreateVersion7())
    {
        VisitId = visitId;
        ReceiptNumber = receiptNumber;
        Amount = amount;
        Method = method;
        Reference = reference;
        TreasuryId = station.TreasuryId;
        DeviceId = deviceId;
        CashierUserId = station.CashierUserId;
        BranchId = station.SessionBranchId;
        BatchId = batchId;
        PaidAtUtc = nowUtc;
        Status = PaymentStatus.Posted;
    }

    public Guid TenantId { get; private set; }

    /// <summary>The visit, and the key of the owning account (<c>visit_accounts.id</c>).</summary>
    public Guid VisitId { get; private set; }

    /// <summary><c>RCT-&lt;year&gt;-&lt;sequence&gt;</c>, unique per tenant (INV-BIL-003).</summary>
    public string ReceiptNumber { get; private set; }

    public decimal Amount { get; private set; }

    public PaymentMethod Method { get; private set; }

    /// <summary>Card slip, POS or transfer number; mandatory for every method except cash (BIL-003).</summary>
    public string? Reference { get; private set; }

    public Guid TreasuryId { get; private set; }

    public string DeviceId { get; private set; }

    public Guid CashierUserId { get; private set; }

    /// <summary>The session branch of the cashier (CP-04), never a request field (pitfall P-30).</summary>
    public Guid BranchId { get; private set; }

    /// <summary>Set when the payment was distributed by a batch pay-all (T4).</summary>
    public Guid? BatchId { get; private set; }

    public DateTimeOffset PaidAtUtc { get; private set; }

    public PaymentStatus Status { get; private set; }

    public string? VoidReason { get; private set; }

    public Guid? VoidedByUserId { get; private set; }

    public DateTimeOffset? VoidedAtUtc { get; private set; }

    public bool IsPosted => Status == PaymentStatus.Posted;

    internal void Void(Guid actorUserId, string reason, DateTimeOffset nowUtc)
    {
        Status = PaymentStatus.Voided;
        VoidReason = reason;
        VoidedByUserId = actorUserId;
        VoidedAtUtc = nowUtc;
    }
}
