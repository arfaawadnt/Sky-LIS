using Platform.Application.Abstractions.Persistence;
using Platform.Application.Audit;
using Platform.Contracts.Account;
using Platform.Contracts.Audit;
using Platform.Contracts.Common;
using Platform.Domain.Account;

namespace Platform.Application.Account;

// Ports of the Account & onboarding module (intake LIS-INT-ACC-001). Repositories and adapters live in
// Platform.Infrastructure/Persistence/Account and register by convention.

/// <summary>Filters of the ticket registers; every value is optional. <paramref name="TenantId"/> narrows the desk to one laboratory.</summary>
public sealed record SupportTicketFilter(SupportTicketStatus? Status, SupportTicketPriority? Priority, string? Search, Guid? OpenedByUserId, Guid? TenantId);

/// <summary>
/// Write side and projections of <see cref="SupportTicket"/> (<c>saas.support_tickets</c>). Every read, including
/// <see cref="IRepository{TAggregate}.GetForUpdateAsync"/>, is scoped to the current tenant's tickets, or to every
/// laboratory's when the current tenant is the platform tenant (INV-SUP-004); the table's RLS policy enforces the same.
/// </summary>
public interface ISupportTicketRepository : IRepository<SupportTicket>
{
    Task<Paged<SupportTicketListItem>> ListAsync(SupportTicketFilter filter, PageRequest page, CancellationToken cancellationToken);

    Task<SupportTicketDetail?> GetDetailAsync(Guid id, CancellationToken cancellationToken);
}

/// <summary>The signed-in user's own account as stored: the platform user and the membership in the current tenant.</summary>
public sealed record OwnAccount(
    Guid UserId,
    string Username,
    string DisplayName,
    string Email,
    string? Mobile,
    bool MustChangePassword,
    DateTimeOffset PasswordChangedAtUtc,
    DateTimeOffset? PasswordExpiresAtUtc,
    string? FullNameEn,
    string? FullNameAr,
    string? EmployeeCode,
    string? Designation,
    string? PreferredLanguage,
    string? RoleNameEn,
    string? RoleNameAr);

/// <summary>Reads of the identity and tenancy tables for self-service screens (CP-65, CP-68 filters). Bounded to the actor and the current tenant.</summary>
public interface IAccountDirectory
{
    Task<OwnAccount?> GetOwnAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>The members of the current tenant (active or not), for the audit log's user filter.</summary>
    Task<IReadOnlyList<AuditActorItem>> ListMembersAsync(CancellationToken cancellationToken);
}

/// <summary>
/// The platform's client record of a laboratory (SA-04 data shown on CP-65) and its display name for a new ticket.
/// Answers <see langword="null"/> when the record is not available (tenant seeded without a client record, or the
/// platform tables not migrated yet).
/// </summary>
public interface ILaboratoryProfileLookup
{
    Task<LaboratoryProfile?> FindAsync(Guid tenantId, CancellationToken cancellationToken);
}

/// <summary>
/// The day figures of one branch the dashboard computes from the operational tables (CP-05); every figure is
/// <see langword="null"/> while its source does not exist. The three result figures (added by the Reporting module,
/// intake LIS-INT-REP-001 §14) come from <c>app.results</c>: <paramref name="PendingResults"/> = results of the visits
/// registered in the day not yet verified, <paramref name="UnverifiedBacklog"/> = results with a value awaiting
/// verification (any day), <paramref name="DelayedTests"/> = open results whose consumed turnaround share reached the
/// tenant's <c>delayLatePercent</c> (BR-13). <paramref name="DelayedList"/> lists those delayed tests (the same predicate),
/// most delayed first and at most <see cref="BranchDashboardLimits.DelayedRows"/> rows (UAT R1-02).
/// </summary>
public sealed record BranchDayFigures(
    int? Registrations,
    decimal? CollectedToday,
    int? SamplesReceived,
    int? RejectedSamples,
    IReadOnlyDictionary<string, int>? RegistrationsByLocalDay,
    int? PendingResults = null,
    int? UnverifiedBacklog = null,
    int? DelayedTests = null,
    IReadOnlyList<DelayedTestItem>? DelayedList = null);

/// <summary>Bounds of the dashboard's lists.</summary>
public static class BranchDashboardLimits
{
    /// <summary>Rows of the "Delayed tests" table; the tile carries the full count.</summary>
    public const int DelayedRows = 20;
}

/// <summary>A window of the dashboard: the local day <c>[FromUtc, ToUtc)</c> and the 7-day chart window <c>[ChartFromUtc, ToUtc)</c>, with the offset that turns UTC into local days.</summary>
public sealed record DashboardWindow(DateTimeOffset FromUtc, DateTimeOffset ToUtc, DateTimeOffset ChartFromUtc, int UtcOffsetMinutes);

/// <summary>
/// Raw-SQL reads over the wave-2/3 tables of MODULES.md §5 (<c>app.visits</c>, <c>app.samples</c>, <c>app.payments</c>,
/// <c>app.refunds</c>, <c>app.results</c>) and the platform wallet (<c>saas.wallets</c>). A missing table (PostgreSQL 42P01) or column
/// (42703) yields <see langword="null"/>, never an error, so the dashboard works before those modules are migrated.
/// </summary>
public interface IBranchDashboardReader
{
    Task<BranchDayFigures> ReadAsync(Guid branchId, DashboardWindow window, CancellationToken cancellationToken);

    Task<(decimal Balance, string CurrencyCode)?> ReadWalletAsync(Guid tenantId, CancellationToken cancellationToken);
}

/// <summary>The audit log of the current tenant for a CSV export: the newest <paramref name="maxRows"/> entries of the filter, in one pass.</summary>
public interface IAuditExportReader
{
    Task<IReadOnlyList<AuditEntryItem>> ReadAsync(AuditEntryFilter filter, int maxRows, CancellationToken cancellationToken);
}

/// <summary>Security events of this module (02 §7 ledger); details are categories and counts, never passwords.</summary>
public static class AccountSecurityEvents
{
    public const string PasswordChanged = "PASSWORD_CHANGED";
    public const string PasswordChangeFailed = "PASSWORD_CHANGE_FAILED";
    public const string AuditExported = "AUDIT_EXPORTED";
}
