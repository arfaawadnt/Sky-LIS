using Platform.Contracts.Common;

namespace Platform.Contracts.Account;

// Request and response shapes of the Account & onboarding module (intake LIS-INT-ACC-001). Validation here is shape
// only; the rules live in the aggregates and handlers.

/// <summary>A1 (CP-65 / SA-21): the language the user works in, <c>en</c> or <c>ar</c>, stored on the membership of the session's tenant.</summary>
public sealed record UpdatePreferencesRequest(string Language);

/// <summary>A2 (CP-65 / SA-21): both passwords travel in the credential envelope (D-03).</summary>
public sealed record ChangePasswordRequest
{
    [CredentialField]
    public string CurrentPassword { get; init; } = string.Empty;

    [CredentialField]
    public string NewPassword { get; init; } = string.Empty;
}

/// <summary>The password rule the profile page shows (02 §6.1 as decided on 2026-10-09): length only, never the username.</summary>
public sealed record PasswordPolicyView(int MinLength, int MaxLength);

/// <summary>
/// The laboratory record behind the session's tenant (CP-65), read from the platform's client record. Every field may be
/// <see langword="null"/> when the client record is not available (a tenant seeded by the development tools).
/// </summary>
public sealed record LaboratoryProfile(
    Guid TenantId,
    string? NameEn,
    string? NameAr,
    string? PortalUrl,
    string? CountryNameEn,
    string? CountryNameAr,
    string? PlanCode,
    string? PlanNameEn,
    string? PlanNameAr,
    string? ManagerName,
    string? ManagerMobile,
    string? Mobile,
    string? Email,
    string? Status,
    DateTimeOffset? TrialEndsAtUtc);

/// <summary>CP-65 / SA-21: the signed-in user's own account. <see cref="Laboratory"/> is <see langword="null"/> for the platform tenant.</summary>
public sealed record MyAccountProfile(
    Guid UserId,
    string Username,
    string DisplayName,
    string Email,
    string? Mobile,
    string Language,
    Guid? TenantId,
    bool IsPlatform,
    string? FullNameEn,
    string? FullNameAr,
    string? EmployeeCode,
    string? Designation,
    string? RoleNameEn,
    string? RoleNameAr,
    bool MustChangePassword,
    DateTimeOffset PasswordChangedAtUtc,
    DateTimeOffset? PasswordExpiresAtUtc,
    PasswordPolicyView PasswordPolicy,
    LaboratoryProfile? Laboratory);

/// <summary>A member of the tenant who may appear as the actor of audit entries (CP-68 "user" filter).</summary>
public sealed record AuditActorItem(Guid UserId, string DisplayName, string Username, bool IsActive);

// ---- Support tickets (CP-67, platform desk) ----

/// <summary>S1: priorities Low, Normal, High.</summary>
public sealed record OpenSupportTicketRequest(string Subject, string Description, string Priority);

public sealed record SupportTicketReplyRequest(string Text);

/// <summary>S5: the desk's optional resolution note, kept as a reply.</summary>
public sealed record ResolveSupportTicketRequest(string? Note);

/// <summary>Statuses: Open, InProgress, Resolved, Closed. Priorities: Low, Normal, High.</summary>
public sealed record SupportTicketListItem(
    Guid Id,
    string Number,
    Guid TenantId,
    string? LaboratoryNameEn,
    string? LaboratoryNameAr,
    string Subject,
    string Priority,
    string Status,
    Guid OpenedByUserId,
    string OpenedByName,
    DateTimeOffset OpenedAtUtc,
    DateTimeOffset LastActivityAtUtc,
    int ReplyCount,
    bool LastReplyFromPlatform);

public sealed record SupportTicketReplyView(Guid Id, Guid AuthorUserId, string AuthorName, string Text, bool FromPlatform, DateTimeOffset AtUtc);

public sealed record SupportTicketDetail(
    Guid Id,
    string Number,
    Guid TenantId,
    string? LaboratoryNameEn,
    string? LaboratoryNameAr,
    string Subject,
    string Description,
    string Priority,
    string Status,
    Guid OpenedByUserId,
    string OpenedByName,
    DateTimeOffset OpenedAtUtc,
    DateTimeOffset LastActivityAtUtc,
    DateTimeOffset? ResolvedAtUtc,
    DateTimeOffset? ClosedAtUtc,
    IReadOnlyList<SupportTicketReplyView> Replies);

// ---- Branch dashboard (CP-05) ----

/// <summary>
/// The figures of the session branch for one day. A <see langword="null"/> figure means "not available yet" (the source
/// table does not exist, or the module that owns it is not built), never zero. Stable contract: later modules add
/// values, they never rename or remove one (intake LIS-INT-ACC-001 §14).
/// </summary>
public sealed record BranchDashboardKpis(
    int? Registrations,
    decimal? CollectedToday,
    int? SamplesReceived,
    int? PendingResults,
    int? UnverifiedBacklog,
    int? DelayedTests,
    int? RejectedSamples,
    decimal? WalletBalance,
    string? WalletCurrency);

/// <summary>One bar of the 7-day registrations chart; <see cref="Date"/> is the local day <c>yyyy-MM-dd</c>.</summary>
public sealed record DailyCount(string Date, int Count);

/// <summary>A test past its expected result time (BR-13); filled by the Results module (wave 3).</summary>
public sealed record DelayedTestItem(Guid VisitId, string Accession, string? PatientName, string TestName, DateTimeOffset ExpectedAtUtc, int DelayMinutes, string Status);

public sealed record BranchDashboard(
    Guid BranchId,
    string Date,
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc,
    string CurrencyCode,
    BranchDashboardKpis Kpis,
    IReadOnlyList<DailyCount> RegistrationsByDay,
    IReadOnlyList<DelayedTestItem> DelayedTests);
