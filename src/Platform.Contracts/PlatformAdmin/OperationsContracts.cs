namespace Platform.Contracts.PlatformAdmin;

// Dashboard (SA-03) and sessions (SA-08) of the Super Admin portal.

/// <summary>Statuses: Active (seen within the last 15 minutes), Idle.</summary>
public sealed record AdminSessionItem(
    Guid SessionId,
    Guid UserId,
    string DisplayName,
    string Username,
    string? IpAddress,
    DateTimeOffset SinceUtc,
    DateTimeOffset LastSeenAtUtc,
    DateTimeOffset ExpiresAtUtc,
    string Status,
    bool IsCurrent);

public sealed record ClientCounts(int Total, int Active, int Inactive, int Trial, int BySubscription, int ByManual);

/// <summary>Per-period activity; zero until the registration module fills <c>IClientActivityReader</c>.</summary>
public sealed record ActivityTotals(int ServedPatients, int Tests, int Registrations);

public sealed record PlanShare(Guid PlanId, string Code, string NameEn, string NameAr, int Clients);

public sealed record ClientActivity(Guid TenantId, string NameEn, string NameAr, int Registrations);

public sealed record RecentClient(
    Guid Id,
    string NameEn,
    string NameAr,
    string PortalUrl,
    string CountryNameEn,
    string CountryNameAr,
    string PlanCode,
    decimal WalletBalance,
    string CurrencyCode,
    DateTimeOffset CreatedAtUtc,
    string Source,
    string Status);

/// <summary>
/// Periods: today, week, month, year (UTC calendar boundaries). LIS-CR-007 A-01: <paramref name="SilentClients"/> counts
/// the Trial and Active clients with no sign-in and no registration for <paramref name="SilentDays"/> days (default 3),
/// <paramref name="SilentTrialClients"/> the trial clients among them; they do not depend on the period.
/// </summary>
public sealed record PlatformDashboard(
    string Period,
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc,
    ClientCounts Clients,
    int AdminUsers,
    ActivityTotals Activity,
    int WalletsAtOrBelowZero,
    IReadOnlyList<PlanShare> PlanDistribution,
    IReadOnlyList<ClientActivity> RegistrationsByClient,
    IReadOnlyList<RecentClient> RecentClients,
    int SilentClients = 0,
    int SilentTrialClients = 0,
    int SilentDays = 3);
