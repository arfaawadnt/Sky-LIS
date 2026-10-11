using Platform.SharedKernel.Localization;
using Platform.SharedKernel.Primitives;

namespace Platform.Domain.PlatformAdmin;

/// <summary>
/// A country the platform serves (SA-10), with the data that validates a laboratory's details: ISO code, dial code
/// (<c>+20</c>), currency, language, time zone and the length of a national mobile number. Platform reference data
/// (<c>saas.countries</c>, no tenant). Deactivated rather than deleted once referenced; deletion is refused while a
/// governorate or a client references it (INV-PLT-010, checked by the handler with <c>GEO-110</c>).
/// </summary>
public sealed class Country : AggregateRoot
{
    public const int MaxNameLength = 100;
    public const int MaxCurrencyNameLength = 60;
    public const int MaxTimeZoneLength = 64;
    public const int MaxFlagLength = 16;
    public const int MaxRemarksLength = 500;
    public const int MinMobileLength = 6;
    public const int MaxMobileLength = 15;
    public static readonly IReadOnlyList<string> Languages = ["en", "ar"];

    private Country()
    {
        Name = null!;
        IsoCode = string.Empty;
        DialCode = string.Empty;
        Currency = string.Empty;
        CurrencyCode = string.Empty;
        Language = "en";
        TimeZone = string.Empty;
    }

    private Country(Guid id, ValidCountry data)
        : base(id)
    {
        Name = data.Name;
        IsoCode = data.IsoCode;
        DialCode = data.DialCode;
        Currency = data.Currency;
        CurrencyCode = data.CurrencyCode;
        Language = data.Language;
        TimeZone = data.TimeZone;
        MobileLength = data.MobileLength;
        Flag = data.Flag;
        Remarks = data.Remarks;
        IsActive = true;
    }

    public LocalizedText Name { get; private set; }

    /// <summary>ISO 3166-1 alpha-2, upper case, unique (INV-PLT-011).</summary>
    public string IsoCode { get; private set; }

    /// <summary><c>+</c> and one to four digits, e.g. <c>+20</c>.</summary>
    public string DialCode { get; private set; }

    public string Currency { get; private set; }

    /// <summary>ISO 4217, e.g. <c>EGP</c>.</summary>
    public string CurrencyCode { get; private set; }

    public string Language { get; private set; }

    /// <summary>IANA time zone, e.g. <c>Africa/Cairo</c>.</summary>
    public string TimeZone { get; private set; }

    /// <summary>Digits of a national mobile number without the trunk prefix (Egypt 10, Saudi Arabia 9).</summary>
    public int MobileLength { get; private set; }

    public string? Flag { get; private set; }

    public string? Remarks { get; private set; }

    public bool IsActive { get; private set; }

    public uint RowVersion { get; private set; }

    public static Country Create(CountryData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        return new Country(Guid.CreateVersion7(), data.Validated());
    }

    /// <summary>Edits every field except the ISO code (the natural key).</summary>
    public void Update(CountryData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var valid = data.Validated();
        Name = valid.Name;
        DialCode = valid.DialCode;
        Currency = valid.Currency;
        CurrencyCode = valid.CurrencyCode;
        Language = valid.Language;
        TimeZone = valid.TimeZone;
        MobileLength = valid.MobileLength;
        Flag = valid.Flag;
        Remarks = valid.Remarks;
    }

    public void Deactivate() => IsActive = false;

    public void Reactivate() => IsActive = true;

    /// <summary>
    /// CP-02 / SA-04 "mobile validated by country length": accepts <c>+20 101 234 5678</c>, <c>0020…</c>, <c>01012345678</c>
    /// or <c>1012345678</c> and returns the E.164 form (<c>+201012345678</c>); anything else is <c>GEO-010</c>.
    /// </summary>
    public string NormaliseMobile(string? input) => NormaliseMobile(input, PlatformAdminCodes.MobileInvalidForCountry, "A mobile number");

    /// <summary>
    /// <see cref="NormaliseMobile(string?)"/> for one named field: the refusal carries <paramref name="code"/> and names the
    /// field (<c>GEO-010</c> the laboratory mobile, <c>GEO-014</c> the manager's mobile; validation 2026-10-11, C-03).
    /// </summary>
    public string NormaliseMobile(string? input, string code, string field)
    {
        var raw = (input ?? string.Empty).Trim();
        var digits = new string(raw.Where(char.IsAsciiDigit).ToArray());
        var dial = DialCode[1..];
        string national;
        if (raw.StartsWith('+'))
        {
            national = digits.StartsWith(dial, StringComparison.Ordinal) ? digits[dial.Length..] : string.Empty;
        }
        else if (digits.StartsWith("00" + dial, StringComparison.Ordinal))
        {
            national = digits[(dial.Length + 2)..];
        }
        else
        {
            national = digits.Length == MobileLength + 1 && digits[0] == '0' ? digits[1..] : digits;
        }

        var shapeOk = raw.Length > 0 && raw.All(c => char.IsAsciiDigit(c) || c is '+' or ' ' or '-' or '(' or ')');
        Require(shapeOk && national.Length == MobileLength && national[0] != '0', code,
            $"{field} in {IsoCode} has {MobileLength} digits after {DialCode}.", DomainFault.Validation);
        return DialCode + national;
    }
}

/// <summary>The editable data of a country; <see cref="Validated"/> trims and checks every field with its code.</summary>
public sealed record CountryData(
    string NameEn,
    string NameAr,
    string IsoCode,
    string DialCode,
    string Currency,
    string CurrencyCode,
    string Language,
    string TimeZone,
    int MobileLength,
    string? Flag,
    string? Remarks)
{
    internal ValidCountry Validated()
    {
        var iso = (IsoCode ?? string.Empty).Trim().ToUpperInvariant();
        if (iso.Length != 2 || !iso.All(char.IsAsciiLetterUpper))
        {
            throw new DomainException(PlatformAdminCodes.IsoCodeInvalid, "The ISO code is two letters (ISO 3166-1 alpha-2).", DomainFault.Validation);
        }

        var dial = (DialCode ?? string.Empty).Trim().Replace(" ", string.Empty, StringComparison.Ordinal);
        if (dial.Length is < 2 or > 5 || dial[0] != '+' || !dial[1..].All(char.IsAsciiDigit) || dial[1] == '0')
        {
            throw new DomainException(PlatformAdminCodes.DialCodeInvalid, "The dial code is '+' followed by one to four digits, like +20.", DomainFault.Validation);
        }

        var currencyCode = (CurrencyCode ?? string.Empty).Trim().ToUpperInvariant();
        if (currencyCode.Length != 3 || !currencyCode.All(char.IsAsciiLetterUpper))
        {
            throw new DomainException(PlatformAdminCodes.CurrencyInvalid, "The currency code is three letters (ISO 4217).", DomainFault.Validation);
        }

        var language = (Language ?? string.Empty).Trim().ToLowerInvariant();
        if (!Country.Languages.Contains(language))
        {
            throw new DomainException(PlatformAdminCodes.LanguageInvalid, "The language is 'en' or 'ar'.", DomainFault.Validation);
        }

        if (MobileLength is < Country.MinMobileLength or > Country.MaxMobileLength)
        {
            throw new DomainException(PlatformAdminCodes.MobileLengthInvalid, $"The mobile length is between {Country.MinMobileLength} and {Country.MaxMobileLength} digits.", DomainFault.Validation);
        }

        return new ValidCountry(
            DomainText.Localized(NameEn, NameAr, Country.MaxNameLength, PlatformAdminCodes.GeographyTextInvalid, "The country name"),
            iso,
            dial,
            DomainText.Required(Currency, Country.MaxCurrencyNameLength, PlatformAdminCodes.CurrencyInvalid, "The currency"),
            currencyCode,
            language,
            IanaTimeZone(DomainText.Required(TimeZone, Country.MaxTimeZoneLength, PlatformAdminCodes.TimeZoneInvalid, "The time zone")),
            MobileLength,
            DomainText.Optional(Flag, Country.MaxFlagLength, PlatformAdminCodes.GeographyTextInvalid, "The flag"),
            DomainText.Optional(Remarks, Country.MaxRemarksLength, PlatformAdminCodes.GeographyTextInvalid, "The remarks"));
    }

    /// <summary>
    /// SA-10: the time zone is an IANA name (<c>Africa/Cairo</c>), validated against the zones the runtime knows (ICU on
    /// Windows, tzdata elsewhere); Windows names (<c>Egypt Standard Time</c>) and unknown names (<c>Mars/Base</c>) are
    /// <c>GEO-007</c>. The canonical spelling is kept (<c>africa/cairo</c> is stored as <c>Africa/Cairo</c>). Validation
    /// 2026-10-11, C-08.
    /// </summary>
    internal static string IanaTimeZone(string name)
    {
        if (!TimeZoneInfo.TryFindSystemTimeZoneById(name, out var zone) || !zone.HasIanaId)
        {
            throw new DomainException(PlatformAdminCodes.TimeZoneInvalid, $"'{name}' is not an IANA time zone name such as Africa/Cairo.", DomainFault.Validation);
        }

        return zone.Id;
    }
}

internal sealed record ValidCountry(LocalizedText Name, string IsoCode, string DialCode, string Currency, string CurrencyCode, string Language, string TimeZone, int MobileLength, string? Flag, string? Remarks);
