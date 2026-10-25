using Platform.Contracts.Common;

namespace Platform.Contracts.PlatformAdmin;

// Clients (SA-04) of the Super Admin portal. Validation here is shape only; the rules live in the aggregates.
// Statuses: Trial, Active, Inactive. Sources: Subscription, Manual. Mobiles are returned in E.164.
// LIS-CR-008 C-04: city / district / area are optional (null = none); the address lines read "Area, District, City, Governorate".

public sealed record ClientListItem(
    Guid Id,
    string NameEn,
    string NameAr,
    string PortalSlug,
    string PortalUrl,
    Guid CountryId,
    string CountryNameEn,
    string CountryNameAr,
    Guid GovernorateId,
    string GovernorateNameEn,
    string GovernorateNameAr,
    string ManagerName,
    string ManagerMobile,
    Guid PlanId,
    string PlanCode,
    string PlanNameEn,
    string PlanNameAr,
    decimal WalletBalance,
    string WalletCurrency,
    int Branches,
    int Users,
    DateTimeOffset CreatedAtUtc,
    string Source,
    string Status,
    DateTimeOffset? TrialEndsAtUtc,
    string? AddressLineEn = null,
    string? AddressLineAr = null);

public sealed record ClientDetail(
    Guid Id,
    string NameEn,
    string NameAr,
    Guid CountryId,
    Guid GovernorateId,
    string Address,
    string Mobile,
    string? LicenceNo,
    string ManagerName,
    string ManagerMobile,
    string? Email,
    Guid PlanId,
    string PortalSlug,
    string PortalUrl,
    string Language,
    string Source,
    string Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? TrialEndsAtUtc,
    DateTimeOffset? ActivatedAtUtc,
    DateTimeOffset? DeactivatedAtUtc,
    string? DeactivationReason,
    Guid? AdministratorUserId,
    string? AdministratorUsername,
    decimal WalletBalance,
    string WalletCurrency,
    int Branches,
    int Users,
    Guid? CityId = null,
    Guid? DistrictId = null,
    Guid? AreaId = null,
    string? AddressLineEn = null,
    string? AddressLineAr = null);

/// <summary>
/// SA-04 "Add": the client's details, the plan, an optional portal slug (derived from the English name when empty)
/// and the laboratory administrator's credentials. The password travels in the credential envelope (D-03).
/// </summary>
public sealed record ProvisionClientRequest
{
    public string NameEn { get; init; } = string.Empty;

    public string NameAr { get; init; } = string.Empty;

    public Guid CountryId { get; init; }

    public Guid GovernorateId { get; init; }

    public string Address { get; init; } = string.Empty;

    /// <summary>LIS-CR-008 C-04: optional city (markaz) / district / area under the governorate (GEO-020…022).</summary>
    public Guid? CityId { get; init; }

    public Guid? DistrictId { get; init; }

    public Guid? AreaId { get; init; }

    public string Mobile { get; init; } = string.Empty;

    public string? LicenceNo { get; init; }

    public string ManagerName { get; init; } = string.Empty;

    public string ManagerMobile { get; init; } = string.Empty;

    public string? Email { get; init; }

    public Guid PlanId { get; init; }

    public string? PortalSlug { get; init; }

    /// <summary><c>en</c> or <c>ar</c>: the language of the notifications sent to the laboratory.</summary>
    public string? Language { get; init; }

    public string AdminUsername { get; init; } = string.Empty;

    [CredentialField]
    public string AdminPassword { get; init; } = string.Empty;
}

public sealed record UpdateClientRequest(
    string NameEn,
    string NameAr,
    Guid CountryId,
    Guid GovernorateId,
    string Address,
    string Mobile,
    string? LicenceNo,
    string ManagerName,
    string ManagerMobile,
    string? Email,
    Guid PlanId,
    string? Language,
    Guid? CityId = null,
    Guid? DistrictId = null,
    Guid? AreaId = null);

public sealed record DeactivateClientRequest(string Reason);

/// <summary>SA-04 "Reset password": the new password of the laboratory administrator, in the credential envelope.</summary>
public sealed record ResetClientAdminPasswordRequest
{
    [CredentialField]
    public string NewPassword { get; init; } = string.Empty;
}

/// <summary>The result of provisioning: the new tenant id, its portal address and the administrator's username.</summary>
public sealed record ProvisionedClient(Guid Id, string PortalUrl, string AdministratorUsername);

/// <summary>
/// LIS-CR-007 A-01: a client with no activity for the window (SA-04 "Silent" filter, SA-03 tile). Last sign-in = the latest
/// browser session issued to one of its users (a sign-in or a renewal); last registration = its latest visit; last activity
/// = the later of the two (null: none since the client was created). <paramref name="SilentDays"/> counts whole days since
/// the last activity, or since the creation. <paramref name="AdministratorName"/> / <paramref name="AdministratorMobile"/> are
/// the client record's manager (the person provisioning made the laboratory administrator), the mobile in E.164.
/// <paramref name="Status"/> is Trial or Active (inactive clients are never silent).
/// </summary>
public sealed record SilentClientItem(
    Guid Id,
    string NameEn,
    string NameAr,
    string PortalUrl,
    string Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? LastSignInAtUtc,
    DateTimeOffset? LastRegistrationAtUtc,
    DateTimeOffset? LastActivityAtUtc,
    int SilentDays,
    string AdministratorName,
    string AdministratorMobile);

/// <summary>The silent clients for a window of <paramref name="Days"/> days as of <paramref name="AsOfUtc"/>, longest silence first; <paramref name="Trial"/> counts the trial clients among them.</summary>
public sealed record SilentClientList(int Days, DateTimeOffset AsOfUtc, int Trial, IReadOnlyList<SilentClientItem> Items);
