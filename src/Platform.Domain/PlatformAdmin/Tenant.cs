using Platform.SharedKernel.Localization;
using Platform.SharedKernel.MultiTenancy;
using Platform.SharedKernel.Primitives;

namespace Platform.Domain.PlatformAdmin;

/// <summary>
/// A client laboratory (SA-04): the platform-side record of a tenant (<c>saas.tenants</c>; its <see cref="Entity{TId}.Id"/>
/// IS the tenant id every row of the laboratory carries). Created by provisioning (manual or the subscription wizard),
/// it carries the contact data, the plan, the portal address and the life cycle Trial → Active ↔ Inactive of intake
/// §6–§7. Deactivation blocks every user of the client at sign-in (<c>ITenantStatusLookup</c>, <c>CLI-020</c>).
/// </summary>
public sealed class Tenant : AggregateRoot
{
    public const int MaxNameLength = 200;
    public const int MaxAddressLength = 300;
    public const int MaxLicenceLength = 64;
    public const int MaxManagerNameLength = 200;
    public const int MaxMobileLength = 20;
    public const int MaxEmailLength = 254;
    public const int MaxReasonLength = 500;
    public const int MinSlugLength = 3;
    public const int MaxSlugLength = 40;
    /// <summary>LIS-CR-005: the path segment of a laboratory's sign-in address (<c>{base}/lab/{slug}</c>).</summary>
    public const string LaboratoryPathSegment = "lab";

    /// <summary>Addresses the platform keeps for itself (formerly sub-domains; still refused as laboratory addresses).</summary>
    public static readonly IReadOnlySet<string> ReservedSlugs = new HashSet<string>(StringComparer.Ordinal)
    {
        "www", "api", "app", "admin", "portal", "platform", "mail", "smtp", "static", "cdn", "status", "help", "support", "login", "auth", "docs", "test", "demo",
    };

    private Tenant()
    {
        Name = null!;
        Address = string.Empty;
        Mobile = string.Empty;
        ManagerName = string.Empty;
        ManagerMobile = string.Empty;
        PortalSlug = string.Empty;
        PreferredLanguage = "en";
    }

    private Tenant(Guid id, ClientProfile profile, Guid planId, string portalSlug, TenantSource source, string language)
        : base(id)
    {
        Name = profile.Name;
        Address = profile.Address;
        Mobile = profile.Mobile;
        ManagerName = profile.ManagerName;
        ManagerMobile = profile.ManagerMobile;
        PortalSlug = portalSlug;
        PreferredLanguage = language;
        Source = source;
        PlanId = planId;
        Apply(profile);
    }

    /// <summary>
    /// Always <see cref="TenantKind.Laboratory"/> for an aggregate the domain creates or loads: the platform row of the registry
    /// (<see cref="TenantKind.Platform"/>) is filtered out of every read of the model and has no country, governorate or plan.
    /// </summary>
    public TenantKind Kind { get; private set; } = TenantKind.Laboratory;

    public LocalizedText Name { get; private set; }

    public Guid CountryId { get; private set; }

    public Guid GovernorateId { get; private set; }

    public string Address { get; private set; }

    /// <summary>LIS-CR-008 C-04: the city (markaz) of the laboratory under its governorate, optional (<see cref="Locate"/>).</summary>
    public Guid? CityId { get; private set; }

    /// <summary>LIS-CR-008 C-04: the district under the city, optional.</summary>
    public Guid? DistrictId { get; private set; }

    /// <summary>LIS-CR-008 C-04: the area (village) under the district, optional.</summary>
    public Guid? AreaId { get; private set; }

    /// <summary>E.164, validated by the country's mobile length.</summary>
    public string Mobile { get; private set; }

    public string? LicenceNo { get; private set; }

    public string ManagerName { get; private set; }

    /// <summary>E.164; receives the one-time code of the wizard and the portal address.</summary>
    public string ManagerMobile { get; private set; }

    public string? Email { get; private set; }

    public Guid PlanId { get; private set; }

    /// <summary>The laboratory address (LIS-CR-005: sign-in at <c>{base}/lab/{slug}</c>); unique (INV-PLT-001).</summary>
    public string PortalSlug { get; private set; }

    public TenantSource Source { get; private set; }

    public TenantStatus Status { get; private set; }

    public DateTimeOffset? TrialEndsAtUtc { get; private set; }

    public DateTimeOffset? ActivatedAtUtc { get; private set; }

    public DateTimeOffset? DeactivatedAtUtc { get; private set; }

    public string? DeactivationReason { get; private set; }

    /// <summary>The laboratory administrator staged by provisioning (SA-04 "reset password" acts on this user).</summary>
    public Guid? AdministratorUserId { get; private set; }

    public string? AdministratorUsername { get; private set; }

    /// <summary>Language of the notifications sent to the laboratory (<c>en</c> / <c>ar</c>).</summary>
    public string PreferredLanguage { get; private set; }

    public uint RowVersion { get; private set; }

    /// <summary>
    /// P1 Provision. The country and governorate must be active and match (GEO-011/012/013), the plan active (PLN-010).
    /// A subscription starts in Trial for <paramref name="trialDays"/> days (Active at once when 0); a manual client
    /// starts Active (WF-01: "creates the client manually and activates it").
    /// </summary>
    public static Tenant Provision(ClientProfile profile, Country country, Governorate governorate, Plan plan, string portalSlug, TenantSource source, string language, int trialDays, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(plan);
        CheckPlace(profile, country, governorate);
        Require(plan.IsActive, PlatformAdminCodes.PlanInactive, "The plan is inactive.", DomainFault.Conflict);
        Require(trialDays is >= 0 and <= 365, PlatformAdminCodes.ClientDetailsInvalid, "The trial lasts 0 to 365 days.", DomainFault.Validation);

        var tenant = new Tenant(Guid.CreateVersion7(), profile, plan.Id, CleanSlug(portalSlug), source, CleanLanguage(language));
        Require(!TenantIds.IsPlatform(tenant.Id), PlatformAdminCodes.ClientDetailsInvalid, "The platform tenant is not a client.", DomainFault.Refused);
        if (source == TenantSource.Subscription && trialDays > 0)
        {
            tenant.Status = TenantStatus.Trial;
            tenant.TrialEndsAtUtc = nowUtc.AddDays(trialDays);
        }
        else
        {
            tenant.Status = TenantStatus.Active;
            tenant.ActivatedAtUtc = nowUtc;
        }

        tenant.Raise(new TenantProvisioned(tenant.Id, tenant.PortalSlug, source.ToString(), plan.Id, nowUtc));
        return tenant;
    }

    /// <summary>Records the administrator the organization module staged for this tenant.</summary>
    public void AttachAdministrator(Guid userId, string username)
    {
        Require(userId != Guid.Empty, PlatformAdminCodes.AdministratorMissing, "The administrator is required.", DomainFault.Validation);
        AdministratorUserId = userId;
        AdministratorUsername = DomainText.Required(username, 64, PlatformAdminCodes.UsernameInvalid, "The administrator username");
    }

    /// <summary>P4 Edit details (any state). The place is re-checked against the country and governorate.</summary>
    public void UpdateProfile(ClientProfile profile, Country country, Governorate governorate, string language)
    {
        ArgumentNullException.ThrowIfNull(profile);
        CheckPlace(profile, country, governorate);
        Name = profile.Name;
        Address = profile.Address;
        Mobile = profile.Mobile;
        ManagerName = profile.ManagerName;
        ManagerMobile = profile.ManagerMobile;
        PreferredLanguage = CleanLanguage(language);
        Apply(profile);
    }

    /// <summary>P5 Change plan (any state): the new plan must be active (PLN-010). Idempotent.</summary>
    public void ChangePlan(Plan plan, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.Id == PlanId)
        {
            return;
        }

        Require(plan.IsActive, PlatformAdminCodes.PlanInactive, "The plan is inactive.", DomainFault.Conflict);
        var from = PlanId;
        PlanId = plan.Id;
        Raise(new TenantPlanChanged(Id, from, plan.Id, nowUtc));
    }

    /// <summary>P2 Activate: Trial or Inactive → Active (CLI-010 when already active). Ends a trial.</summary>
    public void Activate(DateTimeOffset nowUtc)
    {
        if (Status == TenantStatus.Active)
        {
            throw new InvalidStateTransitionException(PlatformAdminCodes.ClientAlreadyActive, "client", Status.ToString(), "activate");
        }

        Status = TenantStatus.Active;
        ActivatedAtUtc = nowUtc;
        DeactivatedAtUtc = null;
        DeactivationReason = null;
        TrialEndsAtUtc = null;
        Raise(new TenantActivated(Id, nowUtc));
    }

    /// <summary>P3 Deactivate: Trial or Active → Inactive with a reason (CLI-021); CLI-011 when already inactive.</summary>
    public void Deactivate(string reason, DateTimeOffset nowUtc)
    {
        if (Status == TenantStatus.Inactive)
        {
            throw new InvalidStateTransitionException(PlatformAdminCodes.ClientAlreadyInactive, "client", Status.ToString(), "deactivate");
        }

        var cleaned = DomainText.Required(reason, MaxReasonLength, PlatformAdminCodes.DeactivationReasonRequired, "The deactivation reason");
        Status = TenantStatus.Inactive;
        DeactivatedAtUtc = nowUtc;
        DeactivationReason = cleaned;
        Raise(new TenantDeactivated(Id, cleaned, nowUtc));
    }

    /// <summary>
    /// LIS-CR-008 C-04: places the laboratory in a city / district / area of its governorate (any state). The locality was
    /// checked by <see cref="Locality.Resolve"/>; one resolved against another governorate is refused (GEO-020). Call after
    /// <see cref="Provision"/> or <see cref="UpdateProfile"/>, so the check uses the governorate just set;
    /// <see cref="Locality.None"/> clears it.
    /// </summary>
    public void Locate(Locality locality)
    {
        ArgumentNullException.ThrowIfNull(locality);
        locality.EnsureIn(GovernorateId);
        CityId = locality.CityId;
        DistrictId = locality.DistrictId;
        AreaId = locality.AreaId;
    }

    /// <summary>
    /// INV-PLT-003 as amended by LIS-CR-012 (D-3): the client's users reach sign-in unless the client is deactivated (<c>CLI-020</c>).
    /// A trial past its end is no longer refused here: it runs its grace period and then expires, and what an expired client's
    /// users may do is decided by <see cref="SubscriptionState"/> (<c>SUB-020</c> / <c>SUB-021</c>).
    /// </summary>
    public bool IsUsableAt(DateTimeOffset nowUtc) => Status != TenantStatus.Inactive;

    /// <summary>
    /// LIS-CR-012: the subscription state at <paramref name="nowUtc"/> — Ok, Warning, Grace or Expired with the days left — from the
    /// trial end, the wallet (<paramref name="wallet"/>: balance, plan fee, zero mark) and the platform's grace days
    /// (<see cref="SubscriptionTerms.Evaluate"/>).
    /// </summary>
    public SubscriptionStatus SubscriptionState(DateTimeOffset nowUtc, int graceDays, WalletPosition? wallet) =>
        SubscriptionTerms.Evaluate(Status, TrialEndsAtUtc, wallet, graceDays, nowUtc);

    /// <summary>LIS-CR-005: the laboratory's sign-in address, <c>{portalBaseUrl}/lab/{slug}</c>.</summary>
    public string PortalUrl(string portalBaseUrl) => PortalUrlOf(PortalSlug, portalBaseUrl);

    /// <summary>
    /// LIS-CR-005: <c>{portalBaseUrl}/lab/{slug}</c>. The base is the configured <c>Lis:Platform:PortalBaseUrl</c> (an absolute
    /// URL without path; a trailing slash is tolerated here, the start-up guard refuses it in configuration).
    /// </summary>
    public static string PortalUrlOf(string slug, string portalBaseUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);
        ArgumentException.ThrowIfNullOrWhiteSpace(portalBaseUrl);
        return $"{portalBaseUrl.Trim().TrimEnd('/')}/{LaboratoryPathSegment}/{slug}";
    }

    /// <summary>
    /// LIS-CR-005: the shape of a laboratory address as typed or sent at sign-in (trimmed, lower-cased): 3–40 characters of
    /// <c>a-z 0-9 -</c>. Shape only (no reserved-word or hyphen-placement rule): an address that passes and names no
    /// laboratory is answered like a wrong password, never as a different refusal.
    /// </summary>
    public static bool IsAddressShape(string? slug)
    {
        var value = NormaliseAddress(slug);
        return value.Length is >= MinSlugLength and <= MaxSlugLength && value.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '-');
    }

    /// <summary>LIS-CR-005: a laboratory address as compared with the stored slug (trimmed, lower-cased).</summary>
    public static string NormaliseAddress(string? slug) => (slug ?? string.Empty).Trim().ToLowerInvariant();

    /// <summary>
    /// LIS-CR-005 (development tooling): the client record of a tenant that already exists without one (a laboratory seeded
    /// before client records, e.g. the demo laboratory), so it gets a sign-in address. Active at once, source Manual; the
    /// country, governorate and plan must exist and match but need not be active (placeholders). Raises no event: nothing is
    /// provisioned (no wallet, no administrator, no catalogue copy).
    /// </summary>
    public static Tenant RegisterExisting(Guid tenantId, ClientProfile profile, Country country, Governorate governorate, Plan plan, string portalSlug, string language, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(country);
        ArgumentNullException.ThrowIfNull(governorate);
        ArgumentNullException.ThrowIfNull(plan);
        Require(tenantId != Guid.Empty && !TenantIds.IsPlatform(tenantId), PlatformAdminCodes.ClientDetailsInvalid, "The platform tenant is not a client.", DomainFault.Refused);
        Require(country.Id == profile.CountryId && governorate.Id == profile.GovernorateId && governorate.CountryId == country.Id, PlatformAdminCodes.ClientDetailsInvalid, "The country and governorate do not match the profile.", DomainFault.Validation);
        var tenant = new Tenant(tenantId, profile, plan.Id, CleanSlug(portalSlug), TenantSource.Manual, CleanLanguage(language))
        {
            Status = TenantStatus.Active,
            ActivatedAtUtc = nowUtc,
        };
        return tenant;
    }

    /// <summary>INV-PLT-001: 3–40 characters of <c>a-z 0-9 -</c>, not starting or ending with '-', not reserved (CLI-001).</summary>
    public static string CleanSlug(string? slug)
    {
        var value = (slug ?? string.Empty).Trim().ToLowerInvariant();
        Require(
            value.Length is >= MinSlugLength and <= MaxSlugLength
            && value.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '-')
            && value[0] != '-' && value[^1] != '-'
            && !value.Contains("--", StringComparison.Ordinal)
            && !ReservedSlugs.Contains(value),
            PlatformAdminCodes.PortalSlugInvalid,
            $"The portal address is {MinSlugLength} to {MaxSlugLength} characters of a-z, 0-9 and '-', not reserved.",
            DomainFault.Validation);
        return value;
    }

    /// <summary>A slug derived from the English name ("Al Noor Lab" → <c>al-noor-lab</c>); <c>lab-…</c> when the name has too few latin letters.</summary>
    public static string SuggestSlug(string? nameEn, string fallbackSeed)
    {
        var builder = new System.Text.StringBuilder();
        foreach (var c in (nameEn ?? string.Empty).Trim().ToLowerInvariant())
        {
            if (char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c))
            {
                builder.Append(c);
            }
            else if (builder.Length > 0 && builder[^1] != '-')
            {
                builder.Append('-');
            }
        }

        var slug = builder.ToString().Trim('-');
        if (slug.Length > 30)
        {
            slug = slug[..30].TrimEnd('-');
        }

        if (slug.Length < MinSlugLength || ReservedSlugs.Contains(slug))
        {
            var seed = new string((fallbackSeed ?? string.Empty).ToLowerInvariant().Where(char.IsAsciiLetterOrDigit).ToArray());
            slug = "lab-" + (seed.Length > 8 ? seed[^8..] : seed.PadLeft(3, '0'));
        }

        return slug;
    }

    private static void CheckPlace(ClientProfile profile, Country country, Governorate governorate)
    {
        ArgumentNullException.ThrowIfNull(country);
        ArgumentNullException.ThrowIfNull(governorate);
        Require(country.Id == profile.CountryId && governorate.Id == profile.GovernorateId, PlatformAdminCodes.ClientDetailsInvalid, "The country and governorate do not match the profile.", DomainFault.Validation);
        Require(country.IsActive, PlatformAdminCodes.CountryInactive, "The country is inactive.", DomainFault.Conflict);
        Require(governorate.IsActive, PlatformAdminCodes.GovernorateInactive, "The governorate is inactive.", DomainFault.Conflict);
        Require(governorate.CountryId == country.Id, PlatformAdminCodes.GovernorateNotInCountry, "The governorate does not belong to the country.", DomainFault.Validation);
    }

    private static string CleanLanguage(string? language) =>
        language is not null && language.Trim().StartsWith("ar", StringComparison.OrdinalIgnoreCase) ? "ar" : "en";

    private void Apply(ClientProfile profile)
    {
        CountryId = profile.CountryId;
        GovernorateId = profile.GovernorateId;
        LicenceNo = profile.LicenceNo;
        Email = profile.Email;
    }
}

/// <summary>
/// The contact data of a client as SA-04 and CP-02 collect it. Mobiles must already be normalised by the country
/// (<see cref="Country.NormaliseMobile"/>); <see cref="Create"/> trims and checks the rest.
/// </summary>
public sealed record ClientProfile
{
    private ClientProfile(LocalizedText name, Guid countryId, Guid governorateId, string address, string mobile, string? licenceNo, string managerName, string managerMobile, string? email)
    {
        Name = name;
        CountryId = countryId;
        GovernorateId = governorateId;
        Address = address;
        Mobile = mobile;
        LicenceNo = licenceNo;
        ManagerName = managerName;
        ManagerMobile = managerMobile;
        Email = email;
    }

    public LocalizedText Name { get; }

    public Guid CountryId { get; }

    public Guid GovernorateId { get; }

    public string Address { get; }

    public string Mobile { get; }

    public string? LicenceNo { get; }

    public string ManagerName { get; }

    public string ManagerMobile { get; }

    public string? Email { get; }

    public static ClientProfile Create(string nameEn, string nameAr, Country country, Guid governorateId, string address, string mobile, string? licenceNo, string managerName, string managerMobile, string? email)
    {
        ArgumentNullException.ThrowIfNull(country);
        return new ClientProfile(
            DomainText.Localized(nameEn, nameAr, Tenant.MaxNameLength, PlatformAdminCodes.ClientDetailsInvalid, "The laboratory name"),
            country.Id,
            governorateId,
            DomainText.Required(address, Tenant.MaxAddressLength, PlatformAdminCodes.ClientDetailsInvalid, "The address"),
            country.NormaliseMobile(mobile, PlatformAdminCodes.MobileInvalidForCountry, "The laboratory mobile"),
            DomainText.Optional(licenceNo, Tenant.MaxLicenceLength, PlatformAdminCodes.ClientDetailsInvalid, "The medical licence"),
            DomainText.Required(managerName, Tenant.MaxManagerNameLength, PlatformAdminCodes.ClientDetailsInvalid, "The manager name"),
            country.NormaliseMobile(managerMobile, PlatformAdminCodes.ManagerMobileInvalidForCountry, "The manager's mobile"),
            DomainText.Email(email, PlatformAdminCodes.EmailInvalid));
    }
}
