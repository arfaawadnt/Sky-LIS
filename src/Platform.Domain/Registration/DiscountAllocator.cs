namespace Platform.Domain.Registration;

/// <summary>
/// The discount arithmetic of intake §5 "Discount" as pure functions: the amount a kind and value give on a base, and
/// its allocation to lines in proportion to their gross patient share, to the cent, by largest remainder (ties by
/// position), so the allocations always add up to the amount.
/// </summary>
public static class DiscountAllocator
{
    public static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    /// <summary>Percent: round(base × value / 100); Amount: the value, never above the base; never negative.</summary>
    public static decimal AmountFor(DiscountKind kind, decimal value, decimal baseAmount)
    {
        if (baseAmount <= 0 || value <= 0)
        {
            return 0m;
        }

        var amount = kind == DiscountKind.Percent ? Round(baseAmount * Math.Min(value, 100m) / 100m) : Round(value);
        return Math.Min(amount, baseAmount);
    }

    /// <summary>The percent of the base an amount represents (for the cap check).</summary>
    public static decimal PercentOf(decimal amount, decimal baseAmount) => baseAmount <= 0 ? 0m : amount * 100m / baseAmount;

    /// <summary>Splits <paramref name="amount"/> over <paramref name="weights"/> (gross patient shares) to the cent.</summary>
    public static IReadOnlyList<decimal> Allocate(decimal amount, IReadOnlyList<decimal> weights)
    {
        ArgumentNullException.ThrowIfNull(weights);
        var result = new decimal[weights.Count];
        var total = weights.Where(weight => weight > 0).Sum();
        var cents = (long)Math.Round(amount * 100m, MidpointRounding.AwayFromZero);
        if (cents <= 0 || total <= 0)
        {
            return result;
        }

        var raw = weights.Select(weight => weight > 0 ? cents * weight / total : 0m).ToArray();
        var floors = raw.Select(value => (long)Math.Floor(value)).ToArray();
        var left = cents - floors.Sum();
        var order = Enumerable.Range(0, raw.Length)
            .Where(index => weights[index] > 0)
            .OrderByDescending(index => raw[index] - floors[index])
            .ThenBy(index => index)
            .ToList();
        for (var i = 0; left > 0 && order.Count > 0; i = (i + 1) % order.Count, left--)
        {
            floors[order[i]]++;
        }

        for (var index = 0; index < result.Length; index++)
        {
            result[index] = floors[index] / 100m;
        }

        return result;
    }
}
