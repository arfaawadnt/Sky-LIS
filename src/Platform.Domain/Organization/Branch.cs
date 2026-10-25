using Platform.Domain.PlatformAdmin;
using Platform.SharedKernel.Localization;
using Platform.SharedKernel.MultiTenancy;
using Platform.SharedKernel.Primitives;

namespace Platform.Domain.Organization;

/// <summary>
/// A laboratory branch (SRS CP-36). Every session works in one active branch (CP-04) and every branch-stamped
/// record carries its id. A branch is never deleted (BR-15, INV-ORG-003): it is deactivated, and a tenant keeps at
/// least one active branch (INV-ORG-002, checked by the handler). Country and governorate reference the platform
/// lists by id (module <c>plat</c>, no foreign key across the schema boundary).
/// </summary>
public sealed class Branch : AggregateRoot, ITenantScoped
{
    public const int MaxCodeLength = 16;
    public const int MaxPhoneLength = 20;
    public const int MaxEmailLength = 254;
    public const int CurrencyLength = 3;

    /// <summary>The code of the branch every new tenant receives (platform provisioning).</summary>
    public const string MainCode = "MAIN";

    private Branch()
    {
        Code = string.Empty;
        Name = null!;
        Currency = string.Empty;
    }

    private Branch(Guid id, string code, LocalizedText name)
        : base(id)
    {
        Code = code;
        Name = name;
        Currency = string.Empty;
        IsActive = true;
    }

    public Guid TenantId { get; private set; }

    /// <summary>Upper-case code, unique per tenant, immutable (INV-ORG-001).</summary>
    public string Code { get; private set; }

    public LocalizedText Name { get; private set; }

    public Guid? CountryId { get; private set; }

    public Guid? GovernorateId { get; private set; }

    /// <summary>LIS-CR-008 C-04: the city (markaz) of the branch under its governorate (saas.cities by id), optional.</summary>
    public Guid? CityId { get; private set; }

    /// <summary>LIS-CR-008 C-04: the district under the city, optional.</summary>
    public Guid? DistrictId { get; private set; }

    /// <summary>LIS-CR-008 C-04: the area (village) under the district, optional.</summary>
    public Guid? AreaId { get; private set; }

    public string? Phone { get; private set; }

    public string? Email { get; private set; }

    /// <summary>ISO 4217 alphabetic code (three upper-case letters).</summary>
    public string Currency { get; private set; }

    public bool IsActive { get; private set; }

    public uint RowVersion { get; private set; }

    /// <summary>B1 Create (<c>organization.create</c>) and B5 provisioning. Code uniqueness is the handler's check and the database's index.</summary>
    public static Branch Create(string code, LocalizedText name, Guid? countryId, Guid? governorateId, string? phone, string? email, string currency, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(name);
        var branch = new Branch(Guid.CreateVersion7(), CleanCode(code), name);
        branch.ApplyDetails(name, countryId, governorateId, phone, email, currency);
        branch.Raise(new BranchCreated(branch.Id, branch.Code, nowUtc));
        return branch;
    }

    /// <summary>B2 Update (<c>organization.edit</c>): everything except the code; allowed in both states.</summary>
    public void Update(LocalizedText name, Guid? countryId, Guid? governorateId, string? phone, string? email, string currency)
    {
        ArgumentNullException.ThrowIfNull(name);
        ApplyDetails(name, countryId, governorateId, phone, email, currency);
    }

    /// <summary>
    /// LIS-CR-008 C-04 (B1/B2): places the branch in a city / district / area of its governorate. The locality was checked by
    /// <see cref="Locality.Resolve"/> (chain GEO-020…022); one checked against another governorate, or set on a branch without a
    /// governorate, is refused GEO-020. Call after <see cref="Create"/> / <see cref="Update"/>; <see cref="Locality.None"/> clears it.
    /// </summary>
    public void Locate(Locality locality)
    {
        ArgumentNullException.ThrowIfNull(locality);
        locality.EnsureIn(GovernorateId);
        CityId = locality.CityId;
        DistrictId = locality.DistrictId;
        AreaId = locality.AreaId;
    }

    /// <summary>B3 Deactivate (<c>organization.manage</c>). Idempotent. The handler refuses the last active branch (ORG-110).</summary>
    public void Deactivate(DateTimeOffset nowUtc)
    {
        if (!IsActive)
        {
            return;
        }

        IsActive = false;
        Raise(new BranchDeactivated(Id, Code, nowUtc));
    }

    /// <summary>B4 Reactivate (<c>organization.manage</c>). Idempotent.</summary>
    public void Reactivate(DateTimeOffset nowUtc)
    {
        if (IsActive)
        {
            return;
        }

        IsActive = true;
        Raise(new BranchReactivated(Id, Code, nowUtc));
    }

    public static string CleanCode(string code)
    {
        var value = (code ?? string.Empty).Trim().ToUpperInvariant();
        Require(
            value.Length is >= 1 and <= MaxCodeLength && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'),
            OrganizationCodes.BranchCodeInvalid,
            $"The branch code must be 1 to {MaxCodeLength} characters: letters, digits, '-' or '_'.",
            DomainFault.Validation);
        return value;
    }

    public static string CleanCurrency(string currency)
    {
        var value = (currency ?? string.Empty).Trim().ToUpperInvariant();
        Require(value.Length == CurrencyLength && value.All(char.IsAsciiLetterUpper), OrganizationCodes.CurrencyInvalid, "The currency must be a three-letter ISO 4217 code.", DomainFault.Validation);
        return value;
    }

    private void ApplyDetails(LocalizedText name, Guid? countryId, Guid? governorateId, string? phone, string? email, string currency)
    {
        var country = countryId == Guid.Empty ? null : countryId;
        var governorate = governorateId == Guid.Empty ? null : governorateId;
        Require(governorate is null || country is not null, OrganizationCodes.BranchContactInvalid, "A governorate needs its country.", DomainFault.Validation);

        Name = name;
        CountryId = country;
        GovernorateId = governorate;
        Phone = CleanPhone(phone);
        Email = CleanEmail(email);
        Currency = CleanCurrency(currency);
    }

    private static string? CleanPhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
        {
            return null;
        }

        var value = phone.Trim();
        var digits = value.Count(char.IsAsciiDigit);
        Require(
            value.Length <= MaxPhoneLength && digits >= 5 && value.All(c => char.IsAsciiDigit(c) || c is '+' or ' ' or '-' or '(' or ')'),
            OrganizationCodes.BranchPhoneInvalid,
            // One wording with the page's ORG-015 text (UAT R1-06).
            $"The phone number must have at least 5 digits and at most {MaxPhoneLength} characters: digits, spaces, '+', '-' or parentheses.",
            DomainFault.Validation);
        return value;
    }

    private static string? CleanEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return null;
        }

        var value = email.Trim().ToLowerInvariant();
        var at = value.IndexOf('@', StringComparison.Ordinal);
        Require(
            value.Length <= MaxEmailLength && at > 0 && at < value.Length - 1 && value.IndexOf('@', at + 1) < 0 && !value.Any(char.IsWhiteSpace),
            OrganizationCodes.BranchEmailInvalid,
            "The e-mail address is not valid.",
            DomainFault.Validation);
        return value;
    }
}
