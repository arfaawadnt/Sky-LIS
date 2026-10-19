using FluentValidation;
using Platform.Application.Abstractions.Messaging;
using Platform.Application.Abstractions.Security;
using Platform.Contracts.PlatformAdmin;
using Platform.Domain.Authorization;
using Platform.Domain.PlatformAdmin;
using Platform.SharedKernel.Primitives;

namespace Platform.Application.PlatformAdmin.Queries;

// The location hierarchy (change request 2026-10-19, A2): the Locations drawer lists one level per parent
// [tenancy.view]; the public lookups beside /api/v1/public/governorates feed the patient form (active rows, id and
// names, filtered by parent).

// ── Admin lists per parent ───────────────────────────────────────────────────────────────────────

[RequirePermissionPolicy(PermissionCatalog.Tenancy, PermissionAction.View)]
public sealed record ListCitiesQuery(Guid GovernorateId, string? Search, bool? IsActive) : IQuery<IReadOnlyList<LocationListItem>>, IPlatformAdministrationRequest;

public sealed class ListCitiesValidator : AbstractValidator<ListCitiesQuery>
{
    public ListCitiesValidator()
    {
        RuleFor(query => query.GovernorateId).NotEmpty();
        RuleFor(query => query.Search).MaximumLength(100);
    }
}

public sealed class ListCitiesHandler(IGovernorateRepository governorates, ICityRepository cities, ICurrentTenant currentTenant) : IQueryHandler<ListCitiesQuery, IReadOnlyList<LocationListItem>>
{
    public async Task<IReadOnlyList<LocationListItem>> Handle(ListCitiesQuery query, CancellationToken cancellationToken)
    {
        PlatformAdminScope.Require(currentTenant);
        _ = await governorates.GetForUpdateAsync(query.GovernorateId, cancellationToken)
            ?? throw new DomainException(PlatformAdminCodes.GovernorateNotFound, "Governorate not found.", DomainFault.NotFound);
        return await cities.ListAsync(query.GovernorateId, SearchText.Of(query.Search), query.IsActive, cancellationToken);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Tenancy, PermissionAction.View)]
public sealed record ListDistrictsQuery(Guid CityId, string? Search, bool? IsActive) : IQuery<IReadOnlyList<LocationListItem>>, IPlatformAdministrationRequest;

public sealed class ListDistrictsValidator : AbstractValidator<ListDistrictsQuery>
{
    public ListDistrictsValidator()
    {
        RuleFor(query => query.CityId).NotEmpty();
        RuleFor(query => query.Search).MaximumLength(100);
    }
}

public sealed class ListDistrictsHandler(ICityRepository cities, IDistrictRepository districts, ICurrentTenant currentTenant) : IQueryHandler<ListDistrictsQuery, IReadOnlyList<LocationListItem>>
{
    public async Task<IReadOnlyList<LocationListItem>> Handle(ListDistrictsQuery query, CancellationToken cancellationToken)
    {
        PlatformAdminScope.Require(currentTenant);
        _ = await cities.GetForUpdateAsync(query.CityId, cancellationToken)
            ?? throw new DomainException(PlatformAdminCodes.CityNotFound, "City not found.", DomainFault.NotFound);
        return await districts.ListAsync(query.CityId, SearchText.Of(query.Search), query.IsActive, cancellationToken);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Tenancy, PermissionAction.View)]
public sealed record ListAreasQuery(Guid DistrictId, string? Search, bool? IsActive) : IQuery<IReadOnlyList<LocationListItem>>, IPlatformAdministrationRequest;

public sealed class ListAreasValidator : AbstractValidator<ListAreasQuery>
{
    public ListAreasValidator()
    {
        RuleFor(query => query.DistrictId).NotEmpty();
        RuleFor(query => query.Search).MaximumLength(100);
    }
}

public sealed class ListAreasHandler(IDistrictRepository districts, IAreaRepository areas, ICurrentTenant currentTenant) : IQueryHandler<ListAreasQuery, IReadOnlyList<LocationListItem>>
{
    public async Task<IReadOnlyList<LocationListItem>> Handle(ListAreasQuery query, CancellationToken cancellationToken)
    {
        PlatformAdminScope.Require(currentTenant);
        _ = await districts.GetForUpdateAsync(query.DistrictId, cancellationToken)
            ?? throw new DomainException(PlatformAdminCodes.DistrictNotFound, "District not found.", DomainFault.NotFound);
        return await areas.ListAsync(query.DistrictId, SearchText.Of(query.Search), query.IsActive, cancellationToken);
    }
}

// ── Public lookups (the patient form, anonymous like the governorates) ───────────────────────────

/// <summary>Active cities of one governorate, or of every governorate when <paramref name="GovernorateId"/> is null.</summary>
[AllowUnauthenticated]
public sealed record ListPublicCitiesQuery(Guid? GovernorateId) : IQuery<IReadOnlyList<PublicCity>>;

public sealed class ListPublicCitiesValidator : AbstractValidator<ListPublicCitiesQuery>
{
    public ListPublicCitiesValidator() => RuleFor(query => query.GovernorateId).NotEqual(Guid.Empty).When(query => query.GovernorateId.HasValue);
}

public sealed class ListPublicCitiesHandler(ICityRepository cities) : IQueryHandler<ListPublicCitiesQuery, IReadOnlyList<PublicCity>>
{
    public Task<IReadOnlyList<PublicCity>> Handle(ListPublicCitiesQuery query, CancellationToken cancellationToken) => cities.ListPublicAsync(query.GovernorateId, cancellationToken);
}

/// <summary>Active districts of one city, or of every city when <paramref name="CityId"/> is null.</summary>
[AllowUnauthenticated]
public sealed record ListPublicDistrictsQuery(Guid? CityId) : IQuery<IReadOnlyList<PublicDistrict>>;

public sealed class ListPublicDistrictsValidator : AbstractValidator<ListPublicDistrictsQuery>
{
    public ListPublicDistrictsValidator() => RuleFor(query => query.CityId).NotEqual(Guid.Empty).When(query => query.CityId.HasValue);
}

public sealed class ListPublicDistrictsHandler(IDistrictRepository districts) : IQueryHandler<ListPublicDistrictsQuery, IReadOnlyList<PublicDistrict>>
{
    public Task<IReadOnlyList<PublicDistrict>> Handle(ListPublicDistrictsQuery query, CancellationToken cancellationToken) => districts.ListPublicAsync(query.CityId, cancellationToken);
}

/// <summary>Active areas of one district, or of every district when <paramref name="DistrictId"/> is null.</summary>
[AllowUnauthenticated]
public sealed record ListPublicAreasQuery(Guid? DistrictId) : IQuery<IReadOnlyList<PublicArea>>;

public sealed class ListPublicAreasValidator : AbstractValidator<ListPublicAreasQuery>
{
    public ListPublicAreasValidator() => RuleFor(query => query.DistrictId).NotEqual(Guid.Empty).When(query => query.DistrictId.HasValue);
}

public sealed class ListPublicAreasHandler(IAreaRepository areas) : IQueryHandler<ListPublicAreasQuery, IReadOnlyList<PublicArea>>
{
    public Task<IReadOnlyList<PublicArea>> Handle(ListPublicAreasQuery query, CancellationToken cancellationToken) => areas.ListPublicAsync(query.DistrictId, cancellationToken);
}
