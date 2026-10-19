using Platform.SharedKernel.Primitives;

namespace Platform.Domain.Pricing;

/// <summary>
/// Money and factor rules of the module (intake §16 Q13–Q14): amounts are 0–99 999 999.99 with two decimals (column
/// <c>numeric(12,2)</c>), factors 0–10 with four decimals (<c>numeric(9,4)</c>), shares rounded to 0.01 half away from zero.
/// </summary>
public static class PriceMath
{
    public const decimal MaxAmount = 99_999_999.99m;
    public const decimal MaxFactor = 10m;
    public const int AmountScale = 2;
    public const int FactorScale = 4;

    /// <summary>The rounding steps a copied list may use (SRS CP-33, intake Q13).</summary>
    public static readonly IReadOnlyList<decimal> RoundingSteps = [0.01m, 0.25m, 0.5m, 1m];

    public static decimal RoundAmount(decimal value) => Math.Round(value, AmountScale, MidpointRounding.AwayFromZero);

    /// <summary>Rounds to the nearest multiple of <paramref name="step"/>, half away from zero (0.25 → 12.37 becomes 12.25, 12.38 becomes 12.50).</summary>
    public static decimal RoundToStep(decimal value, decimal step)
    {
        if (step <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(step), step, "The rounding step must be positive.");
        }

        return RoundAmount(Math.Round(value / step, 0, MidpointRounding.AwayFromZero) * step);
    }

    /// <summary>Percent adjustments (C21) go from −100 % (every price becomes 0) to +1 000 %, with at most two decimals.</summary>
    public const decimal MinPercentAdjustment = -100m;
    public const decimal MaxPercentAdjustment = 1_000m;

    /// <summary>
    /// One adjusted price (C21 bulk adjust): the amount or percentage applied, the rounding applied, and a negative result
    /// clamped to 0 (<paramref name="Clamped"/> reports it so the preview can show the line). Above the maximum the
    /// adjustment is refused (PRC-046): a silent cap at 99 999 999.99 would hide a wrong percentage.
    /// </summary>
    public static AdjustedPrice Adjust(decimal price, PriceAdjustMode mode, decimal value, PriceRounding rounding)
    {
        RequireAdjustment(mode, value);
        var raw = mode == PriceAdjustMode.Amount ? price + value : price * (1 + (value / 100m));
        var rounded = rounding switch
        {
            PriceRounding.None => RoundAmount(raw),
            PriceRounding.Whole => RoundToStep(raw, 1m),
            PriceRounding.Nearest5 => RoundToStep(raw, 5m),
            PriceRounding.Nearest10 => RoundToStep(raw, 10m),
            _ => throw new DomainException(PricingCodes.AdjustmentInvalid, "Unknown rounding.", DomainFault.Validation),
        };
        if (rounded < 0)
        {
            return new AdjustedPrice(0m, true);
        }

        if (rounded > MaxAmount)
        {
            throw new DomainException(PricingCodes.AdjustResultOutOfRange, $"The adjustment would price a service at {rounded}; prices must not exceed {MaxAmount}.", DomainFault.Validation);
        }

        return new AdjustedPrice(rounded, false);
    }

    /// <summary>PRC-044: an amount within ±<see cref="MaxAmount"/> with two decimals, or a percentage between −100 and 1 000 with two decimals.</summary>
    public static void RequireAdjustment(PriceAdjustMode mode, decimal value)
    {
        var valid = mode switch
        {
            PriceAdjustMode.Amount => Math.Abs(value) <= MaxAmount && decimal.Round(value, AmountScale) == value,
            PriceAdjustMode.Percent => value >= MinPercentAdjustment && value <= MaxPercentAdjustment && decimal.Round(value, AmountScale) == value,
            _ => false,
        };
        if (!valid)
        {
            throw new DomainException(PricingCodes.AdjustmentInvalid, $"Adjust by an amount within ±{MaxAmount} or by a percentage between {MinPercentAdjustment} and {MaxPercentAdjustment}, with at most two decimals.", DomainFault.Validation);
        }
    }

    public static bool IsValidAmount(decimal value) => value is >= 0 and <= MaxAmount && decimal.Round(value, AmountScale) == value;

    public static bool IsValidFactor(decimal value) => value is >= 0 and <= MaxFactor && decimal.Round(value, FactorScale) == value;

    public static decimal RequireAmount(decimal value, string code, string message)
    {
        if (!IsValidAmount(value))
        {
            throw new DomainException(code, message, DomainFault.Validation);
        }

        return value;
    }

    public static decimal RequireFactor(decimal value)
    {
        if (!IsValidFactor(value))
        {
            throw new DomainException(PricingCodes.FactorInvalid, $"A factor must be between 0 and {MaxFactor} with at most {FactorScale} decimals.", DomainFault.Validation);
        }

        return value;
    }

    /// <summary>Upper-case code (list codes, payer search keys) as the SPA's <c>CODE_PATTERN</c>: a letter or digit, then letters, digits, '.', '-' or '_'.</summary>
    public static string CleanKey(string? value, int maxLength, string code, string message)
    {
        var cleaned = (value ?? string.Empty).Trim().ToUpperInvariant();
        if (cleaned.Length < 1 || cleaned.Length > maxLength || !char.IsAsciiLetterOrDigit(cleaned[0]) || !cleaned.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_'))
        {
            throw new DomainException(code, message, DomainFault.Validation);
        }

        return cleaned;
    }
}

/// <summary>The result of <see cref="PriceMath.Adjust"/>: the new price and whether a negative result was clamped to 0.</summary>
public readonly record struct AdjustedPrice(decimal Price, bool Clamped);
