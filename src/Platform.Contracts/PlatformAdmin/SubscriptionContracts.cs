using Platform.Contracts.Common;

namespace Platform.Contracts.PlatformAdmin;

// The public subscription wizard (CP-02): anonymous, rate-limited, one hour from start to completion.
// Step 1 start → code to the manager's mobile; step 2 verify; step 3 complete (credentials) → portal address.
// LIS-CR-008 C-04: step 1 may name a city / district / area under the governorate (optional, chain-checked GEO-020…022).

public sealed record StartSubscriptionRequest(
    string LabNameEn,
    string LabNameAr,
    Guid CountryId,
    Guid GovernorateId,
    string Address,
    string Mobile,
    string? LicenceNo,
    string ManagerName,
    string ManagerMobile,
    string? Email,
    string? Language,
    Guid? CityId = null,
    Guid? DistrictId = null,
    Guid? AreaId = null);

/// <summary>The request to continue with; the code went to <paramref name="MaskedMobile"/> and lives <paramref name="CodeLifetimeSeconds"/>.</summary>
/// <summary><see cref="VerificationCode"/> is present only on a host that reveals codes (<c>Lis:Notifications:RevealCodes</c>, no SMS gateway yet): the wizard shows it.</summary>
public sealed record SubscriptionStarted(Guid SubscriptionId, DateTimeOffset ExpiresAtUtc, string MaskedMobile, int CodeLifetimeSeconds, string? VerificationCode = null);

public sealed record VerifySubscriptionRequest(Guid SubscriptionId, string Code);

public sealed record ResendSubscriptionCodeRequest(Guid SubscriptionId);

/// <summary>Step 3: the administrator's username and password (credential envelope, D-03). The confirmation is checked in the browser.</summary>
public sealed record CompleteSubscriptionRequest
{
    public Guid SubscriptionId { get; init; }

    public string Username { get; init; } = string.Empty;

    [CredentialField]
    public string Password { get; init; } = string.Empty;
}

public sealed record SubscriptionCompletedResponse(Guid TenantId, string PortalUrl, string Username);

/// <summary>
/// LIS-CR-014 (S1): the step-1 fields checked before the code is sent (<c>POST check</c>); every member is optional so the
/// wizard can check a field as soon as it is typed. With <paramref name="CountryId"/> the mobiles are first checked against the
/// country's rule (GEO-010 / GEO-014); the manager's mobile is then checked against the platform's accounts (SUB-011).
/// </summary>
public sealed record CheckSubscriptionRequest(Guid? CountryId, string? Mobile, string? ManagerMobile);

/// <summary>One refused field of a check: the camelCase request field (<c>mobile</c>, <c>managerMobile</c>, <c>username</c>) and its code.</summary>
public sealed record SubscriptionFieldIssue(string Field, string Code);

/// <summary>The answer of a check: empty <paramref name="Issues"/> when every checked field is acceptable.</summary>
public sealed record SubscriptionCheckResult(IReadOnlyList<SubscriptionFieldIssue> Issues);

/// <summary><paramref name="Valid"/>: the username has an acceptable shape; <paramref name="Available"/>: no account uses it.</summary>
public sealed record UsernameAvailability(string Username, bool Valid, bool Available);

public sealed record PublicCountry(Guid Id, string NameEn, string NameAr, string IsoCode, string DialCode, int MobileLength, string? Flag, string CurrencyCode, string Language);

public sealed record PublicGovernorate(Guid Id, Guid CountryId, string NameEn, string NameAr, string? District);
