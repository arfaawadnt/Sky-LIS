using FluentValidation;
using Platform.Application.Abstractions.Audit;
using Platform.Application.Abstractions.Messaging;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Abstractions.Security;
using Platform.Application.Abstractions.Settings;
using Platform.Application.Identity;
using Platform.Application.Tenancy;
using Platform.Contracts.Organization;
using Platform.Domain.Authorization;
using Platform.Domain.Identity;
using Platform.Domain.Organization;
using Platform.Domain.Settings;
using Platform.Domain.Tenancy;
using Platform.SharedKernel.Abstractions;
using Platform.SharedKernel.MultiTenancy;
using Platform.SharedKernel.Primitives;

namespace Platform.Application.Organization.Commands;

// Access administration of a user (transitions U4–U11 of intake LIS-INT-ORG-001), all under identity.manage and
// SOD-ORG-001 (an administrator does not administer their own access). Each handler commits once; cache eviction
// and the security event follow the commit.

[RequirePermissionPolicy(PermissionCatalog.Identity, PermissionAction.Manage)]
public sealed record AssignUserRoleCommand(Guid UserId, Guid RoleId) : ICommand;

public sealed class AssignUserRoleValidator : AbstractValidator<AssignUserRoleCommand>
{
    public AssignUserRoleValidator()
    {
        RuleFor(command => command.UserId).NotEmpty();
        RuleFor(command => command.RoleId).NotEmpty();
    }
}

/// <summary>U4: the member inherits the role (BR-17). The role must be active (ORG-010) and grantable by the actor (ORG-030); the main administrator keeps theirs (TEN-012).</summary>
public sealed class AssignUserRoleHandler(
    ITenantMembershipRepository memberships,
    IRoleRepository roles,
    IUserPrivileges privileges,
    IPrincipalCacheInvalidator principals,
    ISecurityEventLog events,
    ICurrentUser currentUser,
    ICurrentTenant tenant,
    IUnitOfWork unitOfWork,
    IClock clock) : ICommandHandler<AssignUserRoleCommand>
{
    public async Task Handle(AssignUserRoleCommand command, CancellationToken cancellationToken)
    {
        OrganizationGuards.EnsureNotSelf(OrganizationGuards.Actor(currentUser), command.UserId);
        var membership = await memberships.FindByPlatformUserAsync(command.UserId, cancellationToken)
            ?? throw new DomainException(OrganizationCodes.UserNotFound, "User not found.", DomainFault.NotFound);
        var role = await roles.GetForUpdateAsync(command.RoleId, cancellationToken)
            ?? throw new DomainException(OrganizationCodes.RoleNotFound, "Role not found.", DomainFault.NotFound);
        OrganizationGuards.EnsureGrantable(privileges, role.PermissionKeys);

        membership.AssignRole(role, clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await principals.EvictUserAsync(command.UserId, cancellationToken);
        await events.WriteAsync(new SecurityEvent(OrganizationSecurityEvents.MembershipChanged, SecurityEvent.Info, tenant.TenantId, currentUser.UserId, $"user:{command.UserId}:role:{role.Id}"), cancellationToken);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Identity, PermissionAction.Manage)]
public sealed record SetUserOverridesCommand(Guid UserId, IReadOnlyList<string> AddedPermissionKeys, IReadOnlyList<string> RemovedPermissionKeys, SpecialRightsDto? SpecialRights) : ICommand;

public sealed class SetUserOverridesValidator : AbstractValidator<SetUserOverridesCommand>
{
    public SetUserOverridesValidator()
    {
        RuleFor(command => command.UserId).NotEmpty();
        RuleFor(command => command.AddedPermissionKeys).NotNull().Must(keys => keys is null || keys.Count <= EffectivePrivileges.MaxKeys);
        RuleFor(command => command.RemovedPermissionKeys).NotNull().Must(keys => keys is null || keys.Count <= EffectivePrivileges.MaxKeys);
        RuleFor(command => command.SpecialRights!.DiscountCapPercent)
            .InclusiveBetween(0, SpecialRights.MaxDiscountCapPercent)
            .When(command => command.SpecialRights?.DiscountCapPercent is not null);
    }
}

/// <summary>
/// U5 (BR-17): this user only. Not in the platform tenant (SA-07, ORG-032); newly added keys must be grantable by the
/// actor (ORG-030) and allowed in the tenant (ORG-033); nothing is withheld from the main administrator (TEN-012).
/// </summary>
public sealed class SetUserOverridesHandler(
    ITenantMembershipRepository memberships,
    IUserPrivileges privileges,
    IPrincipalCacheInvalidator principals,
    ISecurityEventLog events,
    ICurrentUser currentUser,
    ICurrentTenant tenant,
    IUnitOfWork unitOfWork,
    IClock clock) : ICommandHandler<SetUserOverridesCommand>
{
    public async Task Handle(SetUserOverridesCommand command, CancellationToken cancellationToken)
    {
        var tenantId = OrganizationGuards.Tenant(tenant);
        OrganizationGuards.EnsureNotSelf(OrganizationGuards.Actor(currentUser), command.UserId);
        if (tenantId == TenantIds.Platform)
        {
            throw new DomainException(OrganizationCodes.OverridesNotAllowedOnPlatform, "Platform staff inherit their role only; per-user overrides are not offered.", DomainFault.Refused);
        }

        var membership = await memberships.FindByPlatformUserAsync(command.UserId, cancellationToken)
            ?? throw new DomainException(OrganizationCodes.UserNotFound, "User not found.", DomainFault.NotFound);

        var added = EffectivePrivileges.Clean(command.AddedPermissionKeys);
        OrganizationGuards.EnsureTenantMayGrant(tenantId, added);
        OrganizationGuards.EnsureGrantable(privileges, added.Except(membership.AddedPermissionKeys, StringComparer.Ordinal));

        membership.SetOverrides(added, command.RemovedPermissionKeys, command.SpecialRights is null ? null : OrganizationGuards.ToDomain(command.SpecialRights), clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await principals.EvictUserAsync(command.UserId, cancellationToken);
        await events.WriteAsync(new SecurityEvent(OrganizationSecurityEvents.MembershipChanged, SecurityEvent.Info, tenantId, currentUser.UserId, $"user:{command.UserId}:overrides"), cancellationToken);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Identity, PermissionAction.Manage)]
public sealed record RestoreUserToRoleCommand(Guid UserId) : ICommand;

public sealed class RestoreUserToRoleValidator : AbstractValidator<RestoreUserToRoleCommand>
{
    public RestoreUserToRoleValidator() => RuleFor(command => command.UserId).NotEmpty();
}

/// <summary>U6 Restore: the user's overrides are dropped and the role applies again (LDM manual §12.8).</summary>
public sealed class RestoreUserToRoleHandler(
    ITenantMembershipRepository memberships,
    IPrincipalCacheInvalidator principals,
    ISecurityEventLog events,
    ICurrentUser currentUser,
    ICurrentTenant tenant,
    IUnitOfWork unitOfWork,
    IClock clock) : ICommandHandler<RestoreUserToRoleCommand>
{
    public async Task Handle(RestoreUserToRoleCommand command, CancellationToken cancellationToken)
    {
        OrganizationGuards.EnsureNotSelf(OrganizationGuards.Actor(currentUser), command.UserId);
        var membership = await memberships.FindByPlatformUserAsync(command.UserId, cancellationToken)
            ?? throw new DomainException(OrganizationCodes.UserNotFound, "User not found.", DomainFault.NotFound);

        membership.RestoreToRole(clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await principals.EvictUserAsync(command.UserId, cancellationToken);
        await events.WriteAsync(new SecurityEvent(OrganizationSecurityEvents.MembershipChanged, SecurityEvent.Info, tenant.TenantId, currentUser.UserId, $"user:{command.UserId}:restored"), cancellationToken);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Identity, PermissionAction.Manage)]
public sealed record LockUserCommand(Guid UserId) : ICommand;

public sealed class LockUserValidator : AbstractValidator<LockUserCommand>
{
    public LockUserValidator() => RuleFor(command => command.UserId).NotEmpty();
}

/// <summary>U7 Lock: sign-in is refused until unlocked and every live session ends now (02 §4.1). Not the main administrator (TEN-012), not a user of other tenants (ORG-031).</summary>
public sealed class LockUserHandler(
    IPlatformUserRepository users,
    ITenantMembershipRepository memberships,
    IRefreshSessionRepository sessions,
    IMembershipDirectory directory,
    ISessionPrincipalQuery principals,
    ISecurityEventLog events,
    ICurrentUser currentUser,
    ICurrentTenant tenant,
    IUnitOfWork unitOfWork,
    IClock clock) : ICommandHandler<LockUserCommand>
{
    public async Task Handle(LockUserCommand command, CancellationToken cancellationToken)
    {
        var tenantId = OrganizationGuards.Tenant(tenant);
        OrganizationGuards.EnsureNotSelf(OrganizationGuards.Actor(currentUser), command.UserId);
        var membership = await memberships.FindByPlatformUserAsync(command.UserId, cancellationToken)
            ?? throw new DomainException(OrganizationCodes.UserNotFound, "User not found.", DomainFault.NotFound);
        if (membership.IsPrimaryAdministrator)
        {
            throw new DomainException(TenancyCodes.PrimaryAdministratorProtected, "The main administrator cannot be locked.", DomainFault.Refused);
        }

        await OrganizationGuards.EnsureSoleTenancyAsync(directory, command.UserId, tenantId, cancellationToken);
        var user = await users.GetForUpdateAsync(command.UserId, cancellationToken)
            ?? throw new DomainException(OrganizationCodes.UserNotFound, "User not found.", DomainFault.NotFound);

        var now = clock.UtcNow;
        user.LockByAdministrator(now);
        var revoked = await sessions.ListLiveForUserAsync(user.Id, cancellationToken);
        foreach (var session in revoked)
        {
            session.Revoke(RevocationReason.Admin, now);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);   // one commit: the lock and the revocations
        foreach (var session in revoked)
        {
            principals.Evict(session.Id);
        }

        await events.WriteAsync(new SecurityEvent(OrganizationSecurityEvents.MembershipChanged, SecurityEvent.Warning, tenantId, currentUser.UserId, $"user:{user.Id}:locked"), cancellationToken);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Identity, PermissionAction.Manage)]
public sealed record UnlockUserCommand(Guid UserId) : ICommand;

public sealed class UnlockUserValidator : AbstractValidator<UnlockUserCommand>
{
    public UnlockUserValidator() => RuleFor(command => command.UserId).NotEmpty();
}

/// <summary>U8 Unlock: clears an administrator's lock or an automatic lockout; the failure counter restarts.</summary>
public sealed class UnlockUserHandler(
    IPlatformUserRepository users,
    ITenantMembershipRepository memberships,
    IMembershipDirectory directory,
    ISecurityEventLog events,
    ICurrentUser currentUser,
    ICurrentTenant tenant,
    IUnitOfWork unitOfWork,
    IClock clock) : ICommandHandler<UnlockUserCommand>
{
    public async Task Handle(UnlockUserCommand command, CancellationToken cancellationToken)
    {
        var tenantId = OrganizationGuards.Tenant(tenant);
        OrganizationGuards.EnsureNotSelf(OrganizationGuards.Actor(currentUser), command.UserId);
        _ = await memberships.FindByPlatformUserAsync(command.UserId, cancellationToken)
            ?? throw new DomainException(OrganizationCodes.UserNotFound, "User not found.", DomainFault.NotFound);
        await OrganizationGuards.EnsureSoleTenancyAsync(directory, command.UserId, tenantId, cancellationToken);
        var user = await users.GetForUpdateAsync(command.UserId, cancellationToken)
            ?? throw new DomainException(OrganizationCodes.UserNotFound, "User not found.", DomainFault.NotFound);

        user.Unlock(clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await events.WriteAsync(new SecurityEvent(OrganizationSecurityEvents.MembershipChanged, SecurityEvent.Info, tenantId, currentUser.UserId, $"user:{user.Id}:unlocked"), cancellationToken);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Identity, PermissionAction.Manage)]
public sealed record ResetUserPasswordCommand(Guid UserId, string TemporaryPassword, bool MustChangePassword) : ICommand;

public sealed class ResetUserPasswordValidator : AbstractValidator<ResetUserPasswordCommand>
{
    public ResetUserPasswordValidator()
    {
        RuleFor(command => command.UserId).NotEmpty();
        RuleFor(command => command.TemporaryPassword).NotEmpty().MaximumLength(PasswordRules.MaxLength);
    }
}

/// <summary>
/// U9 Reset password (CP-38): a temporary password under the tenant's policy (ORG-020/021), must-change by default,
/// every live session revoked (02 §4.1). The main administrator recovers by mobile instead (TEN-012); a user of other
/// tenants is refused (ORG-031).
/// </summary>
public sealed class ResetUserPasswordHandler(
    IPlatformUserRepository users,
    ITenantMembershipRepository memberships,
    IRefreshSessionRepository sessions,
    IMembershipDirectory directory,
    ISessionPrincipalQuery principals,
    IPasswordHasher hasher,
    ITenantSettings settings,
    ISecurityEventLog events,
    ICurrentUser currentUser,
    ICurrentTenant tenant,
    IUnitOfWork unitOfWork,
    IClock clock) : ICommandHandler<ResetUserPasswordCommand>
{
    public async Task Handle(ResetUserPasswordCommand command, CancellationToken cancellationToken)
    {
        var tenantId = OrganizationGuards.Tenant(tenant);
        OrganizationGuards.EnsureNotSelf(OrganizationGuards.Actor(currentUser), command.UserId);
        var membership = await memberships.FindByPlatformUserAsync(command.UserId, cancellationToken)
            ?? throw new DomainException(OrganizationCodes.UserNotFound, "User not found.", DomainFault.NotFound);
        if (membership.IsPrimaryAdministrator)
        {
            throw new DomainException(TenancyCodes.PrimaryAdministratorProtected, "The main administrator resets their password through recovery.", DomainFault.Refused);
        }

        await OrganizationGuards.EnsureSoleTenancyAsync(directory, command.UserId, tenantId, cancellationToken);
        var user = await users.GetForUpdateAsync(command.UserId, cancellationToken)
            ?? throw new DomainException(OrganizationCodes.UserNotFound, "User not found.", DomainFault.NotFound);
        PasswordRules.Ensure(command.TemporaryPassword, user.Username, await settings.GetAsync(SettingKeys.PasswordMinLength, cancellationToken));

        var now = clock.UtcNow;
        user.ResetPasswordByAdministrator(hasher.Hash(command.TemporaryPassword), command.MustChangePassword, now);
        var revoked = await sessions.ListLiveForUserAsync(user.Id, cancellationToken);
        foreach (var session in revoked)
        {
            session.Revoke(RevocationReason.PasswordChanged, now);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);   // one commit: the new hash, the stamp and the revocations
        foreach (var session in revoked)
        {
            principals.Evict(session.Id);
        }

        await events.WriteAsync(new SecurityEvent(OrganizationSecurityEvents.MembershipChanged, SecurityEvent.Warning, tenantId, currentUser.UserId, $"user:{user.Id}:password-reset"), cancellationToken);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Identity, PermissionAction.Manage)]
public sealed record DeactivateUserCommand(Guid UserId) : ICommand;

public sealed class DeactivateUserValidator : AbstractValidator<DeactivateUserCommand>
{
    public DeactivateUserValidator() => RuleFor(command => command.UserId).NotEmpty();
}

/// <summary>U10: the member stops working in this tenant (other tenants are untouched); the sessions in this tenant end. Not the main administrator (TEN-012).</summary>
public sealed class DeactivateUserHandler(
    ITenantMembershipRepository memberships,
    IRefreshSessionRepository sessions,
    ISessionPrincipalQuery principals,
    ISecurityEventLog events,
    ICurrentUser currentUser,
    ICurrentTenant tenant,
    IUnitOfWork unitOfWork,
    IClock clock) : ICommandHandler<DeactivateUserCommand>
{
    public async Task Handle(DeactivateUserCommand command, CancellationToken cancellationToken)
    {
        var tenantId = OrganizationGuards.Tenant(tenant);
        OrganizationGuards.EnsureNotSelf(OrganizationGuards.Actor(currentUser), command.UserId);
        var membership = await memberships.FindByPlatformUserAsync(command.UserId, cancellationToken)
            ?? throw new DomainException(OrganizationCodes.UserNotFound, "User not found.", DomainFault.NotFound);

        var now = clock.UtcNow;
        membership.Deactivate(now);
        var revoked = (await sessions.ListLiveForUserAsync(command.UserId, cancellationToken)).Where(session => session.TenantId == tenantId).ToList();
        foreach (var session in revoked)
        {
            session.Revoke(RevocationReason.Admin, now);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        foreach (var session in revoked)
        {
            principals.Evict(session.Id);
        }

        await events.WriteAsync(new SecurityEvent(OrganizationSecurityEvents.UserDeactivated, SecurityEvent.Info, tenantId, currentUser.UserId, $"user:{command.UserId}"), cancellationToken);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Identity, PermissionAction.Manage)]
public sealed record ReactivateUserCommand(Guid UserId) : ICommand;

public sealed class ReactivateUserValidator : AbstractValidator<ReactivateUserCommand>
{
    public ReactivateUserValidator() => RuleFor(command => command.UserId).NotEmpty();
}

public sealed class ReactivateUserHandler(
    ITenantMembershipRepository memberships,
    ISecurityEventLog events,
    ICurrentUser currentUser,
    ICurrentTenant tenant,
    IUnitOfWork unitOfWork,
    IClock clock) : ICommandHandler<ReactivateUserCommand>
{
    public async Task Handle(ReactivateUserCommand command, CancellationToken cancellationToken)
    {
        OrganizationGuards.EnsureNotSelf(OrganizationGuards.Actor(currentUser), command.UserId);
        var membership = await memberships.FindByPlatformUserAsync(command.UserId, cancellationToken)
            ?? throw new DomainException(OrganizationCodes.UserNotFound, "User not found.", DomainFault.NotFound);

        membership.Reactivate(clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await events.WriteAsync(new SecurityEvent(OrganizationSecurityEvents.MembershipChanged, SecurityEvent.Info, tenant.TenantId, currentUser.UserId, $"user:{command.UserId}:reactivated"), cancellationToken);
    }
}
