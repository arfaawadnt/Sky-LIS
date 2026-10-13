using Platform.SharedKernel.Primitives;

namespace Platform.Domain.Organization;

/// <summary>
/// The particular actions a role (or one user, as an override) may take beyond the screen rights (SRS CP-37;
/// LDM manual §12.3 "Special Rights"). Immutable: a change replaces the whole value.
/// <see cref="DiscountCapPercent"/>: <see langword="null"/> = the tenant parameter <c>discountCapPercent</c> applies,
/// <c>0</c> = no discount, 1–100 = the highest discount in percent.
/// <see cref="DemographicsEditUpTo"/>: the highest result status of a visit up to which the member may still change
/// the patient's identity demographics (change request 2026-10-13, item 1).
/// <see cref="CanViewBranchCollections"/>: the collections report covers the whole branch, not only the member's own
/// payments (item 2). <see cref="ReportDaysLimit"/>: how many days back the member may report (<c>0</c> = today only,
/// <see langword="null"/> = no limit; item 3).
/// </summary>
public sealed class SpecialRights : ValueObject
{
    public const int MaxDiscountCapPercent = 100;

    /// <summary>Ten years: a longer limit means no limit.</summary>
    public const int MaxReportDaysLimit = 3650;

    /// <summary>Nothing granted: no discount, no payment, no refund … The state of a user without a role.</summary>
    public static readonly SpecialRights None = new(0, false, false, false, false, false, false, false, false, DemographicsEditLimit.Ordered, false, 0);

    /// <summary>Everything granted with a cap of 100 %: the provisioned administrator role.</summary>
    public static readonly SpecialRights All = new(MaxDiscountCapPercent, true, true, true, true, true, true, true, true, DemographicsEditLimit.Printed, true, null);

    /// <summary>The value an existing role holds when the three later rights were introduced (migration <c>SpecialRightsExtended</c>).</summary>
    public const DemographicsEditLimit DefaultDemographicsEditUpTo = DemographicsEditLimit.Complete;

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
        : this(discountCapPercent, canPay, canRefund, canTransfer, canCancel, canModifyVerifiedResult, canPreviewWithoutVerify, canPrintInvoice, canSeeVip, DefaultDemographicsEditUpTo, false, null)
    {
    }

    public SpecialRights(
        int? discountCapPercent,
        bool canPay,
        bool canRefund,
        bool canTransfer,
        bool canCancel,
        bool canModifyVerifiedResult,
        bool canPreviewWithoutVerify,
        bool canPrintInvoice,
        bool canSeeVip,
        DemographicsEditLimit demographicsEditUpTo,
        bool canViewBranchCollections,
        int? reportDaysLimit)
    {
        if (discountCapPercent is < 0 or > MaxDiscountCapPercent)
        {
            throw new DomainException(OrganizationCodes.DiscountCapInvalid, $"The discount cap must be empty (tenant default) or 0 to {MaxDiscountCapPercent} percent.", DomainFault.Validation);
        }

        if (!Enum.IsDefined(demographicsEditUpTo))
        {
            throw new DomainException(OrganizationCodes.DemographicsEditLimitInvalid, "The demographics edit limit must be one of the result statuses.", DomainFault.Validation);
        }

        if (reportDaysLimit is < 0 or > MaxReportDaysLimit)
        {
            throw new DomainException(OrganizationCodes.ReportDaysLimitInvalid, $"The report days limit must be empty (no limit) or 0 to {MaxReportDaysLimit} days.", DomainFault.Validation);
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
        DemographicsEditUpTo = demographicsEditUpTo;
        CanViewBranchCollections = canViewBranchCollections;
        ReportDaysLimit = reportDaysLimit;
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

    public DemographicsEditLimit DemographicsEditUpTo { get; private set; }

    public bool CanViewBranchCollections { get; private set; }

    public int? ReportDaysLimit { get; private set; }

    /// <summary>The cap that applies to a discount: the role's own, or the tenant default when the role defers to it.</summary>
    public int EffectiveDiscountCap(int tenantDefaultPercent) => DiscountCapPercent ?? Math.Clamp(tenantDefaultPercent, 0, MaxDiscountCapPercent);

    /// <summary>True while a visit whose highest result status is <paramref name="highestResultStatus"/> still allows a demographics change by this member.</summary>
    public bool AllowsDemographicsEditAt(DemographicsEditLimit highestResultStatus) => highestResultStatus <= DemographicsEditUpTo;

    /// <summary>The earliest day (laboratory calendar) this member may report on, given today; <see langword="null"/> = no limit.</summary>
    public DateOnly? EarliestReportDay(DateOnly today) => ReportDaysLimit is { } days ? today.AddDays(-days) : null;

    /// <summary>A fresh instance with the same values: an owned value is never shared between two owners (EF Core tracks owned instances per owner).</summary>
    public SpecialRights Copy() => new(DiscountCapPercent, CanPay, CanRefund, CanTransfer, CanCancel, CanModifyVerifiedResult, CanPreviewWithoutVerify, CanPrintInvoice, CanSeeVip, DemographicsEditUpTo, CanViewBranchCollections, ReportDaysLimit);

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
        yield return DemographicsEditUpTo;
        yield return CanViewBranchCollections;
        yield return ReportDaysLimit;
    }
}
