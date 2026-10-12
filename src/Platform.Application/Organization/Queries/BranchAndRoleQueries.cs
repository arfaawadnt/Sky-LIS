using FluentValidation;
using Platform.Application.Abstractions.Messaging;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Abstractions.Security;
using Platform.Contracts.Common;
using Platform.Contracts.Organization;
using Platform.Domain.Authorization;
using Platform.Domain.Organization;
using Platform.SharedKernel.MultiTenancy;
using Platform.SharedKernel.Primitives;

namespace Platform.Application.Organization.Queries;

// Registers of branches and roles (intake LIS-INT-ORG-001 §14). Queries carry a policy like commands (D-04) and read projections only.

[RequirePermissionPolicy(PermissionCatalog.Organization, PermissionAction.View)]
public sealed record ListBranchesQuery(string? Search, bool? IsActive, int? Page, int? PageSize, string? Sort = null, string? Direction = null) : IQuery<Paged<BranchListItem>>;

public sealed class ListBranchesValidator : AbstractValidator<ListBranchesQuery>
{
    public ListBranchesValidator()
    {
        RuleFor(query => query.Search).MaximumLength(100);
        RuleFor(query => query.Page).GreaterThan(0).When(query => query.Page is not null);
        RuleFor(query => query.PageSize).InclusiveBetween(1, PageRequest.MaxPageSize).When(query => query.PageSize is not null);
        RuleFor(query => query.Sort).SortKeyOf(OrganizationSortKeys.Branches);
        RuleFor(query => query.Direction).SortDirection();
    }
}

public sealed class ListBranchesHandler(IBranchRepository branches) : IQueryHandler<ListBranchesQuery, Paged<BranchListItem>>
{
    public Task<Paged<BranchListItem>> Handle(ListBranchesQuery query, CancellationToken cancellationToken) =>
        branches.ListAsync(new BranchFilter(query.Search, query.IsActive, ListSort.Of(query.Sort, query.Direction)), PageRequest.Normalized(query.Page, query.PageSize), cancellationToken);
}

[RequirePermissionPolicy(PermissionCatalog.Organization, PermissionAction.View)]
public sealed record GetBranchQuery(Guid BranchId) : IQuery<BranchListItem>;

public sealed class GetBranchHandler(IBranchRepository branches) : IQueryHandler<GetBranchQuery, BranchListItem>
{
    public async Task<BranchListItem> Handle(GetBranchQuery query, CancellationToken cancellationToken) =>
        await branches.GetListItemAsync(query.BranchId, cancellationToken)
        ?? throw new DomainException(OrganizationCodes.BranchNotFound, "Branch not found.", DomainFault.NotFound);
}

/// <summary>
/// CP-04: the branches the current session may bind to — the membership's assigned branches that are active, or every
/// active branch of the tenant when the membership is unrestricted (intake A1). Any authenticated actor, for themselves.
/// </summary>
[RequireAuthenticatedActor]
public sealed record ListSelectableBranchesQuery : IQuery<SelectableBranchesResponse>;

public sealed class ListSelectableBranchesHandler(ICurrentUser currentUser, ISessionPrincipalQuery principals, IBranchLookup branches) : IQueryHandler<ListSelectableBranchesQuery, SelectableBranchesResponse>
{
    public async Task<SelectableBranchesResponse> Handle(ListSelectableBranchesQuery query, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId || currentUser.SessionId is not { } sessionId)
        {
            throw new DomainException(ProblemCodes.AuthenticatedActorRequired, "A session-bound actor is required.", DomainFault.Unauthenticated);
        }

        var principal = await principals.ResolveAsync(sessionId, userId, cancellationToken)
            ?? throw new DomainException(ProblemCodes.TokenSessionMismatch, "The session could not be resolved.", DomainFault.Unauthenticated);
        if (principal.TenantId is null)
        {
            return new SelectableBranchesResponse(null, []);
        }

        var active = await branches.ListActiveAsync(cancellationToken);
        var selectable = principal.BranchIds.Count == 0 ? active : active.Where(branch => principal.BranchIds.Contains(branch.Id)).ToList();
        return new SelectableBranchesResponse(principal.BranchId, selectable.Select(branch => new SelectableBranch(branch.Id, branch.Code, branch.NameEn, branch.NameAr)).ToList());
    }
}

[RequirePermissionPolicy(PermissionCatalog.Organization, PermissionAction.View)]
public sealed record ListRolesQuery(string? Search, bool? IsActive, int? Page, int? PageSize, string? Sort = null, string? Direction = null) : IQuery<Paged<RoleListItem>>;

public sealed class ListRolesValidator : AbstractValidator<ListRolesQuery>
{
    public ListRolesValidator()
    {
        RuleFor(query => query.Search).MaximumLength(100);
        RuleFor(query => query.Page).GreaterThan(0).When(query => query.Page is not null);
        RuleFor(query => query.PageSize).InclusiveBetween(1, PageRequest.MaxPageSize).When(query => query.PageSize is not null);
        RuleFor(query => query.Sort).SortKeyOf(OrganizationSortKeys.Roles);
        RuleFor(query => query.Direction).SortDirection();
    }
}

public sealed class ListRolesHandler(IRoleRepository roles) : IQueryHandler<ListRolesQuery, Paged<RoleListItem>>
{
    public Task<Paged<RoleListItem>> Handle(ListRolesQuery query, CancellationToken cancellationToken) =>
        roles.ListAsync(new RoleFilter(query.Search, query.IsActive, ListSort.Of(query.Sort, query.Direction)), PageRequest.Normalized(query.Page, query.PageSize), cancellationToken);
}

[RequirePermissionPolicy(PermissionCatalog.Organization, PermissionAction.View)]
public sealed record GetRoleQuery(Guid RoleId) : IQuery<RoleDetail>;

public sealed class GetRoleHandler(IRoleRepository roles) : IQueryHandler<GetRoleQuery, RoleDetail>
{
    public async Task<RoleDetail> Handle(GetRoleQuery query, CancellationToken cancellationToken) =>
        await roles.GetDetailAsync(query.RoleId, cancellationToken)
        ?? throw new DomainException(OrganizationCodes.RoleNotFound, "Role not found.", DomainFault.NotFound);
}

/// <summary>
/// The privilege matrix source (CP-37): every module of <see cref="PermissionCatalog.All"/> with its actions. A laboratory
/// sees no platform-only module (INV-ORG-007); the platform tenant sees them flagged. Arabic names come from the SPA's
/// dictionary keyed by module key.
/// </summary>
[RequirePermissionPolicy(PermissionCatalog.Organization, PermissionAction.View)]
public sealed record GetPermissionCatalogueQuery : IQuery<PermissionCatalogueResponse>;

public sealed class GetPermissionCatalogueHandler(ICurrentTenant tenant) : IQueryHandler<GetPermissionCatalogueQuery, PermissionCatalogueResponse>
{
    public Task<PermissionCatalogueResponse> Handle(GetPermissionCatalogueQuery query, CancellationToken cancellationToken)
    {
        var platform = tenant.TenantId == TenantIds.Platform;
        var modules = PermissionCatalog.All
            .Select(module => new PermissionModuleDto(module.Key, module.DisplayName, module.Actions.Select(action => action.ToKey()).ToList(), EffectivePrivileges.PlatformOnlyModules.Contains(module.Key)))
            .Where(module => platform || !module.PlatformOnly)
            .ToList();
        var actions = Enum.GetValues<PermissionAction>().Select(action => action.ToKey()).ToList();
        return Task.FromResult(new PermissionCatalogueResponse(modules, actions));
    }
}
