using FluentValidation;
using Platform.Application.Abstractions.Messaging;
using Platform.Application.Abstractions.Security;
using Platform.Application.Abstractions.Settings;
using Platform.Application.Identity;
using Platform.Contracts.PlatformAdmin;
using Platform.Domain.Authorization;
using Platform.Domain.PlatformAdmin;
using Platform.SharedKernel.Abstractions;
using Platform.SharedKernel.MultiTenancy;

namespace Platform.Application.PlatformAdmin.Queries;

// SA-03 dashboard and SA-08 sessions of the Super Admin portal; CP-02 public lookups.

/// <summary>Live sessions of the platform tenant's users (SA-08), newest first; <paramref name="Search"/> matches name or username.</summary>
[RequirePermissionPolicy(PermissionCatalog.Platform, PermissionAction.View)]
public sealed record ListAdminSessionsQuery(string? Search) : IQuery<IReadOnlyList<AdminSessionItem>>, IPlatformAdministrationRequest;

public sealed class ListAdminSessionsValidator : AbstractValidator<ListAdminSessionsQuery>
{
    public ListAdminSessionsValidator() => RuleFor(query => query.Search).MaximumLength(100);
}

public sealed class ListAdminSessionsHandler(IPlatformSessionDirectory sessions, IRefreshSessionRepository sessionRows, ICurrentTenant currentTenant, ICurrentUser currentUser, IClock clock)
    : IQueryHandler<ListAdminSessionsQuery, IReadOnlyList<AdminSessionItem>>
{
    /// <summary>A session not seen for this long reads as Idle.</summary>
    public static readonly TimeSpan IdleAfter = TimeSpan.FromMinutes(15);

    public async Task<IReadOnlyList<AdminSessionItem>> Handle(ListAdminSessionsQuery query, CancellationToken cancellationToken)
    {
        PlatformAdminScope.Require(currentTenant);
        var now = clock.UtcNow;
        var rows = await sessions.ListLiveAsync(TenantIds.Platform, now, cancellationToken);
        Guid? ownFamily = null;
        if (currentUser.SessionId is { } own)
        {
            ownFamily = rows.FirstOrDefault(row => row.SessionId == own)?.FamilyId
                        ?? (await sessionRows.GetForUpdateAsync(own, cancellationToken))?.FamilyId;
        }

        var search = query.Search?.Trim();
        return rows
            .Where(row => string.IsNullOrEmpty(search)
                          || row.DisplayName.Contains(search, StringComparison.OrdinalIgnoreCase)
                          || row.Username.Contains(search, StringComparison.OrdinalIgnoreCase))
            .Select(row => new AdminSessionItem(
                row.SessionId,
                row.UserId,
                row.DisplayName,
                row.Username,
                row.IpAddress,
                row.SinceUtc,
                row.LastSeenAtUtc,
                row.ExpiresAtUtc,
                now - row.LastSeenAtUtc > IdleAfter ? "Idle" : "Active",
                row.FamilyId == ownFamily))
            .ToList();
    }
}

/// <summary>
/// SA-03 for a period: today, week, month or year (UTC calendar boundaries; default month). LIS-CR-007 A-01:
/// <paramref name="SilentDays"/> is the window of the silent-clients tile (1…90, default 3).
/// </summary>
[RequirePermissionPolicy(PermissionCatalog.Platform, PermissionAction.View)]
public sealed record GetPlatformDashboardQuery(string? Period, int? SilentDays = null) : IQuery<PlatformDashboard>, IPlatformAdministrationRequest;

public sealed class GetPlatformDashboardValidator : AbstractValidator<GetPlatformDashboardQuery>
{
    public GetPlatformDashboardValidator()
    {
        RuleFor(query => query.Period).Must(period => DashboardPeriods.IsKnown(period)).WithMessage("The period is today, week, month or year.");
        RuleFor(query => query.SilentDays).SilentWindow();
    }
}

public sealed class GetPlatformDashboardHandler(
    IPlatformDashboardReader reader,
    IClientActivityReader activity,
    IClientLastActivityReader lastActivity,
    ISubscriptionStateReader subscriptions,
    ITenantSettings settings,
    ICurrentTenantSetter tenantScope,
    IPortalAddressOptions portal,
    ICurrentTenant currentTenant,
    IClock clock) : IQueryHandler<GetPlatformDashboardQuery, PlatformDashboard>
{
    public const int RecentClients = 5;
    public const int TopClients = 10;

    public async Task<PlatformDashboard> Handle(GetPlatformDashboardQuery query, CancellationToken cancellationToken)
    {
        PlatformAdminScope.Require(currentTenant);
        var now = clock.UtcNow;
        var period = DashboardPeriods.Normalise(query.Period);
        var (from, to) = DashboardPeriods.Range(period, now);

        var clients = await reader.CountClientsAsync(now, cancellationToken);
        var adminUsers = await reader.CountAdminUsersAsync(cancellationToken);
        var atZero = await reader.CountWalletsAtOrBelowZeroAsync(cancellationToken);
        var plans = await reader.PlanDistributionAsync(cancellationToken);
        var recent = await reader.RecentClientsAsync(RecentClients, portal.PortalBaseUrl, cancellationToken);
        var snapshot = await activity.ReadAsync(from, to, cancellationToken);

        var top = snapshot.ByClient.OrderByDescending(item => item.Registrations).Take(TopClients).ToList();
        IReadOnlyDictionary<Guid, (string NameEn, string NameAr)> names = top.Count == 0 ? new Dictionary<Guid, (string NameEn, string NameAr)>() : await reader.ClientNamesAsync(top.Select(item => item.TenantId).ToList(), cancellationToken);
        var byClient = top
            .Select(item => names.TryGetValue(item.TenantId, out var name) ? new ClientActivity(item.TenantId, name.NameEn, name.NameAr, item.Registrations) : new ClientActivity(item.TenantId, item.TenantId.ToString(), item.TenantId.ToString(), item.Registrations))
            .ToList();

        var silentDays = query.SilentDays ?? ClientSilence.DefaultDays;
        var silent = await SilentClientFinder.FindAsync(lastActivity, portal.PortalBaseUrl, now, silentDays, cancellationToken);
        var silentTrial = silent.Count(item => item.Status == nameof(TenantStatus.Trial));

        // LIS-CR-012 (C-16): "Expiring soon" = the clients whose trial, wallet cover or grace ends within 7 days; with the grace and expired counts.
        var graceDays = await SubscriptionGrace.DaysAsync(settings, tenantScope, cancellationToken);
        var states = await SubscriptionClientFinder.StatesAsync(subscriptions, now, graceDays, cancellationToken);
        var expiring = states.Count(entry => SubscriptionFilters.Matches(SubscriptionFilters.Expiring, entry.State, SubscriptionFilters.DefaultDays));
        var inGrace = states.Count(entry => SubscriptionFilters.Matches(SubscriptionFilters.Grace, entry.State, SubscriptionFilters.DefaultDays));
        var expired = states.Count(entry => SubscriptionFilters.Matches(SubscriptionFilters.Expired, entry.State, SubscriptionFilters.DefaultDays));

        return new PlatformDashboard(period, from, to, clients, adminUsers, new ActivityTotals(snapshot.ServedPatients, snapshot.Tests, snapshot.Registrations), atZero, plans, byClient, recent, silent.Count, silentTrial, silentDays)
        {
            ExpiringSoonClients = expiring,
            GraceClients = inGrace,
            ExpiredClients = expired,
        };
    }
}

public static class DashboardPeriods
{
    public static readonly IReadOnlyList<string> All = ["today", "week", "month", "year"];

    public static bool IsKnown(string? period) => string.IsNullOrEmpty(period) || All.Contains(period.Trim().ToLowerInvariant(), StringComparer.Ordinal);

    public static string Normalise(string? period) => string.IsNullOrWhiteSpace(period) ? "month" : period.Trim().ToLowerInvariant();

    /// <summary>[from, to) in UTC: the current day, ISO week (Monday), month or year.</summary>
    public static (DateTimeOffset From, DateTimeOffset To) Range(string period, DateTimeOffset nowUtc)
    {
        var today = new DateTimeOffset(nowUtc.Year, nowUtc.Month, nowUtc.Day, 0, 0, 0, TimeSpan.Zero);
        return period switch
        {
            "today" => (today, today.AddDays(1)),
            "week" => (today.AddDays(-(((int)today.DayOfWeek + 6) % 7)), today.AddDays(-(((int)today.DayOfWeek + 6) % 7)).AddDays(7)),
            "year" => (new DateTimeOffset(nowUtc.Year, 1, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(nowUtc.Year + 1, 1, 1, 0, 0, 0, TimeSpan.Zero)),
            _ => (new DateTimeOffset(nowUtc.Year, nowUtc.Month, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(nowUtc.Year, nowUtc.Month, 1, 0, 0, 0, TimeSpan.Zero).AddMonths(1)),
        };
    }
}
