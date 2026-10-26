namespace Platform.Domain.PlatformAdmin;

/// <summary>
/// Refusal codes of platform administration (intake LIS-INT-PLT-001 §7–§8): <c>CLI</c> clients, <c>PLN</c> plans,
/// <c>WAL</c> wallets, <c>GEO</c> countries and governorates, <c>NTF</c> notification templates, <c>LIB</c> the
/// integration library, <c>SUB</c> the subscription wizard, <c>PLT</c> the platform portal itself.
/// 001–099 validation and state, 100–199 conflicts, 404/405… not found.
/// </summary>
public static class PlatformAdminCodes
{
    // Clients (SA-04)
    public const string PortalSlugInvalid = "CLI-001";
    public const string ClientDetailsInvalid = "CLI-002";
    public const string EmailInvalid = "CLI-003";
    public const string ClientAlreadyActive = "CLI-010";
    public const string ClientAlreadyInactive = "CLI-011";

    /// <summary>The client is inactive (or its trial has ended): its users cannot sign in (SA-04 rule). Raised by sign-in through <c>ITenantStatusLookup</c>.</summary>
    public const string ClientInactive = "CLI-020";
    public const string DeactivationReasonRequired = "CLI-021";
    public const string AdministratorMissing = "CLI-030";
    public const string PasswordTooShort = "CLI-040";
    public const string PasswordContainsUsername = "CLI-041";
    public const string UsernameInvalid = "CLI-042";
    public const string PortalSlugTaken = "CLI-100";
    public const string UsernameTaken = "CLI-101";
    public const string ClientNotFound = "CLI-404";

    // Plans (SA-05)
    public const string PlanCodeInvalid = "PLN-001";
    public const string PlanLimitsInvalid = "PLN-002";
    public const string PlanInactive = "PLN-010";
    public const string PlanCodeExists = "PLN-100";
    public const string PlanNotFound = "PLN-404";

    // Wallets (SA-05, CP-66)
    public const string AmountInvalid = "WAL-001";
    public const string ReferenceRequired = "WAL-002";
    public const string WalletNoteInvalid = "WAL-003";
    public const string InsufficientBalance = "WAL-010";
    public const string TopUpRequestNotPending = "WAL-011";
    public const string WalletNotFound = "WAL-404";
    public const string TopUpRequestNotFound = "WAL-405";

    // Countries and governorates (SA-10, SA-11)
    public const string IsoCodeInvalid = "GEO-001";
    public const string DialCodeInvalid = "GEO-002";
    public const string CurrencyInvalid = "GEO-003";
    public const string MobileLengthInvalid = "GEO-004";
    public const string LanguageInvalid = "GEO-005";
    public const string GeographyTextInvalid = "GEO-006";
    public const string TimeZoneInvalid = "GEO-007";
    public const string MobileInvalidForCountry = "GEO-010";
    public const string CountryInactive = "GEO-011";
    public const string GovernorateInactive = "GEO-012";
    public const string GovernorateNotInCountry = "GEO-013";

    /// <summary>The manager's mobile does not match the country (the laboratory's own mobile is <see cref="MobileInvalidForCountry"/>), so a form can mark the field (validation 2026-10-11, C-03).</summary>
    public const string ManagerMobileInvalidForCountry = "GEO-014";
    /// <summary>A city, district or area is created or reactivated only under an active parent (change request 2026-10-19, A2).</summary>
    public const string LocationParentInactive = "GEO-015";

    // LIS-CR-008 C-04 (P-ADR): the locality of a laboratory, a wizard request or a branch, and the locality import (SA-11).
    /// <summary>The picked city is unknown, or does not belong to the governorate (or no governorate is chosen).</summary>
    public const string LocalityCityNotInGovernorate = "GEO-020";

    /// <summary>The picked district is unknown, does not belong to the city, or is sent without its city.</summary>
    public const string LocalityDistrictNotInCity = "GEO-021";

    /// <summary>The picked area is unknown, does not belong to the district, or is sent without its district.</summary>
    public const string LocalityAreaNotInDistrict = "GEO-022";

    /// <summary>The locality file cannot be read: empty, no recognised header, a malformed line (SA-11 import).</summary>
    public const string LocalityImportFileInvalid = "GEO-023";

    /// <summary>The locality file exceeds the size or row limit.</summary>
    public const string LocalityImportTooLarge = "GEO-024";

    /// <summary>A row of the locality file is refused: no city, an area without its district, a name too long.</summary>
    public const string LocalityImportRowInvalid = "GEO-025";
    public const string IsoCodeExists = "GEO-100";
    public const string GovernorateExists = "GEO-101";
    public const string CityExists = "GEO-102";
    public const string DistrictExists = "GEO-103";
    public const string AreaExists = "GEO-104";
    public const string CountryInUse = "GEO-110";

    /// <summary>A city or district with active children is not deactivated; deactivate the children first.</summary>
    public const string LocationHasActiveChildren = "GEO-112";
    public const string CountryNotFound = "GEO-404";
    public const string GovernorateNotFound = "GEO-405";
    public const string CityNotFound = "GEO-406";
    public const string DistrictNotFound = "GEO-407";
    public const string AreaNotFound = "GEO-408";

    // Notification templates (SA-20)
    public const string TemplateCodeInvalid = "NTF-001";
    public const string TemplateTextInvalid = "NTF-002";
    public const string UnknownPlaceholder = "NTF-003";
    public const string SubjectRequired = "NTF-004";
    public const string TemplateCodeExists = "NTF-100";
    public const string TemplateNotFound = "NTF-404";

    // Integration library (SA-15…SA-18)
    public const string LibraryTextInvalid = "LIB-001";
    public const string ModesInvalid = "LIB-002";
    public const string JsonInvalid = "LIB-003";
    public const string CodeSystemItemInvalid = "LIB-004";
    public const string ImportFileInvalid = "LIB-010";
    public const string ImportTooLarge = "LIB-011";
    public const string AnalyserModelInactive = "LIB-012";
    public const string AnalyserModelExists = "LIB-100";
    public const string PcrKitExists = "LIB-101";
    public const string CodeSystemExists = "LIB-102";
    public const string RuleTemplateExists = "LIB-103";
    public const string AnalyserModelNotFound = "LIB-404";
    public const string PcrKitNotFound = "LIB-405";
    public const string CodeSystemNotFound = "LIB-406";
    public const string RuleTemplateNotFound = "LIB-407";

    // Subscription wizard (CP-02)
    public const string SubscriptionExpired = "SUB-001";
    public const string SubscriptionNotPending = "SUB-002";
    public const string SubscriptionNotVerified = "SUB-003";
    public const string TooManyCodes = "SUB-004";
    public const string CodeRejected = "SUB-010";

    /// <summary>
    /// LIS-CR-014 (S1): the manager's mobile already belongs to a platform user account (any spelling; it becomes the
    /// administrator's mobile, unique among users). Raised by the early check, by start and again by complete, on the field <c>managerMobile</c>.
    /// </summary>
    public const string MobileInUse = "SUB-011";

    // SUB-012 is reserved for a laboratory-name rule; no such rule exists (names are not unique), so nothing raises it.

    /// <summary>LIS-CR-014 (S2): the administrator username is taken (the availability check of step 3 and complete, field <c>username</c>).</summary>
    public const string UsernameInUse = "SUB-013";
    public const string SubscriptionNotFound = "SUB-404";

    // The Super Admin portal
    /// <summary>Platform administration is reachable only from the platform tenant (MODULES.md §1), whatever permissions a client role holds.</summary>
    public const string PlatformTenantRequired = "PLT-403";
    public const string OwnSessionTermination = "PLT-010";

    /// <summary>LIS-CR-005: no usable laboratory has this address (unknown, inactive, trial ended, the platform tenant or a malformed address): 404 of the public laboratory card and logo.</summary>
    public const string LaboratoryAddressNotFound = "PLT-404";
}
