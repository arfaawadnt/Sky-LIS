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
    public const string DefaultPortalDomain = "skylis.app";

    /// <summary>Sub-domains the platform keeps for itself.</summary>
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

    public LocalizedText Name { get; private set; }

    public Guid CountryId { get; private set; }

    public Guid GovernorateId { get; private set; }

    public string Address { get; private set; }

    /// <summary>E.164, validated by the country's mobile length.</summary>
    public string Mobile { get; private set; }

    public string? LicenceNo { get; private set; }

    public string ManagerName { get; private set; }

    /// <summary>E.164; receives the one-time code of the wizard and the portal address.</summary>
    public string ManagerMobile { get; private set; }

    public string? Email { get; private set; }

    public Guid PlanId { get; private set; }

    /// <summary>The sub-domain of the portal (<c>https://{slug}.{domain}</c>); unique (INV-PLT-001).</summary>
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

    /// <summary>INV-PLT-003: the client's users may sign in while Active, or in Trial before the trial ends.</summary>
    public bool IsUsableAt(DateTimeOffset nowUtc) =>
        Status == TenantStatus.Active || (Status == TenantStatus.Trial && (TrialEndsAtUtc is null || TrialEndsAtUtc > nowUtc));

    /// <summary><c>https://{slug}.{domain}</c>.</summary>
    public string PortalUrl(string portalDomain) => BuildPortalUrl(PortalSlug, portalDomain);

    public static string BuildPortalUrl(string slug, string portalDomain) =>
        $"https://{slug}.{(string.IsNullOrWhiteSpace(portalDomain) ? DefaultPortalDomain : portalDomain.Trim().Trim('.'))}";

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
