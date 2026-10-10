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
