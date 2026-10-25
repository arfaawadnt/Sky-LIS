using FluentValidation;
using Platform.Application.Abstractions.Messaging;
using Platform.Application.Abstractions.Security;
using Platform.Application.Abstractions.Settings;
using Platform.Contracts.PlatformAdmin;
using Platform.Domain.Authorization;
using Platform.SharedKernel.Abstractions;

namespace Platform.Application.PlatformAdmin.Queries;

// LIS-CR-012 (C-16): SA-04 "Subscription" filter — the clients whose trial, wallet cover or grace ends soon, the ones in grace
// and the expired ones — for the platform tenant only (PLT-403). The list is small (one row per client of the filter) and never paged.

/// <summary>
/// <paramref name="State"/>: <c>expiring</c> (default; Warning or Grace ending within <paramref name="Days"/> days), <c>grace</c>,
/// <c>expired</c>. <paramref name="Days"/>: 1…60, default 7 (used by <c>expiring</c> only).
/// </summary>
[RequirePermissionPolicy(PermissionCatalog.Platform, PermissionAction.View)]
public sealed record ListSubscriptionClientsQuery(string? State, int? Days) : IQuery<SubscriptionClientList>, IPlatformAdministrationRequest;

public sealed class ListSubscriptionClientsValidator : AbstractValidator<ListSubscriptionClientsQuery>
{
    public ListSubscriptionClientsValidator()
    {
        RuleFor(query => query.State).Must(SubscriptionFilters.IsKnown).WithMessage("'state' must be expiring, grace or expired.");
        RuleFor(query => query.Days)
            .Must(days => days is null or >= SubscriptionFilters.MinDays and <= SubscriptionFilters.MaxDays)
            .WithMessage($"'days' must be between {SubscriptionFilters.MinDays} and {SubscriptionFilters.MaxDays}.");
    }
}

public sealed class ListSubscriptionClientsHandler(
    ISubscriptionStateReader reader,
    ITenantSettings settings,
    ICurrentTenantSetter tenantScope,
    IPortalAddressOptions portal,
    ICurrentTenant currentTenant,
    IClock clock) : IQueryHandler<ListSubscriptionClientsQuery, SubscriptionClientList>
{
    public async Task<SubscriptionClientList> Handle(ListSubscriptionClientsQuery query, CancellationToken cancellationToken)
    {
        PlatformAdminScope.Require(currentTenant);
        var now = clock.UtcNow;
        var state = SubscriptionFilters.Normalise(query.State);
        var days = query.Days ?? SubscriptionFilters.DefaultDays;
        var graceDays = await SubscriptionGrace.DaysAsync(settings, tenantScope, cancellationToken);
        var states = await SubscriptionClientFinder.StatesAsync(reader, now, graceDays, cancellationToken);
        return new SubscriptionClientList(state, days, graceDays, now, SubscriptionClientFinder.Filter(states, state, days, portal.PortalBaseUrl));
    }
}
