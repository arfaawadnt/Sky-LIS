using Platform.SharedKernel.MultiTenancy;
using Platform.SharedKernel.Primitives;

namespace Platform.Domain.Registration;

/// <summary>
/// A service ordered on a visit (<c>app.visit_lines</c>, MODULES.md §5 contract). The catalogue identity of the service
/// is a snapshot taken at ordering; prices come from the contract (BR-06) and change only through the transfer flows;
/// the patient share is net of the line's part of the visit discount. Cancelled lines keep everything with reason,
/// actor and time (BR-15). Changed only through <see cref="Visit"/>.
/// </summary>
public sealed class VisitLine : Entity, ITenantScoped
{
    public const int MaxServiceCodeLength = 32;
    public const int MaxServiceNameLength = 400;
    public const int MaxServiceTypeLength = 40;

    private VisitLine()
    {
        ServiceCode = string.Empty;
        ServiceNameEn = string.Empty;
        ServiceNameAr = string.Empty;
        ServiceType = string.Empty;
        TestIds = [];
    }

    private VisitLine(Guid visitId, VisitOrderItem item, VisitLineKind kind, Guid? parentLineId, VisitLineStatus status, int sequence, Guid? collectorUserId, Guid actorUserId, DateTimeOffset nowUtc)
        : base(Guid.CreateVersion7())
    {
        VisitId = visitId;
        ServiceId = item.ServiceId;
        ServiceCode = Clip(item.ServiceCode, MaxServiceCodeLength);
        ServiceNameEn = Clip(item.ServiceNameEn, MaxServiceNameLength);
        ServiceNameAr = Clip(item.ServiceNameAr, MaxServiceNameLength);
        ServiceType = Clip(item.ServiceType, MaxServiceTypeLength);
        Kind = kind;
        ParentLineId = parentLineId;
        Status = status;
        var priced = kind != VisitLineKind.PackageMember;
        ListPrice = priced ? Money(item.ListPrice) : 0m;
        PatientShare = priced ? Money(item.PatientShare) : 0m;
        PayerShare = priced ? Money(item.PayerShare) : 0m;
        ExpectedResultAtUtc = item.ExpectedResultAtUtc;
        TestIds = [.. item.TestIds.Distinct()];
        CollectorUserId = collectorUserId;
        Origin = item.Origin;
        AddedByRuleId = item.AddedByRuleId;
        Sequence = sequence;
        OrderedAtUtc = nowUtc;
        OrderedByUserId = actorUserId;
    }

    public Guid TenantId { get; private set; }

    public Guid VisitId { get; private set; }

    public Guid ServiceId { get; private set; }

    public string ServiceCode { get; private set; }

    public string ServiceNameEn { get; private set; }

    public string ServiceNameAr { get; private set; }

    /// <summary>The catalogue service type name (Test, Profile, Package, Culture, TextReport, Additional).</summary>
    public string ServiceType { get; private set; }

    public VisitLineKind Kind { get; private set; }

    /// <summary>The package line a member belongs to (INV-REG-007).</summary>
    public Guid? ParentLineId { get; private set; }

    /// <summary>The tube (BR-02) of an Ordered sample-carrying line; kept after cancellation for the record.</summary>
    public Guid? TubeId { get; private set; }

    public VisitLineStatus Status { get; private set; }

    public decimal ListPrice { get; private set; }

    /// <summary>Net of <see cref="Discount"/>.</summary>
    public decimal PatientShare { get; private set; }

    public decimal PayerShare { get; private set; }

    /// <summary>This line's part of the visit discount.</summary>
    public decimal Discount { get; private set; }

    public DateTimeOffset? ExpectedResultAtUtc { get; private set; }

    /// <summary>The tests results are created for (BR-12 frequency check reads them too).</summary>
    public Guid[] TestIds { get; private set; }

    /// <summary>Who draws the sample (home visit: the phlebotomist; LDM "Done By").</summary>
    public Guid? CollectorUserId { get; private set; }

    public VisitLineOrigin Origin { get; private set; }

    public Guid? AddedByRuleId { get; private set; }

    public int Sequence { get; private set; }

    public DateTimeOffset OrderedAtUtc { get; private set; }

    public Guid OrderedByUserId { get; private set; }

    public DateTimeOffset? ReleasedAtUtc { get; private set; }

    public Guid? ReleasedByUserId { get; private set; }

    public Guid? CancellationReasonId { get; private set; }

    public string? CancellationReason { get; private set; }

    public Guid? CancelledByUserId { get; private set; }

    public DateTimeOffset? CancelledAtUtc { get; private set; }

    /// <summary>Ordered or Reserved: billed and counted in the totals (BR-05).</summary>
    public bool IsLive => Status != VisitLineStatus.Cancelled;

    /// <summary>The patient share before the discount.</summary>
    public decimal GrossPatientShare => PatientShare + Discount;

    /// <summary>A top-level line carries the price; package members follow their package.</summary>
    public bool IsTopLevel => Kind != VisitLineKind.PackageMember;

    internal static VisitLine Create(Guid visitId, VisitOrderItem item, VisitLineKind kind, Guid? parentLineId, VisitLineStatus status, int sequence, Guid? collectorUserId, Guid actorUserId, DateTimeOffset nowUtc) =>
        new(visitId, item, kind, parentLineId, status, sequence, collectorUserId, actorUserId, nowUtc);

    internal void AssignTube(Guid tubeId) => TubeId = tubeId;

    internal void Release(DateTimeOffset? expectedResultAtUtc, Guid actorUserId, DateTimeOffset nowUtc)
    {
        Status = VisitLineStatus.Ordered;
        ExpectedResultAtUtc = expectedResultAtUtc;
        ReleasedAtUtc = nowUtc;
        ReleasedByUserId = actorUserId;
    }

    internal void Cancel(Guid? reasonId, string reason, Guid actorUserId, DateTimeOffset nowUtc)
    {
        Status = VisitLineStatus.Cancelled;
        CancellationReasonId = reasonId;
        CancellationReason = reason;
        CancelledByUserId = actorUserId;
        CancelledAtUtc = nowUtc;
    }

    /// <summary>New contract prices (transfer); the discount is re-allocated by the visit right after.</summary>
    internal void Reprice(LinePrice price)
    {
        ListPrice = Money(price.ListPrice);
        PatientShare = Money(price.PatientShare);
        PayerShare = Money(price.PayerShare);
        Discount = 0m;
    }

    /// <summary>A share shift that keeps the gross total (INV-REG-009); the discount is re-allocated by the visit right after.</summary>
    internal void SetShares(decimal grossPatientShare, decimal payerShare)
    {
        PatientShare = Money(grossPatientShare);
        PayerShare = Money(payerShare);
        Discount = 0m;
    }

    /// <summary>Applies a newly allocated discount to the gross patient share.</summary>
    internal void ApplyDiscount(decimal discount)
    {
        var gross = GrossPatientShare;
        Discount = Money(discount);
        PatientShare = gross - Discount;
    }

    private static decimal Money(decimal value) => DiscountAllocator.Round(Math.Max(0m, value));

    private static string Clip(string? value, int max)
    {
        var text = (value ?? string.Empty).Trim();
        return text.Length <= max ? text : text[..max];
    }
}
