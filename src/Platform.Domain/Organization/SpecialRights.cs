using Platform.SharedKernel.Primitives;

namespace Platform.Domain.Organization;

/// <summary>
/// The particular actions a role (or one user, as an override) may take beyond the screen rights (SRS CP-37;
/// LDM manual §12.3 "Special Rights"). Immutable: a change replaces the whole value.
/// <see cref="DiscountCapPercent"/>: <see langword="null"/> = the tenant parameter <c>discountCapPercent</c> applies,
/// <c>0</c> = no discount, 1–100 = the highest discount in percent.
/// </summary>
public sealed class SpecialRights : ValueObject
{
    public const int MaxDiscountCapPercent = 100;

    /// <summary>Nothing granted: no discount, no payment, no refund … The state of a user without a role.</summary>
    public static readonly SpecialRights None = new(0, false, false, false, false, false, false, false, false);

    /// <summary>Everything granted with a cap of 100 %: the provisioned administrator role.</summary>
    public static readonly SpecialRights All = new(MaxDiscountCapPercent, true, true, true, true, true, true, true, true);

    public SpecialRights(
        int? discountCapPercent,
        bool canPay,
        bool canRefund,
        bool canTransfer,
        bool canCancel,
        bool canModifyVerifiedResult,
        bool canPreviewWithoutVerify,
        bool canPrintInvoice,
        bool canSeeVip)
    {
        if (discountCapPercent is < 0 or > MaxDiscountCapPercent)
        {
            throw new DomainException(OrganizationCodes.DiscountCapInvalid, $"The discount cap must be empty (tenant default) or 0 to {MaxDiscountCapPercent} percent.", DomainFault.Validation);
        }

        DiscountCapPercent = discountCapPercent;
        CanPay = canPay;
        CanRefund = canRefund;
        CanTransfer = canTransfer;
        CanCancel = canCancel;
        CanModifyVerifiedResult = canModifyVerifiedResult;
        CanPreviewWithoutVerify = canPreviewWithoutVerify;
        CanPrintInvoice = canPrintInvoice;
        CanSeeVip = canSeeVip;
    }

    /// <summary>Reserved for object materialisers (EF Core owned type).</summary>
    private SpecialRights()
    {
    }

    public int? DiscountCapPercent { get; private set; }

    public bool CanPay { get; private set; }

    public bool CanRefund { get; private set; }

    public bool CanTransfer { get; private set; }

    public bool CanCancel { get; private set; }

    public bool CanModifyVerifiedResult { get; private set; }

    public bool CanPreviewWithoutVerify { get; private set; }

    public bool CanPrintInvoice { get; private set; }

    public bool CanSeeVip { get; private set; }

    /// <summary>The cap that applies to a discount: the role's own, or the tenant default when the role defers to it.</summary>
    public int EffectiveDiscountCap(int tenantDefaultPercent) => DiscountCapPercent ?? Math.Clamp(tenantDefaultPercent, 0, MaxDiscountCapPercent);

    /// <summary>A fresh instance with the same values: an owned value is never shared between two owners (EF Core tracks owned instances per owner).</summary>
    public SpecialRights Copy() => new(DiscountCapPercent, CanPay, CanRefund, CanTransfer, CanCancel, CanModifyVerifiedResult, CanPreviewWithoutVerify, CanPrintInvoice, CanSeeVip);

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return DiscountCapPercent;
        yield return CanPay;
        yield return CanRefund;
        yield return CanTransfer;
        yield return CanCancel;
        yield return CanModifyVerifiedResult;
        yield return CanPreviewWithoutVerify;
        yield return CanPrintInvoice;
        yield return CanSeeVip;
    }
}
