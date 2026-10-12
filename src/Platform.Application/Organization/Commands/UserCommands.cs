using FluentValidation;
using Platform.Application.Abstractions.Audit;
using Platform.Application.Abstractions.Messaging;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Abstractions.Security;
using Platform.Application.Abstractions.Settings;
using Platform.Application.Identity;
using Platform.Application.Tenancy;
using Platform.Contracts.Common;
using Platform.Domain.Authorization;
using Platform.Domain.Identity;
using Platform.Domain.Organization;
using Platform.Domain.Settings;
using Platform.Domain.Tenancy;
using Platform.SharedKernel.Abstractions;
using Platform.SharedKernel.Localization;
using Platform.SharedKernel.Primitives;

namespace Platform.Application.Organization.Commands;

// Staff accounts (SRS CP-38 / SA-07; transitions U1–U3). A user is a platform user (global credentials, D-06) plus
// the membership in the current tenant; the route id is the platform user id.

[RequirePermissionPolicy(PermissionCatalog.Identity, PermissionAction.Create)]
public sealed record CreateUserCommand(
    string Username,
    string Password,
    bool MustChangePassword,
    string FullNameEn,
    string FullNameAr,
    string Email,
    string? Mobile,
    string? Designation,
    IReadOnlyList<string>? Shifts,
    Guid? RoleId,
    IReadOnlyList<Guid>? BranchIds,
    DateTimeOffset? PasswordExpiresAtUtc,
    string? EmployeeCode,
    string? PreferredLanguage,
    bool? AllBranches = null) : ICommand<CreatedResource>;

public sealed class CreateUserValidator : AbstractValidator<CreateUserCommand>
{
    public CreateUserValidator()
    {
        RuleFor(command => command.Username).NotEmpty().MaximumLength(StaffAccount.MaxUsernameLength);
        RuleFor(command => command.Password).NotEmpty().MaximumLength(PasswordRules.MaxLength);
        RuleFor(command => command.FullNameEn).NotEmpty().MaximumLength(LocalizedText.MaxLength);
        RuleFor(command => command.FullNameAr).NotEmpty().MaximumLength(LocalizedText.MaxLength);
        RuleFor(command => command.Email).NotEmpty().MaximumLength(PlatformUser.MaxEmailLength);
        RuleFor(command => command.Mobile).MaximumLength(PlatformUser.MaxMobileLength + 10);
        RuleFor(command => command.Designation).MaximumLength(TenantMembership.MaxDesignationLength);
        RuleFor(command => command.Shifts).Must(shifts => shifts is null || shifts.Count <= TenantMembership.MaxShifts);
        RuleFor(command => command.BranchIds).Must(ids => ids is null || ids.Count <= TenantMembership.MaxBranches);
        RuleFor(command => command.EmployeeCode).MaximumLength(TenantMembership.MaxEmployeeCodeLength);
        RuleFor(command => command.PreferredLanguage).Must(language => language is null || language is "en" or "ar");
    }
}

public sealed class CreateUserHandler(
    IPlatformUserRepository users,
    ITenantMembershipRepository memberships,
    IRoleRepository roles,
    IBranchLookup branches,
    IPasswordHasher hasher,
    ITenantSettings settings,
    IUserPrivileges privileges,
    ICurrentTenant tenant,
    IUserMobileNumbers mobiles,
    IUnitOfWork unitOfWork,
    IClock clock) : ICommandHandler<CreateUserCommand, CreatedResource>
{
    public async Task<CreatedResource> Handle(CreateUserCommand command, CancellationToken cancellationToken)
    {
        var tenantId = OrganizationGuards.Tenant(tenant);
        var now = clock.UtcNow;

        var username = StaffAccount.CleanUsername(command.Username);
        if (await users.UsernameExistsAsync(username, cancellationToken))
        {
            throw new DomainException(OrganizationCodes.UsernameExists, $"The username '{username}' is taken.", DomainFault.Conflict);
        }

        var email = PlatformUser.NormaliseEmail(command.Email);
        if (await users.EmailExistsAsync(email, cancellationToken))
        {
            throw new DomainException(OrganizationCodes.EmailExists, "Another account uses this e-mail address.", DomainFault.Conflict);
        }

        // E.164 by the country rules, unique across every spelling a number may be stored under (validation C-03).
        var mobile = await mobiles.CanonicalAsync(command.Mobile, tenantId, cancellationToken);
        if (mobile is not null && await mobiles.IsTakenAsync(mobile, null, cancellationToken))
        {
            throw new DomainException(OrganizationCodes.MobileExists, "Another account uses this mobile number.", DomainFault.Conflict);
        }

        var employeeCode = TenantMembership.CleanEmployeeCode(string.IsNullOrWhiteSpace(command.EmployeeCode) ? username.ToUpperInvariant() : command.EmployeeCode);
        if (await memberships.EmployeeCodeExistsAsync(employeeCode, cancellationToken))
        {
            throw new DomainException(OrganizationCodes.EmployeeCodeExists, $"The employee code '{employeeCode}' is taken.", DomainFault.Conflict);
        }

        PasswordRules.Ensure(command.Password, username, await settings.GetAsync(SettingKeys.PasswordMinLength, cancellationToken));

        Role? role = null;
        if (command.RoleId is { } roleId)
        {
            role = await roles.GetForUpdateAsync(roleId, cancellationToken)
                ?? throw new DomainException(OrganizationCodes.RoleNotFound, "Role not found.", DomainFault.NotFound);
            OrganizationGuards.EnsureGrantable(privileges, role.PermissionKeys);   // assigning a role grants its keys (INV-ORG-011)
        }

        OrganizationGuards.EnsureBranchChoice(command.AllBranches, command.BranchIds);   // B-02: restricted with no branch is not "every branch"
        var branchIds = await OrganizationGuards.RequireUsableBranchesAsync(branches, command.BranchIds, cancellationToken);
        var name = new LocalizedText(command.FullNameEn, command.FullNameAr);

        var user = PlatformUser.Register(username, email, name.En, hasher.Hash(command.Password), now, mobile);
        user.RequirePasswordChange(command.MustChangePassword);
        user.SetPasswordExpiry(command.PasswordExpiresAtUtc, now);

        var language = command.PreferredLanguage ?? "en";
        var membership = TenantMembership.Create(tenantId, user.Id, employeeCode, name, language, now);
        membership.UpdateProfile(name, command.Designation, command.Shifts ?? [], language);
        membership.AssignBranches(branchIds);
        if (role is not null)
        {
            membership.AssignRole(role, now);   // ORG-010 when inactive
        }

        users.Add(user);
        memberships.Add(membership);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new CreatedResource(user.Id);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Identity, PermissionAction.Edit)]
public sealed record UpdateUserCommand(
    Guid UserId,
    string FullNameEn,
    string FullNameAr,
    string Email,
    string? Mobile,
    string? Designation,
    IReadOnlyList<string>? Shifts,
    string PreferredLanguage,
    DateTimeOffset? PasswordExpiresAtUtc,
    bool MustChangePassword) : ICommand;

public sealed class UpdateUserValidator : AbstractValidator<UpdateUserCommand>
{
    public UpdateUserValidator()
    {
        RuleFor(command => command.UserId).NotEmpty();
        RuleFor(command => command.FullNameEn).NotEmpty().MaximumLength(LocalizedText.MaxLength);
        RuleFor(command => command.FullNameAr).NotEmpty().MaximumLength(LocalizedText.MaxLength);
        RuleFor(command => command.Email).NotEmpty().MaximumLength(PlatformUser.MaxEmailLength);
        RuleFor(command => command.Mobile).MaximumLength(PlatformUser.MaxMobileLength + 10);
        RuleFor(command => command.Designation).MaximumLength(TenantMembership.MaxDesignationLength);
        RuleFor(command => command.Shifts).Must(shifts => shifts is null || shifts.Count <= TenantMembership.MaxShifts);
        RuleFor(command => command.PreferredLanguage).NotEmpty().Must(language => language is "en" or "ar");
    }
}

/// <summary>
/// U2 Edit profile. Names, designation, shifts and language belong to the membership. E-mail, mobile, expiry and the
/// must-change flag belong to the global account: they change only for a user of this tenant alone (ORG-031), and
/// nobody sets an expiry on the main administrator but the administrator (TEN-012).
/// </summary>
public sealed class UpdateUserHandler(
    IPlatformUserRepository users,
    ITenantMembershipRepository memberships,
    IMembershipDirectory directory,
    IPrincipalCacheInvalidator principals,
    ICurrentUser currentUser,
    ICurrentTenant tenant,
    IUserMobileNumbers mobiles,
    IUnitOfWork unitOfWork,
    IClock clock) : ICommandHandler<UpdateUserCommand>
{
    public async Task Handle(UpdateUserCommand command, CancellationToken cancellationToken)
    {
        var tenantId = OrganizationGuards.Tenant(tenant);
        var actor = OrganizationGuards.Actor(currentUser);
        var now = clock.UtcNow;
        var membership = await memberships.FindByPlatformUserAsync(command.UserId, cancellationToken)
            ?? throw new DomainException(OrganizationCodes.UserNotFound, "User not found.", DomainFault.NotFound);
        var user = await users.GetForUpdateAsync(command.UserId, cancellationToken)
            ?? throw new DomainException(OrganizationCodes.UserNotFound, "User not found.", DomainFault.NotFound);

        var email = PlatformUser.NormaliseEmail(command.Email);
        // E.164 by the country rules (validation C-03). A row saved before the rule may hold the same number in another
        // spelling: that is not a change (a user of several organisations keeps it; a sole-tenancy save normalises it).
        var mobile = await mobiles.CanonicalAsync(command.Mobile, tenantId, cancellationToken);
        var mobileChanged = mobile != user.Mobile
            && !(mobile is not null && user.Mobile is not null && (await mobiles.SpellingsAsync(user.Mobile, cancellationToken)).Contains(mobile));
        var accountChanged = email != user.Email || mobileChanged || command.PasswordExpiresAtUtc != user.PasswordExpiresAtUtc || command.MustChangePassword != user.MustChangePassword;
        var soleTenancy = !await OrganizationGuards.BelongsToOtherTenantsAsync(directory, user.Id, tenantId, cancellationToken);
        if (accountChanged && !soleTenancy)
        {
            throw new DomainException(OrganizationCodes.UserBelongsToOtherTenants, "This user also works for other organisations; their account data cannot be changed here.", DomainFault.Refused);
        }

        if (membership.IsPrimaryAdministrator && actor != user.Id && command.PasswordExpiresAtUtc is not null && command.PasswordExpiresAtUtc != user.PasswordExpiresAtUtc)
        {
            throw new DomainException(TenancyCodes.PrimaryAdministratorProtected, "Only the main administrator sets their own expiry.", DomainFault.Refused);
        }

        if (email != user.Email && await users.EmailExistsAsync(email, cancellationToken))
        {
            throw new DomainException(OrganizationCodes.EmailExists, "Another account uses this e-mail address.", DomainFault.Conflict);
        }

        if (mobile is not null && mobileChanged && await mobiles.IsTakenAsync(mobile, user.Id, cancellationToken))
        {
            throw new DomainException(OrganizationCodes.MobileExists, "Another account uses this mobile number.", DomainFault.Conflict);
        }

        var name = new LocalizedText(command.FullNameEn, command.FullNameAr);
        membership.UpdateProfile(name, command.Designation, command.Shifts ?? [], command.PreferredLanguage);
        if (soleTenancy)
        {
            user.Rename(name.En);   // the global display name follows the only membership
            user.ChangeEmail(email);
            user.SetMobile(mobile);
            user.SetPasswordExpiry(command.PasswordExpiresAtUtc, now);
            user.RequirePasswordChange(command.MustChangePassword);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await principals.EvictUserAsync(user.Id, cancellationToken);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Identity, PermissionAction.Edit)]
/// <summary><see cref="AllBranches"/>: true = every active branch (the list must be empty), false = the listed branches (at least one; ORG-018), null = the older contract (empty = every branch).</summary>
public sealed record AssignUserBranchesCommand(Guid UserId, IReadOnlyList<Guid> BranchIds, bool? AllBranches = null) : ICommand;

public sealed class AssignUserBranchesValidator : AbstractValidator<AssignUserBranchesCommand>
{
    public AssignUserBranchesValidator()
    {
        RuleFor(command => command.UserId).NotEmpty();
        RuleFor(command => command.BranchIds).NotNull().Must(ids => ids is null || ids.Count <= TenantMembership.MaxBranches);
    }
}

/// <summary>U3 Assign branches (empty = every active branch): each must exist and be active (ORG-012); SOD-ORG-001.</summary>
public sealed class AssignUserBranchesHandler(
    ITenantMembershipRepository memberships,
    IBranchLookup branches,
    IPrincipalCacheInvalidator principals,
    ISecurityEventLog events,
    ICurrentUser currentUser,
    ICurrentTenant tenant,
    IUnitOfWork unitOfWork) : ICommandHandler<AssignUserBranchesCommand>
{
    public async Task Handle(AssignUserBranchesCommand command, CancellationToken cancellationToken)
    {
        OrganizationGuards.EnsureNotSelf(OrganizationGuards.Actor(currentUser), command.UserId);
        var membership = await memberships.FindByPlatformUserAsync(command.UserId, cancellationToken)
            ?? throw new DomainException(OrganizationCodes.UserNotFound, "User not found.", DomainFault.NotFound);

        OrganizationGuards.EnsureBranchChoice(command.AllBranches, command.BranchIds);
        membership.AssignBranches(await OrganizationGuards.RequireUsableBranchesAsync(branches, command.BranchIds, cancellationToken));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await principals.EvictUserAsync(command.UserId, cancellationToken);
        await events.WriteAsync(new SecurityEvent(OrganizationSecurityEvents.MembershipChanged, SecurityEvent.Info, tenant.TenantId, currentUser.UserId, $"user:{command.UserId}:branches"), cancellationToken);
    }
}
