namespace Platform.Domain.Pricing;

/// <summary>Kind of payer (SRS CP-32). Stored as its name; the convention adds the domain CHECK constraint.</summary>
public enum PayerType
{
    Cash = 0,
    Credit = 1,
    Insurance = 2,
    Lab = 3,
}

/// <summary>How a copied list derives its prices from the source (SRS CP-33 "× factor or + amount").</summary>
public enum CopyOperation
{
    Multiply = 0,
    Add = 1,
}

/// <summary>How a bulk adjustment changes every price of a list (CR 2026-10-19 C21): by a fixed amount or by a percentage.</summary>
public enum PriceAdjustMode
{
    Amount = 0,
    Percent = 1,
}

/// <summary>Rounding of an adjusted price (CR 2026-10-19 C21): two decimals, the nearest whole, the nearest 5 or the nearest 10 (half away from zero).</summary>
public enum PriceRounding
{
    None = 0,
    Whole = 1,
    Nearest5 = 2,
    Nearest10 = 3,
}
