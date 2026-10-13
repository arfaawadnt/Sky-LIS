using FluentValidation;
using Platform.Application.Abstractions.Audit;
using Platform.Application.Abstractions.Messaging;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Abstractions.Security;
using Platform.Application.Tenancy;
using Platform.Contracts.Common;
using Platform.Contracts.Organization;
using Platform.Domain.Authorization;
using Platform.Domain.Organization;
using Platform.SharedKernel.Abstractions;
using Platform.SharedKernel.Localization;
using Platform.SharedKernel.Primitives;

namespace Platform.Application.Organization.Commands;

// Roles and privileges (SRS CP-37 / SA-06; transitions R1–R7). After the commit, every privilege-affecting handler
// evicts the cached principals of the role's members so the next request resolves the change (BR-17, 02 §4.4).

[RequirePermissionPolicy(PermissionCatalog.Organization, PermissionAction.Create)]
public sealed record CreateRoleCommand(string NameEn, string NameAr, string? Description, IReadOnlyList<string> PermissionKeys) : ICommand<CreatedResource>;

public sealed class CreateRoleValidator : AbstractValidator<CreateRoleCommand>
{
    public CreateRoleValidator()
    {
        RuleFor(command => command.NameEn).NotEmpty().MaximumLength(LocalizedText.MaxLength);
        RuleFor(command => command.NameAr).NotEmpty().MaximumLength(LocalizedText.MaxLength);
        RuleFor(command => command.Description).MaximumLength(Role.MaxDescriptionLength);
        RuleFor(command => command.PermissionKeys).NotNull().Must(keys => keys is null || keys.Count <= Role.MaxPermissions);
    }
}

public sealed class CreateRoleHandler(IRoleRepository roles, IUserPrivileges privileges, ICurrentTenant tenant, IUnitOfWork unitOfWork, IClock clock) : ICommandHandler<CreateRoleCommand, CreatedResource>
{
    public async Task<CreatedResource> Handle(CreateRoleCommand command, CancellationToken cancellationToken)
    {
        var keys = EffectivePrivileges.Clean(command.PermissionKeys);
        OrganizationGuards.EnsureTenantMayGrant(OrganizationGuards.Tenant(tenant), keys);
        OrganizationGuards.EnsureGrantable(privileges, keys);
        if (await roles.NameExistsAsync(command.NameEn, null, cancellationToken))
        {
            throw new DomainException(OrganizationCodes.RoleNameExists, $"A role named '{command.NameEn.Trim()}' already exists.", DomainFault.Conflict);
        }

        var role = Role.Create(new LocalizedText(command.NameEn, command.NameAr), keys, clock.UtcNow, command.Description);
        roles.Add(role);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new CreatedResource(role.Id);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Organization, PermissionAction.Edit)]
public sealed record UpdateRoleCommand(Guid RoleId, string NameEn, string NameAr, string? Description) : ICommand;

public sealed class UpdateRoleValidator : AbstractValidator<UpdateRoleCommand>
{
    public UpdateRoleValidator()
    {
        RuleFor(command => command.RoleId).NotEmpty();
        RuleFor(command => command.NameEn).NotEmpty().MaximumLength(LocalizedText.MaxLength);
        RuleFor(command => command.NameAr).NotEmpty().MaximumLength(LocalizedText.MaxLength);
        RuleFor(command => command.Description).MaximumLength(Role.MaxDescriptionLength);
    }
}

public sealed class UpdateRoleHandler(IRoleRepository roles, IUnitOfWork unitOfWork) : ICommandHandler<UpdateRoleCommand>
{
    public async Task Handle(UpdateRoleCommand command, CancellationToken cancellationToken)
    {
        var role = await roles.GetForUpdateAsync(command.RoleId, cancellationToken)
            ?? throw new DomainException(OrganizationCodes.RoleNotFound, "Role not found.", DomainFault.NotFound);
        if (await roles.NameExistsAsync(command.NameEn, role.Id, cancellationToken))
        {
            throw new DomainException(OrganizationCodes.RoleNameExists, $"A role named '{command.NameEn.Trim()}' already exists.", DomainFault.Conflict);
        }

        role.Describe(new LocalizedText(command.NameEn, command.NameAr), command.Description);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Organization, PermissionAction.Edit)]
public sealed record SetRolePermissionsCommand(Guid RoleId, IReadOnlyList<string> PermissionKeys) : ICommand;

public sealed class SetRolePermissionsValidator : AbstractValidator<SetRolePermissionsCommand>
{
    public SetRolePermissionsValidator()
    {
        RuleFor(command => command.RoleId).NotEmpty();
        RuleFor(command => command.PermissionKeys).NotNull().Must(keys => keys is null || keys.Count <= Role.MaxPermissions);
    }
}

public sealed class SetRolePermissionsHandler(
    IRoleRepository roles,
    ITenantMembershipRepository memberships,
    IPrincipalCacheInvalidator principals,
    IUserPrivileges privileges,
    ICurrentUser currentUser,
    ICurrentTenant tenant,
    ISecurityEventLog events,
    IUnitOfWork unitOfWork,
    IClock clock) : ICommandHandler<SetRolePermissionsCommand>
{
    public async Task Handle(SetRolePermissionsCommand command, CancellationToken cancellationToken)
    {
        var role = await roles.GetForUpdateAsync(command.RoleId, cancellationToken)
            ?? throw new DomainException(OrganizationCodes.RoleNotFound, "Role not found.", DomainFault.NotFound);

        var keys = EffectivePrivileges.Clean(command.PermissionKeys);
        OrganizationGuards.EnsureTenantMayGrant(OrganizationGuards.Tenant(tenant), keys);
        OrganizationGuards.EnsureGrantable(privileges, keys.Except(role.PermissionKeys, StringComparer.Ordinal));   // only what is newly granted

        // INV-ORG-010: the administrator's role never loses a permission (the tenant cannot lock itself out).
        if (role.PermissionKeys.Except(keys, StringComparer.Ordinal).Any() && await memberships.PrimaryAdministratorHoldsRoleAsync(role.Id, cancellationToken))
        {
            throw new DomainException(OrganizationCodes.PrimaryAdministratorRoleProtected, "The main administrator's role cannot lose permissions.", DomainFault.Refused);
        }

        role.SetPermissions(keys, clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await principals.EvictRoleMembersAsync(role.Id, cancellationToken);
        await events.WriteAsync(new SecurityEvent(OrganizationSecurityEvents.PrivilegeMatrixChanged, SecurityEvent.Info, tenant.TenantId, currentUser.UserId, $"role:{role.Id}:{RoleChangeAspects.Permissions}"), cancellationToken);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Organization, PermissionAction.Edit)]
public sealed record SetRoleSpecialRightsCommand(Guid RoleId, SpecialRightsDto SpecialRights) : ICommand;

public sealed class SetRoleSpecialRightsValidator : AbstractValidator<SetRoleSpecialRightsCommand>
{
    public SetRoleSpecialRightsValidator()
    {
        RuleFor(command => command.RoleId).NotEmpty();
        RuleFor(command => command.SpecialRights).NotNull();
        RuleFor(command => command.SpecialRights.DiscountCapPercent)
            .InclusiveBetween(0, Domain.Organization.SpecialRights.MaxDiscountCapPercent)
            .When(command => command.SpecialRights?.DiscountCapPercent is not null);
        RuleFor(command => command.SpecialRights.DemographicsEditUpTo).NotEmpty().IsEnumName(typeof(DemographicsEditLimit), caseSensitive: false).When(command => command.SpecialRights is not null);
        RuleFor(command => command.SpecialRights.ReportDaysLimit)
            .InclusiveBetween(0, Domain.Organization.SpecialRights.MaxReportDaysLimit)
            .When(command => command.SpecialRights?.ReportDaysLimit is not null);
    }
}

public sealed class SetRoleSpecialRightsHandler(IRoleRepository roles, IPrincipalCacheInvalidator principals, ISecurityEventLog events, ICurrentUser currentUser, ICurrentTenant tenant, IUnitOfWork unitOfWork, IClock clock) : ICommandHandler<SetRoleSpecialRightsCommand>
{
    public async Task Handle(SetRoleSpecialRightsCommand command, CancellationToken cancellationToken)
    {
        var role = await roles.GetForUpdateAsync(command.RoleId, cancellationToken)
            ?? throw new DomainException(OrganizationCodes.RoleNotFound, "Role not found.", DomainFault.NotFound);

        role.SetSpecialRights(OrganizationGuards.ToDomain(command.SpecialRights), clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await principals.EvictRoleMembersAsync(role.Id, cancellationToken);
        await events.WriteAsync(new SecurityEvent(OrganizationSecurityEvents.PrivilegeMatrixChanged, SecurityEvent.Info, tenant.TenantId, currentUser.UserId, $"role:{role.Id}:{RoleChangeAspects.SpecialRights}"), cancellationToken);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Organization, PermissionAction.Edit)]
public sealed record SetRoleResultRightsCommand(Guid RoleId, IReadOnlyList<ResultRightDto> Rights) : ICommand;

public sealed class SetRoleResultRightsValidator : AbstractValidator<SetRoleResultRightsCommand>
{
    public SetRoleResultRightsValidator()
    {
        RuleFor(command => command.RoleId).NotEmpty();
        RuleFor(command => command.Rights).NotNull().Must(rights => rights is null || rights.Count <= Role.MaxResultRights);
        RuleForEach(command => command.Rights).ChildRules(right => right.RuleFor(item => item.ClinicalGroupId).NotEmpty());
    }
}

public sealed class SetRoleResultRightsHandler(IRoleRepository roles, IPrincipalCacheInvalidator principals, ISecurityEventLog events, ICurrentUser currentUser, ICurrentTenant tenant, IUnitOfWork unitOfWork, IClock clock) : ICommandHandler<SetRoleResultRightsCommand>
{
    public async Task Handle(SetRoleResultRightsCommand command, CancellationToken cancellationToken)
    {
        var role = await roles.GetForUpdateAsync(command.RoleId, cancellationToken)
            ?? throw new DomainException(OrganizationCodes.RoleNotFound, "Role not found.", DomainFault.NotFound);

        role.SetResultRights(command.Rights.Select(OrganizationGuards.ToDomain), clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await principals.EvictRoleMembersAsync(role.Id, cancellationToken);
        await events.WriteAsync(new SecurityEvent(OrganizationSecurityEvents.PrivilegeMatrixChanged, SecurityEvent.Info, tenant.TenantId, currentUser.UserId, $"role:{role.Id}:{RoleChangeAspects.ResultRights}"), cancellationToken);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Organization, PermissionAction.Manage)]
public sealed record DeactivateRoleCommand(Guid RoleId) : ICommand;

public sealed class DeactivateRoleValidator : AbstractValidator<DeactivateRoleCommand>
{
    public DeactivateRoleValidator() => RuleFor(command => command.RoleId).NotEmpty();
}

public sealed class DeactivateRoleHandler(IRoleRepository roles, ITenantMembershipRepository memberships, IUnitOfWork unitOfWork, IClock clock) : ICommandHandler<DeactivateRoleCommand>
{
    public async Task Handle(DeactivateRoleCommand command, CancellationToken cancellationToken)
    {
        var role = await roles.GetForUpdateAsync(command.RoleId, cancellationToken)
            ?? throw new DomainException(OrganizationCodes.RoleNotFound, "Role not found.", DomainFault.NotFound);

        role.Deactivate(await memberships.CountByRoleAsync(role.Id, cancellationToken), clock.UtcNow);   // ORG-011 while it has members
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

[RequirePermissionPolicy(PermissionCatalog.Organization, PermissionAction.Manage)]
public sealed record ActivateRoleCommand(Guid RoleId) : ICommand;

public sealed class ActivateRoleValidator : AbstractValidator<ActivateRoleCommand>
{
    public ActivateRoleValidator() => RuleFor(command => command.RoleId).NotEmpty();
}

public sealed class ActivateRoleHandler(IRoleRepository roles, IUnitOfWork unitOfWork, IClock clock) : ICommandHandler<ActivateRoleCommand>
{
    public async Task Handle(ActivateRoleCommand command, CancellationToken cancellationToken)
    {
        var role = await roles.GetForUpdateAsync(command.RoleId, cancellationToken)
            ?? throw new DomainException(OrganizationCodes.RoleNotFound, "Role not found.", DomainFault.NotFound);

        role.Reactivate(clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
