using System.Globalization;
using FluentValidation;
using Platform.Application.Abstractions.Messaging;
using Platform.Application.Abstractions.Security;
using Platform.Application.Abstractions.Settings;
using Platform.Contracts.Account;
using Platform.Domain.Authorization;
using Platform.Domain.Settings;
using Platform.SharedKernel.Abstractions;

namespace Platform.Application.Account.Queries;

/// <summary>
/// CP-05: the session branch's day at a glance (intake LIS-INT-ACC-001 §14). <paramref name="Date"/> is the local day
/// (<c>yyyy-MM-dd</c>, default today) and <paramref name="UtcOffsetMinutes"/> the browser's offset from UTC (default 0),
/// so "today" is the user's day. Figures whose source module is not built yet are <see langword="null"/>; the wallet
/// balance needs <c>wallet.view</c> as well.
/// </summary>
[RequirePermissionPolicy(PermissionCatalog.Dashboard, PermissionAction.View)]
public sealed record GetBranchDashboardQuery(string? Date, int? UtcOffsetMinutes) : IQuery<BranchDashboard>;

public sealed class GetBranchDashboardValidator : AbstractValidator<GetBranchDashboardQuery>
{
    public const int MaxOffsetMinutes = 14 * 60;

    public GetBranchDashboardValidator()
    {
        RuleFor(query => query.Date)
            .Must(date => DateOnly.TryParseExact(date, DashboardDays.Format, CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            .When(query => !string.IsNullOrEmpty(query.Date))
            .WithMessage("The date must be yyyy-MM-dd.");
        RuleFor(query => query.UtcOffsetMinutes).InclusiveBetween(-MaxOffsetMinutes, MaxOffsetMinutes).When(query => query.UtcOffsetMinutes is not null);
    }
}

public sealed class GetBranchDashboardHandler(
    IBranchDashboardReader reader,
    ITenantSettings settings,
    IUserPrivileges privileges,
    ICurrentUser user,
    ICurrentTenant tenant,
    IClock clock) : IQueryHandler<GetBranchDashboardQuery, BranchDashboard>
{
    public async Task<BranchDashboard> Handle(GetBranchDashboardQuery query, CancellationToken cancellationToken)
    {
        var branchId = BranchScope.Require(user);
        var offsetMinutes = query.UtcOffsetMinutes ?? 0;
        var offset = TimeSpan.FromMinutes(offsetMinutes);
        var day = string.IsNullOrEmpty(query.Date)
            ? DateOnly.FromDateTime(clock.UtcNow.ToOffset(offset).DateTime)
            : DateOnly.ParseExact(query.Date, DashboardDays.Format, CultureInfo.InvariantCulture);

        var window = DashboardDays.Window(day, offsetMinutes);
        var figures = await reader.ReadAsync(branchId, window, cancellationToken);

        (decimal Balance, string CurrencyCode)? wallet = null;
        if (tenant.TenantId is { } tenantId && privileges.Has(PermissionCatalog.Key(PermissionCatalog.Wallet, PermissionAction.View)))
        {
            wallet = await reader.ReadWalletAsync(tenantId, cancellationToken);
        }

        var currency = await settings.GetAsync(SettingKeys.DefaultCurrency, cancellationToken);
        var kpis = new BranchDashboardKpis(
            figures.Registrations,
            figures.CollectedToday,
            figures.SamplesReceived,
            figures.PendingResults,    // app.results (Reporting, intake LIS-INT-REP-001 §14); null until Results is migrated
            figures.UnverifiedBacklog,
            figures.DelayedTests,
            figures.RejectedSamples,
            wallet?.Balance,
            wallet?.CurrencyCode);

        return new BranchDashboard(
            branchId,
            day.ToString(DashboardDays.Format, CultureInfo.InvariantCulture),
            window.FromUtc,
            window.ToUtc,
            currency,
            kpis,
            DashboardDays.Chart(day, figures.RegistrationsByLocalDay),
            []);
    }
}

/// <summary>Local-day arithmetic of the dashboard: the day window in UTC and the seven bars of the chart, oldest first.</summary>
public static class DashboardDays
{
    public const string Format = "yyyy-MM-dd";
    public const int ChartDays = 7;

    public static DashboardWindow Window(DateOnly day, int utcOffsetMinutes)
    {
        var from = new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), TimeSpan.FromMinutes(utcOffsetMinutes)).ToUniversalTime();
        return new DashboardWindow(from, from.AddDays(1), from.AddDays(-(ChartDays - 1)), utcOffsetMinutes);
    }

    /// <summary>Seven bars ending on <paramref name="day"/>, zero where nothing was registered; empty while the source is not available.</summary>
    public static IReadOnlyList<DailyCount> Chart(DateOnly day, IReadOnlyDictionary<string, int>? countsByDay)
    {
        if (countsByDay is null)
        {
            return [];
        }

        return Enumerable.Range(0, ChartDays)
            .Select(index => day.AddDays(index - (ChartDays - 1)).ToString(Format, CultureInfo.InvariantCulture))
            .Select(date => new DailyCount(date, countsByDay.TryGetValue(date, out var count) ? count : 0))
            .ToList();
    }
}
