using FluentValidation;
using Platform.Application.Abstractions.Audit;
using Platform.Application.Abstractions.Messaging;
using Platform.Application.Abstractions.Notifications;
using Platform.Application.Abstractions.Persistence;
using Platform.Application.Abstractions.Security;
using Platform.Application.Abstractions.Settings;
using Platform.Application.Identity;
using Platform.Contracts.PlatformAdmin;
using Platform.Domain.Identity;
using Platform.Domain.PlatformAdmin;
using Platform.SharedKernel.Abstractions;
using Platform.SharedKernel.Primitives;

namespace Platform.Application.PlatformAdmin.Commands;

// The public subscription wizard (CP-02, intake §7 S1–S3). Anonymous and rate-limited at the edge; the request row
// carries the state between the steps, the one-time code proves the manager's mobile, completion provisions the
// tenant exactly like SA-04 (source Subscription, Trial).

/// <summary>The purpose that keys the wizard's one-time codes; a code issued for another flow never verifies here.</summary>
public static class SubscriptionOtp
{
    public const string Purpose = "subscription";

    /// <summary>Keeps the dial code and the last three digits: <c>+20*******678</c>.</summary>
    public static string Mask(string mobile)
    {
        ArgumentNullException.ThrowIfNull(mobile);
        if (mobile.Length <= 6)
        {
            return new string('*', mobile.Length);
        }

        var head = mobile.StartsWith('+') ? Math.Min(4, mobile.Length - 3) : 0;
        return mobile[..head] + new string('*', mobile.Length - head - 3) + mobile[^3..];
    }
}

/// <summary>S1 Start: validates the details against the country and sends a code to the manager's mobile.</summary>
[AllowUnauthenticated]
public sealed record StartSubscriptionCommand(
    string LabNameEn,
    string LabNameAr,
    Guid CountryId,
    Guid GovernorateId,
    string Address,
    string Mobile,
    string? LicenceNo,
    string ManagerName,
    string ManagerMobile,
    string? Email,
    string? Language) : ICommand<SubscriptionStarted>;

public sealed class StartSubscriptionValidator : AbstractValidator<StartSubscriptionCommand>
{
    public StartSubscriptionValidator()
    {
        RuleFor(command => command.LabNameEn).NotEmpty().MaximumLength(Tenant.MaxNameLength);
        RuleFor(command => command.LabNameAr).NotEmpty().MaximumLength(Tenant.MaxNameLength);
        RuleFor(command => command.CountryId).NotEmpty();
        RuleFor(command => command.GovernorateId).NotEmpty();
        RuleFor(command => command.Address).NotEmpty().MaximumLength(Tenant.MaxAddressLength);
        RuleFor(command => command.Mobile).NotEmpty().MaximumLength(Tenant.MaxMobileLength + 10);
        RuleFor(command => command.LicenceNo).MaximumLength(Tenant.MaxLicenceLength);
        RuleFor(command => command.ManagerName).NotEmpty().MaximumLength(Tenant.MaxManagerNameLength);
        RuleFor(command => command.ManagerMobile).NotEmpty().MaximumLength(Tenant.MaxMobileLength + 10);
        RuleFor(command => command.Email).MaximumLength(Tenant.MaxEmailLength);
        RuleFor(command => command.Language).MaximumLength(5);
    }
}

public sealed class StartSubscriptionHandler(
    ICountryRepository countries,
    IGovernorateRepository governorates,
    ISubscriptionRequestRepository requests,
    IOtpService otp,
    SessionPolicy sessionPolicy,
    ISecurityEventLog events,
    IUnitOfWork unitOfWork,
    IClock clock) : ICommandHandler<StartSubscriptionCommand, SubscriptionStarted>
{
    public async Task<SubscriptionStarted> Handle(StartSubscriptionCommand command, CancellationToken cancellationToken)
    {
        var country = await countries.GetForUpdateAsync(command.CountryId, cancellationToken)
            ?? throw new DomainException(PlatformAdminCodes.CountryNotFound, "Country not found.", DomainFault.NotFound);
        var governorate = await governorates.GetForUpdateAsync(command.GovernorateId, cancellationToken)
            ?? throw new DomainException(PlatformAdminCodes.GovernorateNotFound, "Governorate not found.", DomainFault.NotFound);

        var profile = ClientProfile.Create(command.LabNameEn, command.LabNameAr, country, governorate.Id, command.Address, command.Mobile, command.LicenceNo, command.ManagerName, command.ManagerMobile, command.Email);
        var request = SubscriptionRequest.Start(profile, country, governorate, command.Language ?? country.Language, clock.UtcNow);
        requests.Add(request);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        await otp.IssueAsync(SubscriptionOtp.Purpose, request.ManagerMobile, request.Language, cancellationToken, NotificationTemplateCodes.Otp);
        await events.WriteAsync(new SecurityEvent(PlatformAdminSecurityEvents.SubscriptionStarted, SecurityEvent.Info, Detail: $"subscription:{request.Id}"), cancellationToken);
        return new SubscriptionStarted(request.Id, request.ExpiresAtUtc, SubscriptionOtp.Mask(request.ManagerMobile), (int)sessionPolicy.OtpLifetime.TotalSeconds);
    }
}

/// <summary>S1' Re-send the code (at most five codes per request, SUB-004).</summary>
[AllowUnauthenticated]
public sealed record ResendSubscriptionCodeCommand(Guid SubscriptionId) : ICommand<SubscriptionStarted>;

public sealed class ResendSubscriptionCodeValidator : AbstractValidator<ResendSubscriptionCodeCommand>
{
    public ResendSubscriptionCodeValidator() => RuleFor(command => command.SubscriptionId).NotEmpty();
}

public sealed class ResendSubscriptionCodeHandler(ISubscriptionRequestRepository requests, IOtpService otp, SessionPolicy sessionPolicy, IUnitOfWork unitOfWork, IClock clock)
    : ICommandHandler<ResendSubscriptionCodeCommand, SubscriptionStarted>
{
    public async Task<SubscriptionStarted> Handle(ResendSubscriptionCodeCommand command, CancellationToken cancellationToken)
    {
        var request = await SubscriptionLookup.RequireAsync(requests, command.SubscriptionId, cancellationToken);
        request.RecordCodeSent(clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        await otp.IssueAsync(SubscriptionOtp.Purpose, request.ManagerMobile, request.Language, cancellationToken, NotificationTemplateCodes.Otp);
        return new SubscriptionStarted(request.Id, request.ExpiresAtUtc, SubscriptionOtp.Mask(request.ManagerMobile), (int)sessionPolicy.OtpLifetime.TotalSeconds);
    }
}

/// <summary>S2 Verify the code sent to the manager's mobile. Every refusal of the code is SUB-010 (wrong, expired or exhausted).</summary>
[AllowUnauthenticated]
public sealed record VerifySubscriptionCommand(Guid SubscriptionId, string Code) : ICommand;

public sealed class VerifySubscriptionValidator : AbstractValidator<VerifySubscriptionCommand>
{
    public VerifySubscriptionValidator()
    {
        RuleFor(command => command.SubscriptionId).NotEmpty();
        RuleFor(command => command.Code).NotEmpty().MaximumLength(12);
    }
}

public sealed class VerifySubscriptionHandler(ISubscriptionRequestRepository requests, IOtpService otp, ISecurityEventLog events, IUnitOfWork unitOfWork, IClock clock) : ICommandHandler<VerifySubscriptionCommand>
{
    public async Task Handle(VerifySubscriptionCommand command, CancellationToken cancellationToken)
    {
        var request = await SubscriptionLookup.RequireAsync(requests, command.SubscriptionId, cancellationToken);
        request.EnsureCanVerify(clock.UtcNow);   // a dead request never consumes an attempt

        if (!await otp.VerifyAsync(SubscriptionOtp.Purpose, request.ManagerMobile, command.Code.Trim(), cancellationToken))
        {
            await events.WriteAsync(new SecurityEvent(PlatformAdminSecurityEvents.SubscriptionVerifyFailed, SecurityEvent.Warning, Detail: $"subscription:{request.Id}"), cancellationToken);
            throw new DomainException(PlatformAdminCodes.CodeRejected, "The code is not valid, or it has expired.", DomainFault.Validation);
        }

        request.MarkVerified(clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await events.WriteAsync(new SecurityEvent(PlatformAdminSecurityEvents.SubscriptionVerified, SecurityEvent.Info, Detail: $"subscription:{request.Id}"), cancellationToken);
    }
}

/// <summary>S3 Complete: the administrator's credentials provision the tenant (Trial, source Subscription) in one transaction.</summary>
[AllowUnauthenticated]
public sealed record CompleteSubscriptionCommand(Guid SubscriptionId, string Username, string Password) : ICommand<SubscriptionCompletedResponse>;

public sealed class CompleteSubscriptionValidator : AbstractValidator<CompleteSubscriptionCommand>
{
    public CompleteSubscriptionValidator()
    {
        RuleFor(command => command.SubscriptionId).NotEmpty();
        RuleFor(command => command.Username).NotEmpty().MaximumLength(PlatformUser.MaxUsernameLength);
        RuleFor(command => command.Password).NotEmpty().MaximumLength(ClientPasswordPolicy.MaxLength);
    }
}

public sealed class CompleteSubscriptionHandler(
    ISubscriptionRequestRepository requests,
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
    IPortalAddressOptions portal,
    INotificationSender sender,
    ISecurityEventLog events,
    IUnitOfWork unitOfWork,
    IClock clock) : ICommandHandler<CompleteSubscriptionCommand, SubscriptionCompletedResponse>
{
    public async Task<SubscriptionCompletedResponse> Handle(CompleteSubscriptionCommand command, CancellationToken cancellationToken)
    {
        var request = await SubscriptionLookup.RequireAsync(requests, command.SubscriptionId, cancellationToken);
        request.EnsureCanComplete(clock.UtcNow);

        var provisioner = new TenantProvisioner(tenants, wallets, countries, governorates, plans, users, branches, administrators, masterData, hasher, settings, tenantScope, clock);
        var spec = new ProvisionTenantSpec(
            request.NameEn, request.NameAr, request.CountryId, request.GovernorateId, request.Address, request.Mobile, request.LicenceNo,
            request.ManagerName, request.ManagerMobile, request.Email, PlanId: null, PortalSlug: null, TenantSource.Subscription, request.Language,
            command.Username, command.Password);

        var prepared = await provisioner.PrepareAsync(spec, cancellationToken);
        await provisioningScope.RunAsync(prepared.Tenant.Id, async ct =>
        {
            await provisioner.StageAsync(prepared, ct);
            request.Complete(prepared.Tenant.Id, clock.UtcNow);
            await unitOfWork.SaveChangesAsync(ct);   // the request, the tenant and its first rows in the provisioning transaction (intake §11)
            await provisioner.CopyAfterFlushAsync(prepared, ct);
        }, cancellationToken);

        await TenantProvisioner.AnnounceAsync(prepared, portal.PortalDomain, sender, events, cancellationToken);
        await events.WriteAsync(new SecurityEvent(PlatformAdminSecurityEvents.SubscriptionCompleted, SecurityEvent.Info, Detail: $"subscription:{request.Id} tenant:{prepared.Tenant.Id}"), cancellationToken);
        return new SubscriptionCompletedResponse(prepared.Tenant.Id, prepared.Tenant.PortalUrl(portal.PortalDomain), prepared.AdminUsername);
    }
}

internal static class SubscriptionLookup
{
    public static async Task<SubscriptionRequest> RequireAsync(ISubscriptionRequestRepository requests, Guid id, CancellationToken cancellationToken) =>
        await requests.GetForUpdateAsync(id, cancellationToken)
        ?? throw new DomainException(PlatformAdminCodes.SubscriptionNotFound, "Subscription request not found.", DomainFault.NotFound);
}
