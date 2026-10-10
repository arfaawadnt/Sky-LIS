using FluentValidation;
using Platform.Application.Abstractions.Messaging;
using Platform.Application.Abstractions.Security;
using Platform.Application.Identity;
using Platform.Contracts.Common;
using Platform.Contracts.Organization;
using Platform.Domain.Authorization;
using Platform.Domain.Organization;
using Platform.SharedKernel.Abstractions;
using Platform.SharedKernel.MultiTenancy;
using Platform.SharedKernel.Primitives;

namespace Platform.Application.Organization.Queries;

// The users register (CP-38, SA-07) and the actor's own privileges.

/// <summary><paramref name="Status"/> is a status name: Active, Locked, Expired, Inactive.</summary>
[RequirePermissionPolicy(PermissionCatalog.Identity, PermissionAction.View)]
public sealed record ListUsersQuery(string? Search, Guid? RoleId, string? Status, int? Page, int? PageSize) : IQuery<Paged<UserListItem>>;

public sealed class ListUsersValidator : AbstractValidator<ListUsersQuery>
{
    public ListUsersValidator()
    {
        RuleFor(query => query.Search).MaximumLength(100);
        RuleFor(query => query.Status).IsEnumName(typeof(StaffStatus), caseSensitive: false).When(query => !string.IsNullOrEmpty(query.Status));
        RuleFor(query => query.Page).GreaterThan(0).When(query => query.Page is not null);
        RuleFor(query => query.PageSize).InclusiveBetween(1, PageRequest.MaxPageSize).When(query => query.PageSize is not null);
    }
}

public sealed class ListUsersHandler(IStaffDirectory directory, IClock clock) : IQueryHandler<ListUsersQuery, Paged<UserListItem>>
{
    public Task<Paged<UserListItem>> Handle(ListUsersQuery query, CancellationToken cancellationToken)
    {
        StaffStatus? status = string.IsNullOrEmpty(query.Status) ? null : Enum.Parse<StaffStatus>(query.Status, ignoreCase: true);
        return directory.ListAsync(new StaffFilter(query.Search, query.RoleId, status), PageRequest.Normalized(query.Page, query.PageSize), clock.UtcNow, cancellationToken);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Identity, PermissionAction.View)]
public sealed record GetUserQuery(Guid UserId) : IQuery<UserDetail>;

/// <summary>The user editor: the stored account plus the effective privileges resolved exactly as the session resolves them (BR-17).</summary>
public sealed class GetUserHandler(IStaffDirectory directory, IMembershipDirectory memberships, ICurrentTenant tenant, IClock clock) : IQueryHandler<GetUserQuery, UserDetail>
{
    public async Task<UserDetail> Handle(GetUserQuery query, CancellationToken cancellationToken)
    {
        var account = await directory.GetAsync(query.UserId, cancellationToken)
            ?? throw new DomainException(OrganizationCodes.UserNotFound, "User not found.", DomainFault.NotFound);
        var tenantId = tenant.TenantId ?? Guid.Empty;
        var now = clock.UtcNow;

        var permissions = EffectivePrivileges.PermissionKeys(account.RoleActive ? account.RolePermissionKeys : [], account.AddedPermissionKeys, account.RemovedPermissionKeys, tenantId == TenantIds.Platform);
        var special = EffectivePrivileges.SpecialRightsOf(account.RoleActive ? account.RoleSpecialRights : null, account.SpecialRightsOverride);
        var results = account.RoleActive ? account.RoleResultRights : [];
        var locked = account.IsLockedOut && (account.LockoutEndUtc is null || account.LockoutEndUtc > now);
        var belongsElsewhere = await OrganizationGuards.BelongsToOtherTenantsAsync(memberships, account.UserId, tenantId, cancellationToken);

        return new UserDetail(
            account.UserId,
            account.MembershipId,
            account.Username,
            account.FullNameEn,
            account.FullNameAr,
            account.Email,
            account.Mobile,
            account.EmployeeCode,
            account.Designation,
            account.Shifts,
            account.PreferredLanguage,
            account.RoleId,
            account.RoleNameEn,
            account.RoleNameAr,
            account.BranchIds,
            StaffStatuses.Of(account.UserActive, account.MembershipActive, locked, account.PasswordExpiresAtUtc, now).ToString(),
            account.UserActive && account.MembershipActive,
            locked,
            account.LockoutEndUtc,
            account.MustChangePassword,
            account.PasswordExpiresAtUtc,
            account.PasswordChangedAtUtc,
            account.SignatureFileId,
            account.IsPrimaryAdministrator,
            account.AddedPermissionKeys.Count > 0 || account.RemovedPermissionKeys.Count > 0 || account.SpecialRightsOverride is not null,
            belongsElsewhere,
            new UserOverridesDto(account.AddedPermissionKeys, account.RemovedPermissionKeys, account.SpecialRightsOverride is null ? null : OrganizationGuards.ToDto(account.SpecialRightsOverride)),
            new EffectivePrivilegesDto(permissions.Order(StringComparer.Ordinal).ToList(), OrganizationGuards.ToDto(special), results.Select(OrganizationGuards.ToDto).ToList()),
            account.RoleActive ? account.RolePermissionKeys : [],
            OrganizationGuards.ToDto(account.RoleActive ? account.RoleSpecialRights ?? SpecialRights.None : SpecialRights.None));
    }
}

/// <summary>The current session's effective privileges, for the screens to offer only what the server allows (any authenticated actor).</summary>
[RequireAuthenticatedActor]
public sealed record GetMyPrivilegesQuery : IQuery<MyPrivilegesResponse>;

public sealed class GetMyPrivilegesHandler(ICurrentUser currentUser, ISessionPrincipalQuery principals) : IQueryHandler<GetMyPrivilegesQuery, MyPrivilegesResponse>
{
    public async Task<MyPrivilegesResponse> Handle(GetMyPrivilegesQuery query, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId || currentUser.SessionId is not { } sessionId)
        {
            throw new DomainException(ProblemCodes.AuthenticatedActorRequired, "A session-bound actor is required.", DomainFault.Unauthenticated);
        }

        var principal = await principals.ResolveAsync(sessionId, userId, cancellationToken)
            ?? throw new DomainException(ProblemCodes.TokenSessionMismatch, "The session could not be resolved.", DomainFault.Unauthenticated);

        return new MyPrivilegesResponse(
            principal.UserId,
            principal.TenantId,
            principal.BranchId,
            principal.IsPrimaryAdministrator,
            principal.MustChangePassword,
            principal.PasswordExpiresAtUtc,
            new EffectivePrivilegesDto(
                principal.PermissionKeys.Order(StringComparer.Ordinal).ToList(),
                OrganizationGuards.ToDto(principal.SpecialRights),
                principal.ResultRights.Select(OrganizationGuards.ToDto).ToList()));
    }
}

/// <summary>The status rule of the users register: inactive beats locked beats expired.</summary>
public static class StaffStatuses
{
    public static StaffStatus Of(bool userActive, bool membershipActive, bool locked, DateTimeOffset? passwordExpiresAtUtc, DateTimeOffset nowUtc)
    {
        if (!userActive || !membershipActive)
        {
            return StaffStatus.Inactive;
        }

        if (locked)
        {
            return StaffStatus.Locked;
        }

        return passwordExpiresAtUtc is { } expiry && expiry <= nowUtc ? StaffStatus.Expired : StaffStatus.Active;
    }
}
