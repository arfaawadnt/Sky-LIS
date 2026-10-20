using FluentValidation;
using Platform.Application.Abstractions.Messaging;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Abstractions.Security;
using Platform.Contracts.Common;
using Platform.Contracts.PlatformAdmin;
using Platform.Domain.Authorization;
using Platform.Domain.PlatformAdmin;
using Platform.SharedKernel.Primitives;

namespace Platform.Application.PlatformAdmin.Queries;

// SA-04 registers. Branch and user counts come from IClientUsageCounts (cross-tenant counts only, no elevation).

/// <summary><paramref name="Source"/>: Subscription|Manual; <paramref name="Status"/>: Trial|Active|Inactive; <paramref name="Search"/> matches name, portal slug, manager and mobiles.</summary>
[RequirePermissionPolicy(PermissionCatalog.Tenancy, PermissionAction.View)]
public sealed record ListClientsQuery(string? Search, string? Source, Guid? PlanId, string? Status, int? Page, int? PageSize, string? Sort = null, string? Direction = null) : IQuery<Paged<ClientListItem>>, IPlatformAdministrationRequest;

public sealed class ListClientsValidator : AbstractValidator<ListClientsQuery>
{
    public ListClientsValidator()
    {
        RuleFor(query => query.Search).MaximumLength(100);
        RuleFor(query => query.Source).IsEnumName(typeof(TenantSource), caseSensitive: false).When(query => !string.IsNullOrEmpty(query.Source));
        RuleFor(query => query.Status).IsEnumName(typeof(TenantStatus), caseSensitive: false).When(query => !string.IsNullOrEmpty(query.Status));
        RuleFor(query => query.Page).GreaterThan(0).When(query => query.Page is not null);
        RuleFor(query => query.PageSize).InclusiveBetween(1, PageRequest.MaxPageSize).When(query => query.PageSize is not null);
        RuleFor(query => query.Sort).SortKeyOf(PlatformAdminSortKeys.Clients);
        RuleFor(query => query.Direction).SortDirection();
    }
}

public sealed class ListClientsHandler(ITenantRepository tenants, IClientUsageCounts usage, IPortalAddressOptions portal, ICurrentTenant currentTenant) : IQueryHandler<ListClientsQuery, Paged<ClientListItem>>
{
    public async Task<Paged<ClientListItem>> Handle(ListClientsQuery query, CancellationToken cancellationToken)
    {
        PlatformAdminScope.Require(currentTenant);
        var filter = new ClientFilter(
            string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim(),
            string.IsNullOrEmpty(query.Source) ? null : Enum.Parse<TenantSource>(query.Source, ignoreCase: true),
            query.PlanId,
            string.IsNullOrEmpty(query.Status) ? null : Enum.Parse<TenantStatus>(query.Status, ignoreCase: true),
            ListSort.Of(query.Sort, query.Direction));
        var page = await tenants.ListAsync(filter, PageRequest.Normalized(query.Page, query.PageSize), portal.PortalBaseUrl, cancellationToken);
        if (page.Items.Count == 0)
        {
            return page;
        }

        var counts = await usage.GetAsync(page.Items.Select(item => item.Id).ToList(), cancellationToken);
        var items = page.Items.Select(item => counts.TryGetValue(item.Id, out var count) ? item with { Branches = count.Branches, Users = count.Users } : item).ToList();
        return page with { Items = items };
    }
}

[RequirePermissionPolicy(PermissionCatalog.Tenancy, PermissionAction.View)]
public sealed record GetClientQuery(Guid ClientId) : IQuery<ClientDetail>, IPlatformAdministrationRequest;

public sealed class GetClientHandler(ITenantRepository tenants, IClientUsageCounts usage, IPortalAddressOptions portal, ICurrentTenant currentTenant) : IQueryHandler<GetClientQuery, ClientDetail>
{
    public async Task<ClientDetail> Handle(GetClientQuery query, CancellationToken cancellationToken)
    {
        PlatformAdminScope.Require(currentTenant);
        var detail = await tenants.GetDetailAsync(query.ClientId, portal.PortalBaseUrl, cancellationToken)
            ?? throw new DomainException(PlatformAdminCodes.ClientNotFound, "Client not found.", DomainFault.NotFound);
        var counts = await usage.GetAsync([detail.Id], cancellationToken);
        return counts.TryGetValue(detail.Id, out var count) ? detail with { Branches = count.Branches, Users = count.Users } : detail;
    }
}
