using System.Globalization;
using Platform.SharedKernel.Primitives;

namespace Platform.Domain.Settings;

/// <summary>Refusal codes of the tenant-settings capability (<c>SET-nnn</c>).</summary>
public static class SettingCodes
{
    public const string UnknownKey = "SET-001";
    public const string InvalidValue = "SET-002";

    /// <summary>A well-formed time-zone name this host does not know, e.g. <c>Mars/Olympus</c> (validation LIS-VR-002 round 4).</summary>
    public const string UnknownTimeZone = "SET-003";

    /// <summary>A client flag (<see cref="SettingKeys.IsClientFlag"/>) is set by the Super Admin on SA-04 only, never through the Parameters pages (LIS-CR-013).</summary>
    public const string SetByPlatformOnly = "SET-004";
}

/// <summary>The storage type of a setting value; every value is stored as text and parsed by its descriptor.</summary>
public enum SettingType
{
    Bool = 0,
    Int = 1,
    String = 2,
}

/// <summary>
/// One tenant parameter (SRS CP-57, SA-19 defaults): its key, type, code default and validation. Modules read a
/// value through <c>ITenantSettings</c> with the typed descriptors of <see cref="SettingKeys"/>; the Parameters page
/// edits the text values, which this descriptor validates. Keys are stable identifiers, never renamed.
/// </summary>
public abstract class SettingDescriptor
{
    public const int MaxKeyLength = 80;
    public const int MaxValueLength = 200;
    public const int MaxTimeZoneLength = 64;

    protected SettingDescriptor(string key, SettingType type, string defaultValue, string description, bool isTimeZone = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        if (key.Length > MaxKeyLength || !char.IsAsciiLetterLower(key[0]) || !key.All(c => char.IsAsciiLetterOrDigit(c)))
        {
            throw new ArgumentException($"Setting key '{key}' must be lower camel case (letters and digits, at most {MaxKeyLength} characters).", nameof(key));
        }

        Key = key;
        Type = type;
        DefaultValue = defaultValue;
        Description = description;
        IsTimeZone = isTimeZone;
    }

    public string Key { get; }

    public SettingType Type { get; }

    /// <summary>The code default, in the stored text form.</summary>
    public string DefaultValue { get; }

    public string Description { get; }

    /// <summary>True for an IANA time-zone setting: a well-formed name the host does not know is refused with <c>SET-003</c>.</summary>
    public bool IsTimeZone { get; }

    /// <summary>True when <paramref name="value"/> is a well-formed, in-range value for this setting (the stored text form).</summary>
    public abstract bool IsValid(string value);

    public static SettingDescriptor<bool> Bool(string key, bool defaultValue, string description) =>
        new(key, SettingType.Bool, defaultValue, description, text => bool.TryParse(text, out var value) ? (true, value) : (false, false), _ => true);

    public static SettingDescriptor<int> Int(string key, int defaultValue, string description, int min = 0, int max = int.MaxValue) =>
        new(key, SettingType.Int, defaultValue, description, text => int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? (true, value) : (false, 0), value => value >= min && value <= max);

    /// <summary>A free text (≤ 200 characters) or, when <paramref name="allowed"/> is given, one of a closed set (case-sensitive).</summary>
    public static SettingDescriptor<string> String(string key, string defaultValue, string description, params string[] allowed) =>
        new(key, SettingType.String, defaultValue, description, text => (true, text), value => value.Length <= MaxValueLength && (allowed.Length == 0 || allowed.Contains(value, StringComparer.Ordinal)));

    /// <summary>An IANA time zone name this host knows (<c>Africa/Cairo</c>), at most 64 characters, stored as text (round 2 R2-01).</summary>
    public static SettingDescriptor<string> TimeZone(string key, string defaultValue, string description) =>
        new(key, SettingType.String, defaultValue, description, text => (true, text), IsIanaTimeZone, isTimeZone: true);

    /// <summary>True for an IANA zone id this host resolves (the rule of a country's zone, GEO-007).</summary>
    public static bool IsIanaTimeZone(string value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= MaxTimeZoneLength && TimeZoneInfo.TryFindSystemTimeZoneById(value, out var zone) && zone.HasIanaId;
}

/// <summary>A typed descriptor: parses the stored text into <typeparamref name="T"/> and falls back to the default on anything malformed.</summary>
/// <typeparam name="T">The value type (<see cref="bool"/>, <see cref="int"/> or <see cref="string"/>).</typeparam>
public sealed class SettingDescriptor<T> : SettingDescriptor
{
    private readonly Func<string, (bool Ok, T Value)> _parse;
    private readonly Func<T, bool> _inRange;

    internal SettingDescriptor(string key, SettingType type, T defaultValue, string description, Func<string, (bool, T)> parse, Func<T, bool> inRange, bool isTimeZone = false)
        : base(key, type, Format(defaultValue), description, isTimeZone)
    {
        Default = defaultValue;
        _parse = parse;
        _inRange = inRange;
        if (!IsValid(DefaultValue))
        {
            throw new ArgumentException($"The default of setting '{key}' does not satisfy its own validation.", nameof(defaultValue));
        }
    }

    public T Default { get; }

    /// <summary>Parses the stored text; a malformed or out-of-range value yields the default (a bad row never breaks a module).</summary>
    public T Parse(string? value)
    {
        if (value is null)
        {
            return Default;
        }

        var (ok, parsed) = _parse(value.Trim());
        return ok && _inRange(parsed) ? parsed : Default;
    }

    /// <summary>
    /// The effective value of a tenant: its own valid value, else the inherited (platform) valid value, else the default —
    /// the rule <c>ITenantSettings</c> applies (intake LIS-INT-FND-001 §6), for readers that load the two rows themselves.
    /// </summary>
    public T Resolve(string? own, string? inherited) =>
        own is not null && IsValid(own) ? Parse(own) : inherited is not null && IsValid(inherited) ? Parse(inherited) : Default;

    public override bool IsValid(string value)
    {
        if (value is null)
        {
            return false;
        }

        var (ok, parsed) = _parse(value.Trim());
        return ok && _inRange(parsed);
    }

    private static string Format(T value) => value switch
    {
        bool b => b ? "true" : "false",
        int i => i.ToString(CultureInfo.InvariantCulture),
        string s => s,
        _ => throw new ArgumentException("Unsupported setting type.", nameof(value)),
    };
}

/// <summary>
/// Every tenant parameter the platform knows (SRS CP-57; SA-19 supplies the platform-wide defaults until the plat
/// module makes them editable). One static descriptor per key; <see cref="All"/> is the catalogue the API exposes
/// and validates against. A module adds its keys here only when the foundation has not (MODULE-PATTERN §3).
/// </summary>
public static class SettingKeys
{
    /// <summary>The platform default of <see cref="TimeZone"/>.</summary>
    public const string DefaultTimeZone = "Africa/Cairo";

    // Registration (CP-06, BR-01)
    public static readonly SettingDescriptor<bool> MandatoryMobile = SettingDescriptor.Bool("mandatoryMobile", false, "The patient mobile is required at registration.");
    public static readonly SettingDescriptor<bool> MandatoryDoctor = SettingDescriptor.Bool("mandatoryDoctor", false, "The referring doctor is required at registration.");
    public static readonly SettingDescriptor<bool> MandatoryNationalId = SettingDescriptor.Bool("mandatoryNationalId", false, "The national id is required at registration.");
    public static readonly SettingDescriptor<bool> SimilarNameWarning = SettingDescriptor.Bool("similarNameWarning", true, "Warn when a patient with a similar name and mobile exists (BR-01).");
    public static readonly SettingDescriptor<int> DiscountCapPercent = SettingDescriptor.Int("discountCapPercent", 20, "The highest discount a user may apply without a higher cap on the role, in percent.", 0, 100);
    public static readonly SettingDescriptor<string> AccessionFormat = SettingDescriptor.String("accessionFormat", "ACC-{yyyy}-{nnnnn}", "The accession number pattern; {yyyy} is the year and {nnnnn} the per-year sequence.");

    // Printing (CP-06, CP-10, CP-21)
    public static readonly SettingDescriptor<bool> AutoPrintLabels = SettingDescriptor.Bool("autoPrintLabels", true, "Print sample labels when a visit is saved.");
    public static readonly SettingDescriptor<bool> AutoPrintJobOrder = SettingDescriptor.Bool("autoPrintJobOrder", true, "Print the job order when a visit is saved.");
    public static readonly SettingDescriptor<bool> AutoPrintReceipt = SettingDescriptor.Bool("autoPrintReceipt", true, "Print the receipt when a payment is recorded.");
    public static readonly SettingDescriptor<bool> PrintUnverified = SettingDescriptor.Bool("printUnverified", false, "Allow printing reports that contain unverified results (BR-09 exception).");
    public static readonly SettingDescriptor<int> LabelCopies = SettingDescriptor.Int("labelCopies", 1, "Label copies per sample.", 1, 10);
    public static readonly SettingDescriptor<string> ResultLanguage = SettingDescriptor.String("resultLanguage", "en", "The language of printed reports.", "en", "ar");

    /// <summary>The laboratory's report design (LIS-CR-010, C-14); a report template or a payer contract may override it (<see cref="global::Platform.Domain.Catalogue.ReportDesigns.Resolve"/>).</summary>
    public static readonly SettingDescriptor<string> ReportDesign = SettingDescriptor.String("reportDesign", global::Platform.Domain.Catalogue.ReportDesigns.DefaultSettingValue, "The layout of printed result reports: classic, modern, compact, letterhead or cumulative.", [.. global::Platform.Domain.Catalogue.ReportDesigns.SettingValues]);

    // Samples and results (CP-14…CP-20, BR-03, BR-18)
    public static readonly SettingDescriptor<bool> AutoCollect = SettingDescriptor.Bool("autoCollect", true, "Samples are marked Collected when the visit is saved.");
    public static readonly SettingDescriptor<bool> AutoReceive = SettingDescriptor.Bool("autoReceive", true, "Samples are marked Received when collected (emulates the Received gate, BR-03).");
    public static readonly SettingDescriptor<bool> NewBarcodeOnReject = SettingDescriptor.Bool("newBarcodeOnReject", true, "A replacement sample carries a new barcode (BR-18).");
    public static readonly SettingDescriptor<bool> ReviewBeforeVerify = SettingDescriptor.Bool("reviewBeforeVerify", false, "Results pass through Reviewed before Verified.");
    public static readonly SettingDescriptor<int> DelayWarnPercent = SettingDescriptor.Int("delayWarnPercent", 80, "Share of the turnaround consumed at which a test is shown as at risk (BR-13).", 0, 1000);
    public static readonly SettingDescriptor<int> DelayLatePercent = SettingDescriptor.Int("delayLatePercent", 100, "Share of the turnaround consumed at which a test is shown as late (BR-13).", 0, 1000);

    // Security and platform defaults (SA-19)
    public static readonly SettingDescriptor<int> SessionTimeoutMinutes = SettingDescriptor.Int("sessionTimeoutMinutes", 30, "Idle minutes before the session ends: the client warns a minute before and signs out; the server refuses a later refresh (AUTH-017).", 1, 720);
    public static readonly SettingDescriptor<int> OtpLength = SettingDescriptor.Int("otpLength", 6, "Digits of a one-time code sent by the tenant's flows.", 4, 10);
    public static readonly SettingDescriptor<int> PasswordMinLength = SettingDescriptor.Int("passwordMinLength", 8, "Minimum password length for the tenant's users.", 8, 64);
    public static readonly SettingDescriptor<int> TrialDays = SettingDescriptor.Int("trialDays", 14, "Trial period of a new subscription, in days.", 0, 365);
    public static readonly SettingDescriptor<string> DefaultCurrency = SettingDescriptor.String("defaultCurrency", "EGP", "ISO 4217 currency of prices and payments.");
    public static readonly SettingDescriptor<string> DefaultLanguage = SettingDescriptor.String("defaultLanguage", "en", "Default user-interface language.", "en", "ar");
    public static readonly SettingDescriptor<string> MasterDataUpdatePolicy = SettingDescriptor.String("masterDataUpdatePolicy", "propose", "How later global master-data changes reach the tenant (BR-25): push, propose or ignore.", "push", "propose", "ignore");

    /// <summary>
    /// The laboratory's time zone where its client record names none (round 2 R2-01): a laboratory without a client record
    /// (the demo laboratory, a development tenant) dates registrations and stated ages by this zone, the platform's value
    /// when the laboratory set none, else Africa/Cairo, never by the UTC day.
    /// </summary>
    public static readonly SettingDescriptor<string> TimeZone = SettingDescriptor.TimeZone("timeZone", DefaultTimeZone, "IANA time zone of the laboratory's calendar day when its client record names no country zone.");

    // Subscription (LIS-CR-013, C-17)
    /// <summary>
    /// The client opted out of the announcements of National Technology and its partners (decision D-4): no sign-in hero, no
    /// welcome card. A <b>client flag</b> (<see cref="IsClientFlag"/>): set by the Super Admin on SA-04 only
    /// (<c>PUT /api/v1/platform/clients/{id}/announcements</c>), stored as the laboratory's own row, and only that row is read —
    /// <c>PUT /api/v1/settings</c> refuses it (SET-004), so neither a laboratory nor SA-19 (for every laboratory at once) sets it.
    /// </summary>
    public static readonly SettingDescriptor<bool> HideAnnouncements = SettingDescriptor.Bool("hideAnnouncements", false, "The client does not see the platform's announcements (sign-in hero, welcome card).");

    // Subscription (LIS-CR-012, C-16)
    /// <summary>
    /// The grace period of an expired subscription in days (decision D-3, default 7, 0…60): after a trial ends (not activated) or
    /// a wallet has stayed at or below zero for a plan period, the client keeps working this many days, then only its
    /// administrators sign in, to the wallet (<see cref="PlatformAdmin.SubscriptionTerms"/>). A <b>platform</b> parameter: only
    /// the platform tenant's row (SA-19) is read — a laboratory's own row is ignored, so a laboratory cannot extend its grace.
    /// </summary>
    public static readonly SettingDescriptor<int> SubscriptionGraceDays = SettingDescriptor.Int("subscriptionGraceDays", PlatformAdmin.SubscriptionTerms.DefaultGraceDays, "Days a client keeps working after its trial ended or its wallet stayed at or below zero for a plan period; then only its administrators sign in, to the wallet.", PlatformAdmin.SubscriptionTerms.MinGraceDays, PlatformAdmin.SubscriptionTerms.MaxGraceDays);

    private static readonly SettingDescriptor[] Catalogue =
    [
        MandatoryMobile, MandatoryDoctor, MandatoryNationalId, SimilarNameWarning, DiscountCapPercent, AccessionFormat,
        AutoPrintLabels, AutoPrintJobOrder, AutoPrintReceipt, PrintUnverified, LabelCopies, ResultLanguage, ReportDesign,
        AutoCollect, AutoReceive, NewBarcodeOnReject, ReviewBeforeVerify, DelayWarnPercent, DelayLatePercent,
        SessionTimeoutMinutes, OtpLength, PasswordMinLength, TrialDays, DefaultCurrency, DefaultLanguage, MasterDataUpdatePolicy, TimeZone,
        SubscriptionGraceDays,
        HideAnnouncements,
    ];

    /// <summary>LIS-CR-013: client flags the Super Admin sets per client through its own endpoint; the Parameters pages never write them (SET-004).</summary>
    private static readonly HashSet<string> ClientFlags = new(StringComparer.Ordinal) { HideAnnouncements.Key };

    /// <summary>True for a client flag: <c>PUT /api/v1/settings</c> refuses it with <see cref="SettingCodes.SetByPlatformOnly"/>.</summary>
    public static bool IsClientFlag(string key) => key is not null && ClientFlags.Contains(key);

    private static readonly Dictionary<string, SettingDescriptor> ByKey = Catalogue.ToDictionary(descriptor => descriptor.Key, StringComparer.Ordinal);

    /// <summary>Every descriptor, in display order.</summary>
    public static IReadOnlyList<SettingDescriptor> All => Catalogue;

    public static SettingDescriptor? Find(string key) => key is not null && ByKey.TryGetValue(key, out var descriptor) ? descriptor : null;

    public static bool IsKnown(string key) => Find(key) is not null;

    /// <summary>
    /// Validates one stored value against its descriptor: <c>SET-001</c> for an unknown key, <c>SET-003</c> for a time-zone
    /// name (non-blank, at most 64 characters) the host does not know, <c>SET-002</c> for any other malformed or out-of-range value.
    /// </summary>
    public static SettingDescriptor Validate(string key, string value)
    {
        var descriptor = Find(key) ?? throw new DomainException(SettingCodes.UnknownKey, $"Unknown setting '{key}'.", DomainFault.Validation);
        if (value is null || !descriptor.IsValid(value))
        {
            if (descriptor.IsTimeZone && !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= SettingDescriptor.MaxTimeZoneLength)
            {
                throw new DomainException(SettingCodes.UnknownTimeZone, $"'{value.Trim()}' is not a time zone this host knows for '{key}' (an IANA name such as Africa/Cairo).", DomainFault.Validation);
            }

            throw new DomainException(SettingCodes.InvalidValue, $"'{value}' is not a valid {descriptor.Type} value for '{key}'.", DomainFault.Validation);
        }

        return descriptor;
    }
}
