using Platform.SharedKernel.Primitives;

namespace Platform.Domain.Billing;

/// <summary>How money was handed over (CP-23). Card = the LDM "Visa" tick; Pos = a terminal slip; Transfer = bank or wallet transfer.</summary>
public enum PaymentMethod
{
    Cash = 0,
    Card = 1,
    Pos = 2,
    Transfer = 3,
}

/// <summary>
/// How a movement names its method (change request 2026-10-19, P-C10): the <see cref="PaymentMethod"/> kind that carries
/// the money semantics, the code of the laboratory's <see cref="PaymentMethodDefinition"/> when one was chosen (stored on
/// the movement), and whether a transaction reference is required. <see cref="Legacy"/> is the rule before definitions
/// existed and still applies to a request without a code: every kind but cash needs a reference (BR-26).
/// </summary>
public sealed record PaymentMethodUse(PaymentMethod Kind, string? Code, bool RequiresReference)
{
    public static PaymentMethodUse Legacy(PaymentMethod kind) => new(kind, null, kind != PaymentMethod.Cash);
}

/// <summary>Posted counts in the balance and the drawer; Voided (with a reason) counts nowhere and is terminal.</summary>
public enum PaymentStatus
{
    Posted = 0,
    Voided = 1,
}

/// <summary>The derived payment-balance state of a visit (SRS status model "Payment balance").</summary>
public enum BalanceStatus
{
    Paid = 0,
    Unpaid = 1,
    Partial = 2,
    RefundDue = 3,
}

/// <summary>Money rules shared by every movement: two decimals, positive, bounded by the column precision (intake Q15).</summary>
public static class BillingAmounts
{
    public const int Precision = 14;
    public const int Scale = 2;
    public const decimal Max = 999_999_999_999.99m;

    public static decimal Round(decimal amount) => decimal.Round(amount, Scale, MidpointRounding.AwayFromZero);

    public static bool HasValidScale(decimal amount) => decimal.Round(amount, Scale) == amount;

    /// <summary>BIL-001 (not positive or above the column range) and BIL-002 (more than two decimals).</summary>
    public static void RequireValid(decimal amount)
    {
        if (amount <= 0m || amount > Max)
        {
            throw new DomainException(BillingCodes.AmountInvalid, "The amount must be greater than zero.", DomainFault.Validation);
        }

        if (!HasValidScale(amount))
        {
            throw new DomainException(BillingCodes.AmountScale, $"The amount may have at most {Scale} decimals.", DomainFault.Validation);
        }
    }
}

/// <summary>
/// The single definition of the visit balance (BR-14, MODULES.md §5): <c>remaining = patient share − Σ posted payments
/// + Σ refunds</c>; a negative remaining is a refund due, and the refundable amount is its absolute value.
/// </summary>
public sealed record VisitBalanceFigures(decimal PatientShareTotal, decimal Paid, decimal Refunded)
{
    public decimal Remaining => PatientShareTotal - Paid + Refunded;

    public decimal Refundable => Remaining < 0m ? -Remaining : 0m;

    public BalanceStatus Status => Remaining switch
    {
        < 0m => BalanceStatus.RefundDue,
        0m => BalanceStatus.Paid,
        _ when Paid - Refunded <= 0m => BalanceStatus.Unpaid,
        _ => BalanceStatus.Partial,
    };
}

/// <summary>
/// The BR-27 facts of one money movement, resolved by the handler: the treasury (Treasury module) with its branch and
/// active flag, whether a live Approved access request exists for the cashier on this device, and the session branch
/// (CP-04). The aggregate decides with them (pitfall P-10).
/// </summary>
public sealed record CashierStation(Guid TreasuryId, Guid TreasuryBranchId, bool TreasuryIsActive, bool ApprovedForDevice, string DeviceId, Guid CashierUserId, Guid SessionBranchId)
{
    public const int MaxDeviceIdLength = 128;

    /// <summary>The cleaned device id, or the coded refusal: device (BIL-006), active (BIL-021), branch (BIL-022), approval (BIL-020) — in that order.</summary>
    public string RequireUsable()
    {
        var device = (DeviceId ?? string.Empty).Trim();
        if (device.Length is < 1 or > MaxDeviceIdLength)
        {
            throw new DomainException(BillingCodes.DeviceRequired, $"A device identifier of 1 to {MaxDeviceIdLength} characters is required.", DomainFault.Validation);
        }

        if (CashierUserId == Guid.Empty)
        {
            throw new DomainException(ProblemCodesForBilling.ActorRequired, "An authenticated cashier is required.", DomainFault.Unauthenticated);
        }

        if (!TreasuryIsActive)
        {
            throw new DomainException(BillingCodes.TreasuryInactive, "The treasury is inactive.", DomainFault.Conflict);
        }

        if (TreasuryBranchId != SessionBranchId)
        {
            throw new DomainException(BillingCodes.TreasuryOfAnotherBranch, "The treasury belongs to another branch than the session's.", DomainFault.Refused);
        }

        if (!ApprovedForDevice)
        {
            throw new DomainException(BillingCodes.TreasuryNotApproved, "The treasury is not approved for you on this device (BR-27).", DomainFault.Refused);
        }

        return device;
    }
}

/// <summary>Cross-cutting codes the domain needs without referencing the Contracts layer.</summary>
internal static class ProblemCodesForBilling
{
    /// <summary>Same value as <c>ProblemCodes.AuthenticatedActorRequired</c>.</summary>
    public const string ActorRequired = "AUTH-001";
}

/// <summary>
/// The Registration facts a movement needs (<c>app.visits</c>, MODULES.md §5): the patient share (net of discount, Ordered
/// and Reserved lines) and whether the visit belongs to a lab-to-lab batch, which is billed to the sending laboratory
/// through claims (CP-27) and takes no payment here (BIL-015).
/// </summary>
public sealed record VisitCharge(decimal PatientShareTotal, bool BilledThroughClaims);
