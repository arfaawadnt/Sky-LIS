using Platform.Application.Abstractions.Persistence;
using Platform.Contracts.Common;

namespace Platform.Application.PlatformAdmin;

// Client login logs (SA-08b, change request LIS-CR-003 §2): the browser sessions of the client users, one row per
// rotation family, read across tenants from identity.refresh_sessions (no RLS) by the platform tenant only (PLT-403).

/// <summary>
/// What the reader groups and filters on the server. <paramref name="TenantId"/> null = every client (the platform
/// tenant's own sessions excluded); a value = that tenant only, the platform included when asked. <paramref name="Search"/>
/// matches the username and the display name. The windows are inclusive UTC bounds on the login and on the logout
/// (a null logout never matches a logout window). Minutes bound the duration. <paramref name="Status"/> is one of
/// <see cref="Contracts.PlatformAdmin.ClientLoginLogValues.Statuses"/>. The device is not here: it comes from the
/// user-agent string, parsed by the handler (<see cref="UserAgentParser"/>), so a device filter is applied after the read.
/// </summary>
public sealed record ClientLoginLogFilter(
    Guid? TenantId,
    string? Search,
    DateTimeOffset? LoginFromUtc,
    DateTimeOffset? LoginToUtc,
    DateTimeOffset? LogoutFromUtc,
    DateTimeOffset? LogoutToUtc,
    int? MinMinutes,
    int? MaxMinutes,
    string? Status);

/// <summary>
/// One session family as the reader computes it: login = the first issue, last seen = the latest activity of any member,
/// ended = the sign-out, else the first other revocation, else the sliding expiry when it is past (null while active);
/// duration = (ended ?? last seen) − login in whole minutes. The IP address and user agent are the first member's (the
/// sign-in). Client names are null for a tenant without a client record (seeded by the development tools).
/// </summary>
public sealed record ClientLoginLogRow(
    Guid FamilyId,
    Guid TenantId,
    string? ClientNameEn,
    string? ClientNameAr,
    Guid UserId,
    string Username,
    string DisplayName,
    DateTimeOffset LoginAtUtc,
    DateTimeOffset? EndedAtUtc,
    DateTimeOffset LastSeenAtUtc,
    string? IpAddress,
    string? UserAgent,
    int DurationMinutes,
    string Status,
    string? RevocationReason);

/// <summary>The grouped read of the client sessions; the order is applied before paging (<see cref="PlatformAdminSortKeys.ClientLoginLogs"/>, default newest login first).</summary>
public interface IClientLoginLogReader
{
    Task<Paged<ClientLoginLogRow>> ListAsync(ClientLoginLogFilter filter, ListSort sort, PageRequest page, DateTimeOffset nowUtc, CancellationToken cancellationToken);

    /// <summary>Every family of the filter in the requested order, at most <paramref name="maxRows"/> + 1 (so the caller can tell a cut).</summary>
    Task<IReadOnlyList<ClientLoginLogRow>> ListAllAsync(ClientLoginLogFilter filter, ListSort sort, int maxRows, DateTimeOffset nowUtc, CancellationToken cancellationToken);
}
