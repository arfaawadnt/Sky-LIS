using FluentValidation;
using Platform.Application.Abstractions.Audit;
using Platform.Application.Abstractions.Messaging;
using Platform.Application.Abstractions.Notifications;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Abstractions.Security;
using Platform.Application.Abstractions.Settings;
using Platform.Application.Identity;
using Platform.Contracts.PlatformAdmin;
using Platform.Domain.Authorization;
using Platform.Domain.Identity;
using Platform.Domain.PlatformAdmin;
using Platform.SharedKernel.Abstractions;
using Platform.SharedKernel.Primitives;

namespace Platform.Application.PlatformAdmin.Commands;

// Clients (SA-04, intake §7 P1–P6). Each handler: platform tenant check, load, call the aggregate, commit once.

/// <summary>P1 Provision manually (SA-04 "Add"): an Active client with source Manual, its wallet, branch, administrator and master data.</summary>
[RequirePermissionPolicy(PermissionCatalog.Tenancy, PermissionAction.Create)]
public sealed record ProvisionClientCommand(
    string NameEn,
    string NameAr,
    Guid CountryId,
    Guid GovernorateId,
    string Address,
    string Mobile,
    string? LicenceNo,
    string ManagerName,
    string ManagerMobile,
    string? Email,
    Guid PlanId,
    string? PortalSlug,
    string? Language,
    string AdminUsername,
    string AdminPassword,
    Guid? CityId = null,
    Guid? DistrictId = null,
    Guid? AreaId = null) : ICommand<ProvisionedClient>, IPlatformAdministrationRequest;

public sealed class ProvisionClientValidator : AbstractValidator<ProvisionClientCommand>
{
    public ProvisionClientValidator()
    {
        RuleFor(command => command.NameEn).NotEmpty().MaximumLength(Tenant.MaxNameLength);
        RuleFor(command => command.NameAr).NotEmpty().MaximumLength(Tenant.MaxNameLength);
        RuleFor(command => command.CountryId).NotEmpty();
        RuleFor(command => command.GovernorateId).NotEmpty();
        RuleFor(command => command.Address).NotEmpty().MaximumLength(Tenant.MaxAddressLength);
        RuleFor(command => command.Mobile).NotEmpty().MaximumLength(Tenant.MaxMobileLength + 10);
        RuleFor(command => command.LicenceNo).MaximumLength(Tenant.MaxLicenceLength);
        RuleFor(command => command.ManagerName).NotEmpty().MaximumLength(Tenant.MaxManagerNameLength);
        RuleFor(command => command.ManagerMobile).NotEmpty().MaximumLength(Tenant.MaxMobileLength + 10);
        RuleFor(command => command.Email).MaximumLength(Tenant.MaxEmailLength);
        RuleFor(command => command.PlanId).NotEmpty();
        RuleFor(command => command.PortalSlug).MaximumLength(Tenant.MaxSlugLength);
        RuleFor(command => command.Language).MaximumLength(5);
        RuleFor(command => command.AdminUsername).NotEmpty().MaximumLength(PlatformUser.MaxUsernameLength);
        RuleFor(command => command.AdminPassword).NotEmpty().MaximumLength(ClientPasswordPolicy.MaxLength);
    }
}

public sealed class ProvisionClientHandler(
    ITenantRepository tenants,
    IWalletRepository wallets,
    ICountryRepository countries,
    IGovernorateRepository governorates,
    IPlanRepository plans,
    IPlatformUserRepository users,
    IDefaultBranchProvisioner branches,
    ITenantAdministratorProvisioner administrators,
    IMasterDataCloner masterData,
    IPasswordHasher hasher,
    ITenantSettings settings,
    ICurrentTenantSetter tenantScope,
    ITenantProvisioningScope provisioningScope,
    ICurrentTenant currentTenant,
    IPortalAddressOptions portal,
    INotificationSender sender,
    ISecurityEventLog events,
    IUnitOfWork unitOfWork,
    IClock clock,
    ILocalityLookup localities) : ICommandHandler<ProvisionClientCommand, ProvisionedClient>
{
    public async Task<ProvisionedClient> Handle(ProvisionClientCommand command, CancellationToken cancellationToken)
    {
        PlatformAdminScope.Require(currentTenant);
        var locality = await LocalityChecks.ResolveAsync(localities, command.GovernorateId, command.CityId, command.DistrictId, command.AreaId, cancellationToken);
        var provisioner = new TenantProvisioner(tenants, wallets, countries, governorates, plans, users, branches, administrators, masterData, hasher, settings, tenantScope, clock);
        var spec = new ProvisionTenantSpec(
            command.NameEn, command.NameAr, command.CountryId, command.GovernorateId, command.Address, command.Mobile, command.LicenceNo,
            command.ManagerName, command.ManagerMobile, command.Email, command.PlanId, command.PortalSlug, TenantSource.Manual, command.Language,
            command.AdminUsername, command.AdminPassword, locality);

        var prepared = await provisioner.PrepareAsync(spec, cancellationToken);
        await provisioningScope.RunAsync(prepared.Tenant.Id, async ct =>
        {
            await provisioner.StageAsync(prepared, ct);
            await unitOfWork.SaveChangesAsync(ct);   // the one flush, inside the provisioning transaction (intake §11)
            await provisioner.CopyAfterFlushAsync(prepared, ct);
        }, cancellationToken);

        await TenantProvisioner.AnnounceAsync(prepared, portal.PortalBaseUrl, sender, events, cancellationToken);
        return new ProvisionedClient(prepared.Tenant.Id, prepared.Tenant.PortalUrl(portal.PortalBaseUrl), prepared.AdminUsername);
    }
}

/// <summary>P4/P5 Edit a client's details and plan; LIS-CR-008 C-04: the optional city / district / area (none clears them).</summary>
[RequirePermissionPolicy(PermissionCatalog.Tenancy, PermissionAction.Edit)]
public sealed record UpdateClientCommand(
    Guid ClientId,
    string NameEn,
    string NameAr,
    Guid CountryId,
    Guid GovernorateId,
    string Address,
    string Mobile,
    string? LicenceNo,
    string ManagerName,
    string ManagerMobile,
    string? Email,
    Guid PlanId,
    string? Language,
    Guid? CityId = null,
    Guid? DistrictId = null,
    Guid? AreaId = null) : ICommand, IPlatformAdministrationRequest;

public sealed class UpdateClientValidator : AbstractValidator<UpdateClientCommand>
{
    public UpdateClientValidator()
    {
        RuleFor(command => command.ClientId).NotEmpty();
        RuleFor(command => command.NameEn).NotEmpty().MaximumLength(Tenant.MaxNameLength);
        RuleFor(command => command.NameAr).NotEmpty().MaximumLength(Tenant.MaxNameLength);
        RuleFor(command => command.CountryId).NotEmpty();
        RuleFor(command => command.GovernorateId).NotEmpty();
        RuleFor(command => command.Address).NotEmpty().MaximumLength(Tenant.MaxAddressLength);
        RuleFor(command => command.Mobile).NotEmpty().MaximumLength(Tenant.MaxMobileLength + 10);
        RuleFor(command => command.LicenceNo).MaximumLength(Tenant.MaxLicenceLength);
        RuleFor(command => command.ManagerName).NotEmpty().MaximumLength(Tenant.MaxManagerNameLength);
        RuleFor(command => command.ManagerMobile).NotEmpty().MaximumLength(Tenant.MaxMobileLength + 10);
        RuleFor(command => command.Email).MaximumLength(Tenant.MaxEmailLength);
        RuleFor(command => command.PlanId).NotEmpty();
        RuleFor(command => command.Language).MaximumLength(5);
    }
}

public sealed class UpdateClientHandler(
    ITenantRepository tenants,
    ICountryRepository countries,
    IGovernorateRepository governorates,
    IPlanRepository plans,
    ICurrentTenant currentTenant,
    IUnitOfWork unitOfWork,
    IClock clock,
    ILocalityLookup localities) : ICommandHandler<UpdateClientCommand>
{
    public async Task Handle(UpdateClientCommand command, CancellationToken cancellationToken)
    {
        PlatformAdminScope.Require(currentTenant);
        var tenant = await ClientLookup.RequireAsync(tenants, command.ClientId, cancellationToken);
        var country = await countries.GetForUpdateAsync(command.CountryId, cancellationToken)
            ?? throw new DomainException(PlatformAdminCodes.CountryNotFound, "Country not found.", DomainFault.NotFound);
        var governorate = await governorates.GetForUpdateAsync(command.GovernorateId, cancellationToken)
            ?? throw new DomainException(PlatformAdminCodes.GovernorateNotFound, "Governorate not found.", DomainFault.NotFound);
        var plan = await plans.GetForUpdateAsync(command.PlanId, cancellationToken)
            ?? throw new DomainException(PlatformAdminCodes.PlanNotFound, "Plan not found.", DomainFault.NotFound);

        var profile = ClientProfile.Create(command.NameEn, command.NameAr, country, governorate.Id, command.Address, command.Mobile, command.LicenceNo, command.ManagerName, command.ManagerMobile, command.Email);
        tenant.UpdateProfile(profile, country, governorate, command.Language ?? tenant.PreferredLanguage);
        tenant.Locate(await LocalityChecks.ResolveAsync(localities, governorate.Id, command.CityId, command.DistrictId, command.AreaId, cancellationToken));   // LIS-CR-008 C-04
        tenant.ChangePlan(plan, clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>P2 Activate (Trial/Inactive → Active).</summary>
[RequirePermissionPolicy(PermissionCatalog.Tenancy, PermissionAction.Manage)]
public sealed record ActivateClientCommand(Guid ClientId) : ICommand, IPlatformAdministrationRequest;

public sealed class ActivateClientValidator : AbstractValidator<ActivateClientCommand>
{
    public ActivateClientValidator() => RuleFor(command => command.ClientId).NotEmpty();
}

public sealed class ActivateClientHandler(ITenantRepository tenants, ICurrentTenant currentTenant, ISecurityEventLog events, IUnitOfWork unitOfWork, IClock clock) : ICommandHandler<ActivateClientCommand>
{
    public async Task Handle(ActivateClientCommand command, CancellationToken cancellationToken)
    {
        PlatformAdminScope.Require(currentTenant);
        var tenant = await ClientLookup.RequireAsync(tenants, command.ClientId, cancellationToken);
        tenant.Activate(clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await events.WriteAsync(new SecurityEvent(PlatformAdminSecurityEvents.TenantActivated, SecurityEvent.Info, Detail: $"tenant:{tenant.Id}"), cancellationToken);
    }
}

/// <summary>P3 Deactivate (Trial/Active → Inactive) with a reason; every live session of the client is revoked in the same commit.</summary>
[RequirePermissionPolicy(PermissionCatalog.Tenancy, PermissionAction.Manage)]
public sealed record DeactivateClientCommand(Guid ClientId, string Reason) : ICommand, IPlatformAdministrationRequest;

public sealed class DeactivateClientValidator : AbstractValidator<DeactivateClientCommand>
{
    public DeactivateClientValidator()
    {
        RuleFor(command => command.ClientId).NotEmpty();
        RuleFor(command => command.Reason).NotEmpty().MaximumLength(Tenant.MaxReasonLength);
    }
}

public sealed class DeactivateClientHandler(
    ITenantRepository tenants,
    IPlatformSessionDirectory sessions,
    ISessionPrincipalQuery principals,
    ICurrentTenant currentTenant,
    ISecurityEventLog events,
    IUnitOfWork unitOfWork,
    IClock clock) : ICommandHandler<DeactivateClientCommand>
{
    public async Task Handle(DeactivateClientCommand command, CancellationToken cancellationToken)
    {
        PlatformAdminScope.Require(currentTenant);
        var tenant = await ClientLookup.RequireAsync(tenants, command.ClientId, cancellationToken);
        var now = clock.UtcNow;
        tenant.Deactivate(command.Reason, now);

        var live = await sessions.ListLiveForUpdateAsync(tenant.Id, now, cancellationToken);
        foreach (var session in live)
        {
            session.Revoke(RevocationReason.TenantSuspended, now);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        foreach (var session in live)
        {
            principals.Evict(session.Id);
        }

        await events.WriteAsync(new SecurityEvent(PlatformAdminSecurityEvents.TenantDeactivated, SecurityEvent.Warning, Detail: $"tenant:{tenant.Id} sessions:{live.Count}"), cancellationToken);
    }
}

/// <summary>
/// P6 Reset the laboratory administrator's password (SA-04): the new password arrives through the credential envelope,
/// must be changed at the next sign-in, and every live session of the administrator is revoked. The laboratory is
/// reminded of its portal address by SMS; the password itself is handed over by the platform administrator.
/// </summary>
[RequirePermissionPolicy(PermissionCatalog.Tenancy, PermissionAction.Manage)]
public sealed record ResetClientAdminPasswordCommand(Guid ClientId, string NewPassword) : ICommand, IPlatformAdministrationRequest;

public sealed class ResetClientAdminPasswordValidator : AbstractValidator<ResetClientAdminPasswordCommand>
{
    public ResetClientAdminPasswordValidator()
    {
        RuleFor(command => command.ClientId).NotEmpty();
        RuleFor(command => command.NewPassword).NotEmpty().MaximumLength(ClientPasswordPolicy.MaxLength);
    }
}

public sealed class ResetClientAdminPasswordHandler(
    ITenantRepository tenants,
    IPlatformUserRepository users,
    IRefreshSessionRepository sessions,
    ISessionPrincipalQuery principals,
    IPasswordHasher hasher,
    ITenantSettings settings,
    ICurrentTenantSetter tenantScope,
    ICurrentTenant currentTenant,
    IPortalAddressOptions portal,
    INotificationSender sender,
    ISecurityEventLog events,
    IUnitOfWork unitOfWork,
    IClock clock) : ICommandHandler<ResetClientAdminPasswordCommand>
{
    public async Task Handle(ResetClientAdminPasswordCommand command, CancellationToken cancellationToken)
    {
        PlatformAdminScope.Require(currentTenant);
        var tenant = await ClientLookup.RequireAsync(tenants, command.ClientId, cancellationToken);
        var user = tenant.AdministratorUserId is { } administratorId ? await users.GetForUpdateAsync(administratorId, cancellationToken) : null;
        if (user is null)
        {
            throw new DomainException(PlatformAdminCodes.AdministratorMissing, "The client has no recorded administrator.", DomainFault.Conflict);
        }

        ClientPasswordPolicy.Check(command.NewPassword, user.Username, await ClientPasswordPolicy.MinimumLengthAsync(settings, tenantScope, cancellationToken));

        var now = clock.UtcNow;
        user.ResetPasswordByAdministrator(hasher.Hash(command.NewPassword), mustChange: true, now);
        var live = await sessions.ListLiveForUserAsync(user.Id, cancellationToken);
        foreach (var session in live)
        {
            session.Revoke(RevocationReason.PasswordChanged, now);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);   // the new hash, the stamp rotation and the revocations together
        foreach (var session in live)
        {
            principals.Evict(session.Id);
        }

        var placeholders = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["labName"] = tenant.Name.For(tenant.PreferredLanguage),
            ["url"] = tenant.PortalUrl(portal.PortalBaseUrl),
            ["username"] = user.Username,
        };
        await sender.SendAsync(new NotificationMessage(NotificationChannel.Sms, user.Mobile ?? tenant.ManagerMobile, NotificationTemplateCodes.PortalUrl, tenant.PreferredLanguage, placeholders), cancellationToken);
        await events.WriteAsync(new SecurityEvent(PlatformAdminSecurityEvents.ClientAdminPasswordReset, SecurityEvent.Warning, ActorUserId: user.Id, Detail: $"tenant:{tenant.Id}"), cancellationToken);
    }
}

/// <summary>Loads a client for a command or refuses with CLI-404.</summary>
internal static class ClientLookup
{
    public static async Task<Tenant> RequireAsync(ITenantRepository tenants, Guid clientId, CancellationToken cancellationToken) =>
        await tenants.GetForUpdateAsync(clientId, cancellationToken)
        ?? throw new DomainException(PlatformAdminCodes.ClientNotFound, "Client not found.", DomainFault.NotFound);
}
