using Platform.SharedKernel.Localization;
using Platform.SharedKernel.Primitives;

namespace Platform.Domain.PlatformAdmin;

/// <summary>
/// A subscription plan (SA-05): monthly fee and the limits a client on the plan receives (branches, users, SMS quota,
/// analysers). Platform reference data (<c>saas.plans</c>); the code is unique (INV-PLT-020). An inactive plan stays
/// on the clients that hold it but cannot be given to a new or edited client (<c>PLN-010</c>).
/// </summary>
public sealed class Plan : AggregateRoot
{
    public const int MaxCodeLength = 20;
    public const int MaxNameLength = 100;
    public const int MaxLimit = 1_000_000;

    private Plan()
    {
        Code = string.Empty;
        Name = null!;
        CurrencyCode = string.Empty;
    }

    private Plan(Guid id, string code, PlanTerms terms)
        : base(id)
    {
        Code = code;
        Name = terms.Name;
        CurrencyCode = terms.CurrencyCode;
        Apply(terms);
        IsActive = true;
    }

    public string Code { get; private set; }

    public LocalizedText Name { get; private set; }

    public decimal MonthlyFee { get; private set; }

    /// <summary>ISO 4217 currency of the fee.</summary>
    public string CurrencyCode { get; private set; }

    public int MaxBranches { get; private set; }

    public int MaxUsers { get; private set; }

    public int SmsQuota { get; private set; }

    public int MaxAnalysers { get; private set; }

    public bool IsActive { get; private set; }

    public uint RowVersion { get; private set; }

    public static Plan Create(string code, PlanTerms terms)
    {
        ArgumentNullException.ThrowIfNull(terms);
        return new Plan(Guid.CreateVersion7(), CleanCode(code), terms.Validated());
    }

    public void Update(PlanTerms terms)
    {
        ArgumentNullException.ThrowIfNull(terms);
        Apply(terms.Validated());
    }

    public void Deactivate() => IsActive = false;

    public void Reactivate() => IsActive = true;

    public static string CleanCode(string code)
    {
        var value = (code ?? string.Empty).Trim().ToUpperInvariant();
        Require(
            value.Length is >= 2 and <= MaxCodeLength && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'),
            PlatformAdminCodes.PlanCodeInvalid,
            $"The plan code is 2 to {MaxCodeLength} characters: letters, digits, '-' or '_'.",
            DomainFault.Validation);
        return value;
    }

    private void Apply(PlanTerms terms)
    {
        Name = terms.Name;
        MonthlyFee = terms.MonthlyFee;
        CurrencyCode = terms.CurrencyCode;
        MaxBranches = terms.MaxBranches;
        MaxUsers = terms.MaxUsers;
        SmsQuota = terms.SmsQuota;
        MaxAnalysers = terms.MaxAnalysers;
    }
}

/// <summary>The editable terms of a plan; <see cref="Validated"/> checks them with <c>PLN-002</c>.</summary>
public sealed record PlanTerms(LocalizedText Name, decimal MonthlyFee, string CurrencyCode, int MaxBranches, int MaxUsers, int SmsQuota, int MaxAnalysers)
{
    internal PlanTerms Validated()
    {
        ArgumentNullException.ThrowIfNull(Name);
        var currency = (CurrencyCode ?? string.Empty).Trim().ToUpperInvariant();
        var valid = currency.Length == 3 && currency.All(char.IsAsciiLetterUpper)
                    && MaxBranches is >= 1 and <= Plan.MaxLimit
                    && MaxUsers is >= 1 and <= Plan.MaxLimit
                    && SmsQuota is >= 0 and <= Plan.MaxLimit
                    && MaxAnalysers is >= 0 and <= Plan.MaxLimit;
        if (!valid)
        {
            throw new DomainException(PlatformAdminCodes.PlanLimitsInvalid, "A plan needs a three-letter currency, at least one branch and one user, and no negative limit.", DomainFault.Validation);
        }

        return this with
        {
            MonthlyFee = DomainText.Money(MonthlyFee, PlatformAdminCodes.PlanLimitsInvalid, "The monthly fee", allowZero: true),
            CurrencyCode = currency,
        };
    }
}
