using Platform.SharedKernel.Primitives;

namespace Platform.Domain.Billing;

/// <summary>A member visit of a batch with what it still owes; members are given in accession order.</summary>
public sealed record BatchMemberDue(Guid VisitId, decimal Remaining);

/// <summary>The part of a pay-all amount one member receives.</summary>
public sealed record BatchShare(Guid VisitId, decimal Amount);

/// <summary>
/// Pay all (SRS CP-27, T4; LDM <c>Batch Payment</c> §4.8): an amount is distributed across the members in accession order,
/// each up to its remaining balance; members that owe nothing (or are due a refund) are skipped. The amount may not exceed
/// what the batch owes (BIL-014); a lab-to-lab batch takes no payment (BIL-015).
/// </summary>
public static class BatchAllocation
{
    public static IReadOnlyList<BatchShare> Distribute(decimal amount, IReadOnlyList<BatchMemberDue> members, bool billedThroughClaims)
    {
        ArgumentNullException.ThrowIfNull(members);
        if (billedThroughClaims)
        {
            throw new DomainException(BillingCodes.BilledThroughClaims, "A lab-to-lab batch is billed to the sending laboratory through claims.", DomainFault.Conflict);
        }

        BillingAmounts.RequireValid(amount);

        var owing = members
            .Select(member => member with { Remaining = BillingAmounts.Round(member.Remaining) })
            .Where(member => member.Remaining > 0m)
            .ToList();
        var due = owing.Sum(member => member.Remaining);
        if (amount > due)
        {
            throw new DomainException(BillingCodes.AmountExceedsBatchRemaining, $"The amount exceeds what the batch owes ({due}).", DomainFault.Conflict);
        }

        var shares = new List<BatchShare>();
        var left = amount;
        foreach (var member in owing)
        {
            if (left <= 0m)
            {
                break;
            }

            var share = Math.Min(left, member.Remaining);
            shares.Add(new BatchShare(member.VisitId, share));
            left -= share;
        }

        return shares;
    }
}
