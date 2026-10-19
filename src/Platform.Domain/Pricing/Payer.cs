using Platform.SharedKernel.Localization;
using Platform.SharedKernel.MultiTenancy;
using Platform.SharedKernel.Primitives;

namespace Platform.Domain.Pricing;

/// <summary>
/// A payer (SRS CP-32, LDM "Rank") with its contract tiers (LDM "Relative"). Reference-like aggregate: created, edited,
/// deactivated instead of deleted (a payer with visits is never deleted). Every tenant has exactly one default payer of
/// type Cash with a default tier (intake Q1); both are locked against deactivation and the payer against a type change.
/// The tiers live in <c>app.contract_tiers</c> and change only through this root.
/// </summary>
public sealed class Payer : AggregateRoot, ITenantScoped
{
    public const int MaxSearchKeyLength = 32;
    public const int MaxWebUsernameLength = 64;
    public const int MaxContactNumberLength = PayerContacts.MaxNumberLength;
    public const int MaxEmailLength = PayerContacts.MaxEmailLength;
    public const string DefaultSearchKey = "CASH";

    private readonly List<ContractTier> _tiers = [];

    private Payer()
    {
        Name = null!;
        SearchKey = string.Empty;
    }

    private Payer(Guid id, LocalizedText name, string searchKey, PayerType type, bool isDefault)
        : base(id)
    {
        Name = name;
        SearchKey = searchKey;
        Type = type;
        IsDefault = isDefault;
        IsActive = true;
    }

    public Guid TenantId { get; private set; }

    public LocalizedText Name { get; private set; }

    /// <summary>Short upper-case key typed at registration, unique per tenant (INV-PRC-001).</summary>
    public string SearchKey { get; private set; }

    public PayerType Type { get; private set; }

    /// <summary>The payer's web-results portal is enabled; the username is then required (INV-PRC-002). The portal itself is a later intake (Q9).</summary>
    public bool WebPortalEnabled { get; private set; }

    public string? WebUsername { get; private set; }

    /// <summary>Office phone of the payer (CR 2026-10-19 C19), as typed; optional.</summary>
    public string? Phone1 { get; private set; }

    public string? Phone2 { get; private set; }

    public string? WhatsApp { get; private set; }

    public string? Email { get; private set; }

    /// <summary>The four contact values as one value (C19); not mapped, the columns are the scalars above.</summary>
    public PayerContacts Contacts => new(Phone1, Phone2, WhatsApp, Email);

    public bool IsActive { get; private set; }

    /// <summary>The tenant's cash payer (INV-PRC-003): one per tenant, created by <see cref="CreateDefaultCash"/>.</summary>
    public bool IsDefault { get; private set; }

    public uint RowVersion { get; private set; }

    public IReadOnlyCollection<ContractTier> Tiers => _tiers.AsReadOnly();

    /// <summary>P1 Create (<c>pricing.create</c>). Uniqueness of the search key and the web username is checked by the handler and enforced by the database.</summary>
    public static Payer Create(LocalizedText name, string searchKey, PayerType type, bool webPortalEnabled, string? webUsername, DateTimeOffset nowUtc) =>
        Create(name, searchKey, type, webPortalEnabled, webUsername, PayerContacts.None, nowUtc);

    /// <summary>P1 Create with the office contacts (C19); <paramref name="contacts"/> is cleaned and validated (PRC-017 / PRC-018).</summary>
    public static Payer Create(LocalizedText name, string searchKey, PayerType type, bool webPortalEnabled, string? webUsername, PayerContacts contacts, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(contacts);
        var payer = new Payer(Guid.CreateVersion7(), name, CleanSearchKey(searchKey), type, isDefault: false);
        payer.SetWebPortal(webPortalEnabled, webUsername);
        payer.SetContacts(contacts.Clean());
        payer.Raise(new PayerCreated(payer.Id, payer.SearchKey, type, false, nowUtc));
        return payer;
    }

    /// <summary>P8 Ensure default (system, lazy): the tenant's cash payer "Cash / نقدي" with the default tier "Default / افتراضي".</summary>
    public static Payer CreateDefaultCash(string searchKey, DateTimeOffset nowUtc)
    {
        var payer = new Payer(Guid.CreateVersion7(), new LocalizedText("Cash", "نقدي"), CleanSearchKey(searchKey), PayerType.Cash, isDefault: true);
        payer.Raise(new PayerCreated(payer.Id, payer.SearchKey, PayerType.Cash, true, nowUtc));
        var tier = new ContractTier(payer.Id, new LocalizedText("Default", "افتراضي"), isDefault: true);
        payer._tiers.Add(tier);
        payer.Raise(new ContractTierAdded(payer.Id, tier.Id, true, nowUtc));
        return payer;
    }

    /// <summary>
    /// P2 Edit (<c>pricing.edit</c>): name, search key, type and web portal; the default payer stays Cash (PRC-020).
    /// Raises <see cref="PayerUpdated"/> only when something changed, so the audit trail records the edit beside its
    /// field changes (validation v0.5.0 O-5).
    /// </summary>
    public void Update(LocalizedText name, string searchKey, PayerType type, bool webPortalEnabled, string? webUsername, DateTimeOffset nowUtc) =>
        Update(name, searchKey, type, webPortalEnabled, webUsername, Contacts, nowUtc);

    /// <summary>P2 Edit including the office contacts (C19): one <see cref="PayerUpdated"/> when anything, contacts included, changed.</summary>
    public void Update(LocalizedText name, string searchKey, PayerType type, bool webPortalEnabled, string? webUsername, PayerContacts contacts, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(contacts);
        Require(!IsDefault || type == PayerType.Cash, PricingCodes.DefaultPayerLocked, "The default cash payer must stay of type Cash.", DomainFault.Conflict);

        var before = (Name, SearchKey, Type, WebPortalEnabled, WebUsername, Contacts);
        var key = CleanSearchKey(searchKey);
        var cleanedContacts = contacts.Clean();
        SetWebPortal(webPortalEnabled, webUsername);
        Name = name;
        SearchKey = key;
        Type = type;
        SetContacts(cleanedContacts);
        if (before != (Name, SearchKey, Type, WebPortalEnabled, WebUsername, Contacts))
        {
            Raise(new PayerUpdated(Id, SearchKey, nowUtc));
        }
    }

    /// <summary>P3 Deactivate (<c>pricing.manage</c>). Idempotent; the default payer is locked (PRC-020).</summary>
    public void Deactivate(DateTimeOffset nowUtc)
    {
        Require(!IsDefault, PricingCodes.DefaultPayerLocked, "The default cash payer cannot be deactivated.", DomainFault.Conflict);
        if (!IsActive)
        {
            return;
        }

        IsActive = false;
        Raise(new PayerDeactivated(Id, SearchKey, nowUtc));
    }

    /// <summary>P4 Reactivate (<c>pricing.manage</c>). Idempotent.</summary>
    public void Reactivate(DateTimeOffset nowUtc)
    {
        if (IsActive)
        {
            return;
        }

        IsActive = true;
        Raise(new PayerReactivated(Id, SearchKey, nowUtc));
    }

    /// <summary>P5 Add tier (<c>pricing.create</c>): names unique within the payer (PRC-103).</summary>
    public ContractTier AddTier(LocalizedText name, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(name);
        RequireUniqueTierName(name, exceptTierId: null);

        var tier = new ContractTier(Id, name, isDefault: false);
        _tiers.Add(tier);
        Raise(new ContractTierAdded(Id, tier.Id, false, nowUtc));
        return tier;
    }

    /// <summary>P6 Rename tier (<c>pricing.edit</c>). Raises <see cref="ContractTierRenamed"/> when the name changed (validation v0.5.0 O-5).</summary>
    public void RenameTier(Guid tierId, LocalizedText name, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(name);
        var tier = TierOf(tierId);
        RequireUniqueTierName(name, tierId);
        if (tier.Name.Equals(name))
        {
            return;
        }

        tier.Rename(name);
        Raise(new ContractTierRenamed(Id, tier.Id, nowUtc));
    }

    /// <summary>P7 Deactivate tier (<c>pricing.manage</c>). Idempotent; the default tier is locked (PRC-021).</summary>
    public void DeactivateTier(Guid tierId, DateTimeOffset nowUtc)
    {
        var tier = TierOf(tierId);
        Require(!tier.IsDefault, PricingCodes.DefaultTierLocked, "The default tier of the cash payer cannot be deactivated.", DomainFault.Conflict);
        if (tier.SetActive(false))
        {
            Raise(new ContractTierDeactivated(Id, tier.Id, nowUtc));
        }
    }

    /// <summary>P7 Reactivate tier (<c>pricing.manage</c>). Idempotent.</summary>
    public void ReactivateTier(Guid tierId, DateTimeOffset nowUtc)
    {
        var tier = TierOf(tierId);
        if (tier.SetActive(true))
        {
            Raise(new ContractTierReactivated(Id, tier.Id, nowUtc));
        }
    }

    public ContractTier? FindTier(Guid tierId) => _tiers.FirstOrDefault(tier => tier.Id == tierId);

    public static string CleanSearchKey(string? searchKey) =>
        PriceMath.CleanKey(searchKey, MaxSearchKeyLength, PricingCodes.SearchKeyInvalid, $"The search key must be 1 to {MaxSearchKeyLength} characters: a letter or digit, then letters, digits, '.', '-' or '_'.");

    /// <summary>The username as stored (trimmed, lower-case) or <see langword="null"/> when the portal is off and none was given.</summary>
    public static string? CleanWebUsername(bool webPortalEnabled, string? webUsername)
    {
        var cleaned = string.IsNullOrWhiteSpace(webUsername) ? null : webUsername.Trim().ToLowerInvariant();
        Require(!webPortalEnabled || cleaned is not null, PricingCodes.WebUsernameRequired, "A web username is required when the web-results portal is enabled.", DomainFault.Validation);
        Require(
            cleaned is null || (cleaned.Length <= MaxWebUsernameLength && cleaned.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_' or '@')),
            PricingCodes.WebUsernameInvalid,
            $"The web username must be at most {MaxWebUsernameLength} characters: letters, digits, '.', '-', '_' or '@'.",
            DomainFault.Validation);
        return cleaned;
    }

    private void SetContacts(PayerContacts cleaned)
    {
        Phone1 = cleaned.Phone1;
        Phone2 = cleaned.Phone2;
        WhatsApp = cleaned.WhatsApp;
        Email = cleaned.Email;
    }

    private void SetWebPortal(bool enabled, string? username)
    {
        WebUsername = CleanWebUsername(enabled, username);
        WebPortalEnabled = enabled;
    }

    private ContractTier TierOf(Guid tierId) =>
        FindTier(tierId) ?? throw new DomainException(PricingCodes.TierNotFound, "Contract tier not found.", DomainFault.NotFound);

    private void RequireUniqueTierName(LocalizedText name, Guid? exceptTierId)
    {
        var clash = _tiers.Any(tier => tier.Id != exceptTierId
                                       && (string.Equals(tier.Name.En, name.En, StringComparison.OrdinalIgnoreCase)
                                           || string.Equals(tier.Name.Ar, name.Ar, StringComparison.OrdinalIgnoreCase)));
        Require(!clash, PricingCodes.TierNameExists, "The payer already has a tier with this name.", DomainFault.Conflict);
    }
}

/// <summary>A contract tier of a payer (LDM "Relative"): employees, families, gold … Changed only through <see cref="Payer"/>.</summary>
public sealed class ContractTier : Entity, ITenantScoped
{
    private ContractTier()
    {
        Name = null!;
    }

    internal ContractTier(Guid payerId, LocalizedText name, bool isDefault)
        : base(Guid.CreateVersion7())
    {
        PayerId = payerId;
        Name = name;
        IsDefault = isDefault;
        IsActive = true;
    }

    public Guid TenantId { get; private set; }

    public Guid PayerId { get; private set; }

    public LocalizedText Name { get; private set; }

    public bool IsActive { get; private set; }

    /// <summary>The cash payer's default tier (locked, INV-PRC-003).</summary>
    public bool IsDefault { get; private set; }

    internal void Rename(LocalizedText name) => Name = name;

    /// <summary>Returns true when the flag changed.</summary>
    internal bool SetActive(bool active)
    {
        if (IsActive == active)
        {
            return false;
        }

        IsActive = active;
        return true;
    }
}

/// <summary>
/// The office contacts of a payer (CR 2026-10-19 C19): phones and WhatsApp as typed (digits, spaces, <c>+ - ( )</c>, at most 32
/// characters, at least one digit; PRC-017), an e-mail address of basic shape (one <c>@</c>, a dotted domain, no spaces, at
/// most 254 characters; PRC-018). Every value is optional; blanks become <see langword="null"/>. Owned by <see cref="Payer"/>.
/// </summary>
public sealed record PayerContacts(string? Phone1, string? Phone2, string? WhatsApp, string? Email)
{
    public const int MaxNumberLength = 32;
    public const int MaxEmailLength = 254;

    public static readonly PayerContacts None = new(null, null, null, null);

    /// <summary>Trimmed, inner whitespace collapsed, blanks to null; refuses PRC-017 / PRC-018.</summary>
    public PayerContacts Clean() => new(CleanNumber(Phone1, "Phone 1"), CleanNumber(Phone2, "Phone 2"), CleanNumber(WhatsApp, "WhatsApp"), CleanEmail(Email));

    public static string? CleanNumber(string? value, string what)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var cleaned = string.Join(' ', value.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        var wellFormed = cleaned.Length <= MaxNumberLength
            && cleaned.Any(char.IsAsciiDigit)
            && cleaned.All(c => char.IsAsciiDigit(c) || c is ' ' or '+' or '-' or '(' or ')');
        if (!wellFormed)
        {
            throw new DomainException(PricingCodes.ContactNumberInvalid, $"{what} must be at most {MaxNumberLength} characters: digits, spaces, '+', '-', '(' and ')'.", DomainFault.Validation);
        }

        return cleaned;
    }

    public static string? CleanEmail(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var cleaned = value.Trim();
        var at = cleaned.IndexOf('@', StringComparison.Ordinal);
        var domain = at >= 0 ? cleaned[(at + 1)..] : string.Empty;
        var wellFormed = cleaned.Length <= MaxEmailLength
            && at > 0
            && at == cleaned.LastIndexOf('@')
            && domain.Length >= 3
            && domain.Contains('.', StringComparison.Ordinal)
            && !domain.StartsWith('.')
            && !domain.EndsWith('.')
            && !cleaned.Any(char.IsWhiteSpace);
        if (!wellFormed)
        {
            throw new DomainException(PricingCodes.EmailInvalid, $"The e-mail must look like name@domain.tld (at most {MaxEmailLength} characters).", DomainFault.Validation);
        }

        return cleaned;
    }
}
